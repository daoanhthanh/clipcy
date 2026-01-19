import { createSignal, onMount, Show } from 'solid-js';
import Sidebar from './components/Sidebar';
import ToggleSwitch from './components/ToggleSwitch';
import NumberInput from './components/NumberInput';
import TextInput from './components/TextInput';
import Dropdown from './components/Dropdown';

declare global {
  interface Window {
    __TAURI__: {
      core: {
        invoke: <T>(cmd: string, args?: Record<string, unknown>) => Promise<T>;
      };
    };
  }
}

type ThemeMode = 'light' | 'dark' | 'system';

interface Settings {
  max_history_items: number;
  hotkey: string;
  theme: ThemeMode;
  start_on_boot: boolean;
  show_notifications: boolean;
  show_preview_panel: boolean;
  excluded_apps: string[];
}

const defaultSettings: Settings = {
  max_history_items: 100,
  hotkey: 'Ctrl+Shift+V',
  theme: 'system',
  start_on_boot: true,
  show_notifications: false,
  show_preview_panel: true,
  excluded_apps: [],
};

const themeOptions = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'Sync with OS' },
];

type Page = 'general' | 'clipboard' | 'hotkeys' | 'appearance' | 'about';

export default function App() {
  const [page, setPage] = createSignal<Page>('general');
  const [settings, setSettings] = createSignal<Settings>(defaultSettings);
  const [loading, setLoading] = createSignal(true);
  const [autostart, setAutostart] = createSignal(true);

  const invoke = window.__TAURI__?.core?.invoke;

  onMount(async () => {
    try {
      if (invoke) {
        const loaded = await invoke<Settings>('get_settings');
        setSettings(loaded);
        const autostartStatus = await invoke<boolean>('get_autostart_status');
        setAutostart(autostartStatus);
      }
    } catch (e) {
      console.error('Failed to load settings:', e);
    } finally {
      setLoading(false);
    }
  });

  const saveSettings = async (newSettings: Settings) => {
    setSettings(newSettings);
    try {
      if (invoke) {
        await invoke('save_settings', { settings: newSettings });
      }
    } catch (e) {
      console.error('Failed to save settings:', e);
    }
  };

  const updateSetting = <K extends keyof Settings>(key: K, value: Settings[K]) => {
    const newSettings = { ...settings(), [key]: value };
    saveSettings(newSettings);
  };

  const toggleAutostart = async (enabled: boolean) => {
    setAutostart(enabled);
    try {
      if (invoke) {
        await invoke('set_autostart', { enabled });
      }
    } catch (e) {
      console.error('Failed to set autostart:', e);
      setAutostart(!enabled);
    }
  };

  return (
    <div class="app">
      <Sidebar activePage={page()} onPageChange={setPage} />
      <main class="main">
        <Show when={!loading()} fallback={<div>Loading...</div>}>
          <Show when={page() === 'general'}>
            <h1 class="page-title">General</h1>

            <section class="section">
              <h2 class="section-title">Startup</h2>
              <div class="settings-card">
                <div class="setting-item">
                  <div class="setting-info">
                    <div class="setting-label">Start on boot</div>
                    <div class="setting-description">Launch Clipcy automatically when you sign in</div>
                  </div>
                  <ToggleSwitch
                    checked={autostart()}
                    onChange={toggleAutostart}
                  />
                </div>
              </div>
            </section>

            <section class="section">
              <h2 class="section-title">Notifications</h2>
              <div class="settings-card">
                <div class="setting-item">
                  <div class="setting-info">
                    <div class="setting-label">Show notifications</div>
                    <div class="setting-description">Display notifications when clipboard content is captured</div>
                  </div>
                  <ToggleSwitch
                    checked={settings().show_notifications}
                    onChange={(v) => updateSetting('show_notifications', v)}
                  />
                </div>
              </div>
            </section>
          </Show>

          <Show when={page() === 'clipboard'}>
            <h1 class="page-title">Clipboard</h1>

            <section class="section">
              <h2 class="section-title">History</h2>
              <div class="settings-card">
                <div class="setting-item">
                  <div class="setting-info">
                    <div class="setting-label">Maximum history items</div>
                    <div class="setting-description">Number of clipboard entries to keep in history</div>
                  </div>
                  <NumberInput
                    value={settings().max_history_items}
                    min={10}
                    max={1000}
                    onChange={(v) => updateSetting('max_history_items', v)}
                  />
                </div>
                <div class="setting-item">
                  <div class="setting-info">
                    <div class="setting-label">Show preview panel</div>
                    <div class="setting-description">Display a preview of clipboard content in the popup</div>
                  </div>
                  <ToggleSwitch
                    checked={settings().show_preview_panel}
                    onChange={(v) => updateSetting('show_preview_panel', v)}
                  />
                </div>
              </div>
            </section>
          </Show>

          <Show when={page() === 'hotkeys'}>
            <h1 class="page-title">Hotkeys</h1>

            <section class="section">
              <h2 class="section-title">Keyboard shortcuts</h2>
              <div class="settings-card">
                <div class="setting-item">
                  <div class="setting-info">
                    <div class="setting-label">Show clipboard popup</div>
                    <div class="setting-description">Keyboard shortcut to open the clipboard history</div>
                  </div>
                  <TextInput
                    value={settings().hotkey}
                    onChange={(v) => updateSetting('hotkey', v)}
                    placeholder="Ctrl+Shift+V"
                  />
                </div>
              </div>
            </section>
          </Show>

          <Show when={page() === 'appearance'}>
            <h1 class="page-title">Appearance</h1>

            <section class="section">
              <h2 class="section-title">Theme</h2>
              <div class="settings-card">
                <div class="setting-item">
                  <div class="setting-info">
                    <div class="setting-label">App theme</div>
                    <div class="setting-description">Select the theme for the application</div>
                  </div>
                  <Dropdown
                    value={settings().theme}
                    options={themeOptions}
                    onChange={(v) => updateSetting('theme', v as ThemeMode)}
                  />
                </div>
              </div>
            </section>
          </Show>

          <Show when={page() === 'about'}>
            <h1 class="page-title">About</h1>

            <section class="section">
              <div class="settings-card">
                <div class="about-info">
                  <div class="about-version">Clipcy v0.1.0</div>
                  <div class="about-copyright">A modern clipboard manager for Windows</div>
                </div>
              </div>
            </section>
          </Show>
        </Show>
      </main>
    </div>
  );
}
