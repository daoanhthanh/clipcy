use clipcy_settings_common::{Settings, SettingsManager};
use serde::{Deserialize, Serialize};
use tauri::Manager;

#[derive(Debug, Serialize, Deserialize)]
pub struct AppState {
    #[serde(skip)]
    settings_manager: SettingsManager,
}

impl Default for AppState {
    fn default() -> Self {
        Self {
            settings_manager: SettingsManager::new().expect("Failed to create settings manager"),
        }
    }
}

#[tauri::command]
fn get_settings(state: tauri::State<AppState>) -> Result<Settings, String> {
    state
        .settings_manager
        .load()
        .map_err(|e| e.to_string())
}

#[tauri::command]
fn save_settings(settings: Settings, state: tauri::State<AppState>) -> Result<(), String> {
    state
        .settings_manager
        .save(&settings)
        .map_err(|e| e.to_string())
}

#[tauri::command]
fn get_autostart_status() -> Result<bool, String> {
    #[cfg(windows)]
    {
        use windows::Win32::Foundation::ERROR_SUCCESS;
        use windows::Win32::System::Registry::{
            RegCloseKey, RegOpenKeyExW, RegQueryValueExW, HKEY, HKEY_CURRENT_USER, KEY_READ, REG_SZ,
        };

        const APP_NAME: &str = "Clipcy";
        const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";

        unsafe {
            let subkey: Vec<u16> = RUN_KEY.encode_utf16().chain(std::iter::once(0)).collect();
            let mut hkey = HKEY::default();

            let result = RegOpenKeyExW(
                HKEY_CURRENT_USER,
                windows::core::PCWSTR(subkey.as_ptr()),
                Some(0),
                KEY_READ,
                &mut hkey,
            );

            if result != ERROR_SUCCESS {
                return Ok(false);
            }

            let value_name: Vec<u16> = APP_NAME.encode_utf16().chain(std::iter::once(0)).collect();
            let mut data_type = REG_SZ;
            let mut data_size: u32 = 0;

            let result = RegQueryValueExW(
                hkey,
                windows::core::PCWSTR(value_name.as_ptr()),
                None,
                Some(&mut data_type),
                None,
                Some(&mut data_size),
            );

            let _ = RegCloseKey(hkey);
            Ok(result == ERROR_SUCCESS)
        }
    }

    #[cfg(not(windows))]
    {
        Ok(false)
    }
}

#[tauri::command]
fn set_autostart(enabled: bool) -> Result<(), String> {
    #[cfg(windows)]
    {
        use windows::Win32::Foundation::ERROR_SUCCESS;
        use windows::Win32::System::Registry::{
            RegCloseKey, RegDeleteValueW, RegOpenKeyExW, RegSetValueExW, HKEY, HKEY_CURRENT_USER,
            KEY_WRITE, REG_SZ,
        };

        const APP_NAME: &str = "Clipcy";
        const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";

        unsafe {
            let subkey: Vec<u16> = RUN_KEY.encode_utf16().chain(std::iter::once(0)).collect();
            let mut hkey = HKEY::default();

            let result = RegOpenKeyExW(
                HKEY_CURRENT_USER,
                windows::core::PCWSTR(subkey.as_ptr()),
                Some(0),
                KEY_WRITE,
                &mut hkey,
            );

            if result != ERROR_SUCCESS {
                return Err(format!("Failed to open registry key: {:?}", result));
            }

            let value_name: Vec<u16> = APP_NAME.encode_utf16().chain(std::iter::once(0)).collect();

            let result = if enabled {
                let exe_path = std::env::current_exe()
                    .map_err(|e| e.to_string())?
                    .to_str()
                    .ok_or("Invalid exe path")?
                    .to_string();

                // Get the main app path (replace settings app path)
                let main_app_path = exe_path
                    .replace("clipcy-settings", "clipcy")
                    .replace("clipcy_settings", "clipcy");

                let value_data: Vec<u16> = main_app_path
                    .encode_utf16()
                    .chain(std::iter::once(0))
                    .collect();

                RegSetValueExW(
                    hkey,
                    windows::core::PCWSTR(value_name.as_ptr()),
                    Some(0),
                    REG_SZ,
                    Some(std::slice::from_raw_parts(
                        value_data.as_ptr() as *const u8,
                        value_data.len() * 2,
                    )),
                )
            } else {
                RegDeleteValueW(hkey, windows::core::PCWSTR(value_name.as_ptr()))
            };

            let _ = RegCloseKey(hkey);

            if result != ERROR_SUCCESS && enabled {
                return Err(format!("Failed to modify registry: {:?}", result));
            }

            Ok(())
        }
    }

    #[cfg(not(windows))]
    {
        let _ = enabled;
        Err("Not supported on this platform".to_string())
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tracing_subscriber::fmt::init();

    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(AppState::default())
        .invoke_handler(tauri::generate_handler![
            get_settings,
            save_settings,
            get_autostart_status,
            set_autostart,
        ])
        .setup(|app| {
            let window = app.get_webview_window("main").unwrap();

            // Apply Mica effect on Windows 11, fallback to Acrylic on Windows 10
            #[cfg(target_os = "windows")]
            {
                use window_vibrancy::{apply_mica, apply_acrylic};

                // Try Mica first (Windows 11) - use dark mode variant
                if apply_mica(&window, Some(true)).is_err() {
                    // Fallback to Acrylic (Windows 10)
                    let _ = apply_acrylic(&window, Some((32, 32, 32, 200)));
                }
            }

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
