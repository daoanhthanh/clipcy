/**
 * Locale metadata, matching, and routing logic.
 * Reusable by build and browser code without framework dependencies.
 */

/**
 * @typedef {Object} LocaleMetadata
 * @property {string} code - BCP 47 code
 * @property {string} nativeName - Native language name
 */

/** @type {LocaleMetadata[]} */
export const locales = [
  { code: 'en', nativeName: 'English' },
  { code: 'zh-CN', nativeName: '简体中文' },
  { code: 'ja', nativeName: '日本語' },
  { code: 'ru', nativeName: 'Русский' },
  { code: 'ko', nativeName: '한국어' },
  { code: 'vi', nativeName: 'Tiếng Việt' }
];

const supportedCodes = new Set(locales.map(l => l.code));

/** @type {string[]} - Array of supported locale codes */
export const supportedLocales = locales.map(l => l.code);
const defaultLocale = 'en';

/**
 * Normalize BCP 47 tag to lowercase with preserved case for script/region.
 * @param {string} tag
 * @returns {string}
 */
function normalizeTag(tag) {
  return tag.trim().toLowerCase();
}

/**
 * Match a BCP 47 language tag to a supported locale code.
 * Returns exact match first, then primary language mapping.
 * 
 * @param {string} tag - BCP 47 language tag
 * @returns {string | null} - Supported locale code or null
 */
export function matchLocale(tag) {
  const normalized = normalizeTag(tag);
  
  // Exact match
  if (supportedCodes.has(normalized)) {
    return normalized;
  }
  
  // Extract primary language
  const primary = normalized.split('-')[0];
  
  // Special Chinese mapping: all zh variants → zh-CN
  if (primary === 'zh') {
    return 'zh-CN';
  }
  
  // Regional form mapping (en-GB → en, vi-VN → vi, etc.)
  if (supportedCodes.has(primary)) {
    return primary;
  }
  
  return null;
}

/**
 * Resolve best locale from ordered preferences.
 * Iterates full list before falling back to default.
 * 
 * @param {string[]} preferences - Ordered language preferences
 * @returns {string} - Supported locale code
 */
export function resolveLocale(preferences) {
  for (const pref of preferences) {
    const matched = matchLocale(pref);
    if (matched) {
      return matched;
    }
  }
  return defaultLocale;
}

/**
 * Get saved locale preference from localStorage.
 * 
 * @returns {string | null}
 */
export function getSavedLocale() {
  try {
    const saved = localStorage.getItem('clipcy-locale');
    if (saved && supportedCodes.has(saved)) {
      return saved;
    }
  } catch {
    // Storage exception
  }
  return null;
}

/**
 * Save locale preference to localStorage.
 * 
 * @param {string} code - Supported locale code
 */
export function saveLocale(code) {
  if (!supportedCodes.has(code)) {
    throw new Error(`Unsupported locale: ${code}`);
  }
  try {
    localStorage.setItem('clipcy-locale', code);
  } catch {
    // Storage exception - navigation still proceeds
  }
}

/**
 * Clear saved locale preference.
 */
export function clearSavedLocale() {
  try {
    localStorage.removeItem('clipcy-locale');
  } catch {
    // Storage exception
  }
}

/**
 * Detect appropriate locale from browser and saved preference.
 * Saved preference takes precedence over navigator languages.
 * 
 * @returns {string} - Detected locale code
 */
export function detectLocale() {
  // Saved preference wins
  const saved = getSavedLocale();
  if (saved) {
    return saved;
  }
  
  // Browser language list
  const browserLangs = navigator.languages || [navigator.language || defaultLocale];
  return resolveLocale(browserLangs);
}

/**
 * Build localized path from page kind and locale.
 * 
 * @param {'home' | 'download' | 'privacy'} kind
 * @param {string} locale
 * @returns {string}
 */
export function buildPath(kind, locale) {
  const paths = {
    home: `/${locale}/`,
    download: `/${locale}/download/`,
    privacy: `/${locale}/privacy/`
  };
  return paths[kind] || `/${locale}/`;
}

/**
 * Get counterpart path for a different locale, preserving page kind and hash.
 * 
 * @param {string} currentPath - Current pathname
 * @param {string} currentLocale - Current locale
 * @param {string} targetLocale - Target locale
 * @returns {string}
 */
export function getCounterpartPath(currentPath, currentLocale, targetLocale) {
  // Replace locale segment
  const pattern = new RegExp(`^/${currentLocale}/`);
  return currentPath.replace(pattern, `/${targetLocale}/`);
}
