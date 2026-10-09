/** Prefer architecture hints: Windows ARM browsers may have an x64 user-agent. */
export async function detectArchitecture(browser) {
  try {
    const hints = await browser.userAgentData?.getHighEntropyValues(['architecture']);
    if (/^(arm|arm64|aarch64)$/i.test(hints?.architecture || '')) return 'arm64';
    if (/^(x86|x64|x86_64)$/i.test(hints?.architecture || '')) return 'x64';
  } catch {
    // Browsers may withhold hints; retain the manual architecture choice.
  }
  return /\b(arm64|aarch64|arm)\b/i.test(browser.userAgent || '') ? 'arm64' : 'x64';
}
