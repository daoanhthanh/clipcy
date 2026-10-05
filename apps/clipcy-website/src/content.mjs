/**
 * Language-independent verified facts, public destinations, and route model.
 * Prose and labels come from locale catalogs in src/content/*.mjs.
 */

/**
 * @typedef {Object} ContentModel
 * @property {string} version - Workspace version from Cargo.toml
 * @property {string} repositoryUrl - Public repository URL
 * @property {string} repositoryOwner - GitHub owner/repo identifier
 * @property {boolean} hasPublicRelease - Whether a verified public package exists
 * @property {ReleaseInfo | null} release - Release artifact details when available
 * @property {SystemRequirements} requirements - Runtime requirements
 * @property {string[]} supportedPlatforms - Implemented platforms
 * @property {string[]} plannedPlatforms - Planned but not implemented
 * @property {LocaleCode[]} locales - Supported locale codes
 * @property {PageRoute[]} routes - Page type manifest
 */

/**
 * @typedef {Object} ReleaseInfo
 * @property {string} version
 * @property {string} date - ISO date
 * @property {string} url - Direct download URL
 * @property {string} format - 'exe' | 'msix'
 * @property {string} architecture - 'x64' | 'arm64'
 * @property {boolean} signed
 */

/**
 * @typedef {Object} SystemRequirements
 * @property {string} os - e.g., "Windows 10 version 1809+ or Windows 11"
 * @property {string} runtime - e.g., "Windows App SDK Runtime (bundled)"
 */

/**
 * @typedef {'en' | 'zh-CN' | 'ja' | 'ru' | 'ko' | 'vi'} LocaleCode
 */

/**
 * @typedef {Object} PageRoute
 * @property {string} kind - 'home' | 'download' | 'privacy'
 * @property {string} path - URL path template with {locale}
 */

/** @type {ContentModel} */
export const content = {
  version: '0.4.2',
  repositoryUrl: 'https://github.com/daoanhthanh/clipcy',
  repositoryOwner: 'daoanhthanh/clipcy',
  siteUrl: 'https://clipcy.dev',
  
  // No verified public release available during implementation
  hasPublicRelease: false,
  release: null,
  
  requirements: {
    os: 'Windows 10 version 1809+ or Windows 11',
    runtime: 'Windows App SDK Runtime (bundled)'
  },
  
  supportedPlatforms: ['Windows'],
  plannedPlatforms: ['macOS', 'Linux'],
  
  // Must match apps/clipcy-windows-ui/Localization/*.json filenames
  locales: ['en', 'zh-CN', 'ja', 'ru', 'ko', 'vi'],
  
  routes: [
    { kind: 'home', path: '/{locale}/' },
    { kind: 'download', path: '/{locale}/download/' },
    { kind: 'privacy', path: '/{locale}/privacy/' }
  ]
};

/**
 * Local data storage path for Windows
 */
export const dataPath = '%LOCALAPPDATA%\\clipcy\\';

/**
 * Default hotkey
 */
export const defaultHotkey = 'Alt+V';

/**
 * Pin hotkey
 */
export const pinHotkey = 'Ctrl+P';
