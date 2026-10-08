(() => {
  const storageKey = 'clipcy-theme';
  const system = matchMedia('(prefers-color-scheme: dark)');
  const normalize = value => ['light', 'dark'].includes(value) ? value : null;
  let preference = system.matches ? 'dark' : 'light';
  try {
    const saved = normalize(localStorage.getItem(storageKey));
    if (saved) preference = saved;
    else localStorage.setItem(storageKey, preference);
  } catch {}
  const apply = () => {
    document.documentElement.dataset.theme = preference;
    document.querySelectorAll('[data-theme-wallpaper]').forEach(image => {
      image.src = `/window-wallpaper-${document.documentElement.dataset.theme}.jpg`;
    });
    document.querySelectorAll('[data-theme-toggle]').forEach(button => {
      button.setAttribute('aria-pressed', String(preference === 'dark'));
      button.title = preference === 'dark' ? button.dataset.lightLabel : button.dataset.darkLabel;
    });
  };
  apply();
  window.addEventListener('storage', event => {
    if (event.key === storageKey || event.key === null) {
      preference = normalize(event.newValue) || (system.matches ? 'dark' : 'light');
      apply();
    }
  });
  document.addEventListener('DOMContentLoaded', () => {
    apply();
    document.querySelectorAll('[data-theme-toggle]').forEach(button => {
      button.addEventListener('click', () => {
        preference = preference === 'dark' ? 'light' : 'dark';
        try { localStorage.setItem(storageKey, preference); } catch {}
        apply();
      });
    });
  });
})();
