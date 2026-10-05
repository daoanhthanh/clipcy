/**
 * Progressive locale detection for neutral root entry.
 * Runs early, does not hide fallback content.
 */

import { detectLocale, buildPath } from '/src/locales.mjs';

(function() {
  const currentPath = window.location.pathname;
  
  // Only run detection on neutral root
  if (currentPath !== '/') {
    return;
  }
  
  const detected = detectLocale();
  const targetPath = buildPath('home', detected);
  
  // Preserve query and hash
  const search = window.location.search;
  const hash = window.location.hash;
  
  // Replace without adding history entry
  window.location.replace(targetPath + search + hash);
})();
