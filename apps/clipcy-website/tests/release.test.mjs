import test from 'node:test';
import assert from 'node:assert/strict';
import { selectRelease } from '../scripts/sync-release.mjs';

function asset(version, arch, overrides = {}) {
  const name = `Clipcy-Setup-${version}-${arch}.exe`;
  return {
    name,
    state: 'uploaded',
    size: 100,
    browser_download_url: `https://github.com/daoanhthanh/clipcy/releases/download/${version}/${name}`,
    ...overrides
  };
}

function release(version, assets, overrides = {}) {
  return { tag_name: version, draft: false, prerelease: false, assets, ...overrides };
}

test('does not promote a release until both architecture installers are uploaded', () => {
  const previous = release('0.5.1', [asset('0.5.1', 'x64compatible'), asset('0.5.1', 'arm64')]);
  for (const assets of [
    [],
    [asset('0.5.2', 'x64')],
    [asset('0.5.2', 'arm64')],
    [asset('0.5.2', 'x64'), asset('0.5.2', 'arm64', { state: 'new' })],
    [asset('0.5.2', 'x64'), asset('0.5.2', 'arm64', { size: 0 })]
  ]) {
    const selected = selectRelease([release('0.5.2', assets), previous]);
    assert.equal(selected.version, '0.5.1');
    assert.equal(selected.downloads.x64, previous.assets[0].browser_download_url);
    assert.equal(selected.downloads.arm64, previous.assets[1].browser_download_url);
  }
});

test('promotes a complete stable release and uses the actual asset URLs', () => {
  const assets = [asset('0.5.2', 'x64'), asset('0.5.2', 'arm64')];
  assert.deepEqual(selectRelease([release('v0.5.2', assets)]), {
    version: '0.5.2',
    format: 'exe',
    downloads: { x64: assets[0].browser_download_url, arm64: assets[1].browser_download_url }
  });
});

test('ignores drafts and prereleases even when their installers are complete', () => {
  const assets = [asset('0.6.0', 'x64'), asset('0.6.0', 'arm64')];
  assert.equal(selectRelease([
    release('0.6.0', assets, { draft: true }),
    release('0.6.0', assets, { prerelease: true })
  ]), null);
});

test('prefers the compatible x64 installer when both x64 variants exist', () => {
  const compatible = asset('0.5.2', 'x64compatible');
  const selected = selectRelease([release('0.5.2', [
    asset('0.5.2', 'x64'), compatible, asset('0.5.2', 'arm64')
  ])]);
  assert.equal(selected.downloads.x64, compatible.browser_download_url);
});
