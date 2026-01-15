use std::env;

use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::AutostartHandler;
use windows::Win32::Foundation::ERROR_SUCCESS;
use windows::Win32::System::Registry::{
    RegCloseKey, RegDeleteValueW, RegOpenKeyExW, RegQueryValueExW, RegSetValueExW,
    HKEY, HKEY_CURRENT_USER, KEY_READ, KEY_WRITE, REG_SZ,
};

const APP_NAME: &str = "Clipcy";
const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";

/// Windows auto-start handler using registry
pub struct WindowsAutostartHandler;

impl WindowsAutostartHandler {
    pub fn new() -> Self {
        Self
    }

    fn get_exe_path() -> Result<String> {
        env::current_exe()
            .map_err(|e| ClipboardError::PlatformNotSupported(format!("Failed to get exe path: {}", e)))?
            .to_str()
            .map(|s| s.to_string())
            .ok_or_else(|| ClipboardError::PlatformNotSupported("Invalid exe path".to_string()))
    }
}

impl Default for WindowsAutostartHandler {
    fn default() -> Self {
        Self::new()
    }
}

unsafe impl Send for WindowsAutostartHandler {}
unsafe impl Sync for WindowsAutostartHandler {}

impl AutostartHandler for WindowsAutostartHandler {
    fn enable(&self) -> Result<()> {
        let exe_path = Self::get_exe_path()?;

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
                return Err(ClipboardError::PlatformNotSupported(format!(
                    "Failed to open registry key: {:?}",
                    result
                )));
            }

            let value_name: Vec<u16> = APP_NAME.encode_utf16().chain(std::iter::once(0)).collect();
            let value_data: Vec<u16> = exe_path.encode_utf16().chain(std::iter::once(0)).collect();

            let result = RegSetValueExW(
                hkey,
                windows::core::PCWSTR(value_name.as_ptr()),
                Some(0),
                REG_SZ,
                Some(std::slice::from_raw_parts(
                    value_data.as_ptr() as *const u8,
                    value_data.len() * 2,
                )),
            );

            let _ = RegCloseKey(hkey);

            if result != ERROR_SUCCESS {
                return Err(ClipboardError::PlatformNotSupported(format!(
                    "Failed to set registry value: {:?}",
                    result
                )));
            }

            tracing::info!("Auto-start enabled: {}", exe_path);
            Ok(())
        }
    }

    fn disable(&self) -> Result<()> {
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
                return Err(ClipboardError::PlatformNotSupported(format!(
                    "Failed to open registry key: {:?}",
                    result
                )));
            }

            let value_name: Vec<u16> = APP_NAME.encode_utf16().chain(std::iter::once(0)).collect();

            let result = RegDeleteValueW(hkey, windows::core::PCWSTR(value_name.as_ptr()));

            let _ = RegCloseKey(hkey);

            if result != ERROR_SUCCESS {
                tracing::warn!("Auto-start entry not found or failed to delete: {:?}", result);
            } else {
                tracing::info!("Auto-start disabled");
            }

            Ok(())
        }
    }

    fn is_enabled(&self) -> Result<bool> {
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
}
