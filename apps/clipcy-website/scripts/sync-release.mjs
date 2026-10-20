import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const repository = 'daoanhthanh/clipcy';

// GitHub returns releases newest first. Never advertise a partially uploaded release.
export function selectRelease(releases) {
  for (const release of releases) {
    if (release.draft || release.prerelease) continue;
    const match = /^v?(\d+\.\d+\.\d+)$/.exec(release.tag_name);
    if (!match) continue;
    const version = match[1];
    const findAsset = (name) => release.assets.find((asset) =>
      asset.name === name && asset.state === 'uploaded' && asset.size > 0);
    const x64 = findAsset(`Clipcy-Setup-${version}-x64compatible.exe`)
      ?? findAsset(`Clipcy-Setup-${version}-x64.exe`);
    const arm64 = findAsset(`Clipcy-Setup-${version}-arm64.exe`);
    if (x64 && arm64) {
      return {
        version,
        format: 'exe',
        downloads: { x64: x64.browser_download_url, arm64: arm64.browser_download_url }
      };
    }
  }
  return null;
}

async function main() {
  const headers = { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' };
  if (process.env.GH_TOKEN) headers.Authorization = `Bearer ${process.env.GH_TOKEN}`;

  for (let page = 1; ; page++) {
    const response = await fetch(`https://api.github.com/repos/${repository}/releases?per_page=100&page=${page}`, { headers });
    if (!response.ok) throw new Error(`GitHub releases request failed: ${response.status}`);
    const releases = await response.json();
    const release = selectRelease(releases);
    if (release) {
      await writeFile(new URL('../src/release.json', import.meta.url), `${JSON.stringify(release, null, 2)}\n`);
      console.log(`Website downloads: ${release.version} (x64 and ARM64 installers verified in GitHub release assets)`);
      return;
    }
    if (releases.length < 100) throw new Error('No stable release has both uploaded x64 and ARM64 installers');
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  await main();
}
