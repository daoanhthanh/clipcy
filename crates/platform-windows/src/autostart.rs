use std::os::windows::process::CommandExt;
use std::{env, process::Command};

use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::AutostartHandler;
use windows::Win32::Foundation::ERROR_SUCCESS;
use windows::Win32::System::Registry::{
    HKEY, HKEY_CURRENT_USER, KEY_READ, KEY_WRITE, REG_SZ, RegCloseKey, RegDeleteValueW,
    RegOpenKeyExW, RegQueryValueExW, RegSetValueExW,
};

const APP_NAME: &str = "Clipcy";
const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
const CREATE_NO_WINDOW: u32 = 0x08000000;
// Use a per-user scheduled task (starts before Explorer Run key entries)
const TASK_NAME: &str = r"\\Clipcy\\Autorun";

/// Windows auto-start handler using Task Scheduler (preferred) with registry fallback
pub struct WindowsAutostartHandler;

impl WindowsAutostartHandler {
    pub fn new() -> Self {
        Self
    }

    fn get_exe_path() -> Result<String> {
        env::current_exe()
            .map_err(|e| {
                ClipboardError::PlatformNotSupported(format!("Failed to get exe path: {}", e))
            })?
            .to_str()
            .map(|s| s.to_string())
            .ok_or_else(|| ClipboardError::PlatformNotSupported("Invalid exe path".to_string()))
    }

    /// Try to create/update a per-user scheduled task for fast startup
    fn create_scheduled_task(exe_path: &str) -> Result<()> {
        // Delay a couple seconds to let Explorer initialize (mirrors PowerToys)
        let status = Command::new("schtasks")
            .creation_flags(CREATE_NO_WINDOW)
            .args([
                "/Create",
                "/F",
                "/TN",
                TASK_NAME,
                "/SC",
                "ONLOGON",
                "/RL",
                "LIMITED",
                "/IT",
                "/DELAY",
                "0000:03",
                "/TR",
                &format!("\"{}\"", exe_path),
            ])
            .status()
            .map_err(|e| {
                ClipboardError::PlatformNotSupported(format!("Failed to launch schtasks: {}", e))
            })?;

        if status.success() {
            tracing::info!("Auto-start enabled via scheduled task: {}", exe_path);
            Ok(())
        } else {
            Err(ClipboardError::PlatformNotSupported(format!(
                "schtasks /Create failed with status: {:?}",
                status.code()
            )))
        }
    }

    fn delete_scheduled_task() -> Result<bool> {
        let status = Command::new("schtasks")
            .creation_flags(CREATE_NO_WINDOW)
            .args(["/Delete", "/F", "/TN", TASK_NAME])
            .status()
            .map_err(|e| {
                ClipboardError::PlatformNotSupported(format!("Failed to launch schtasks: {}", e))
            })?;
        Ok(status.success())
    }

    fn is_scheduled_task_present() -> Result<bool> {
        let status = Command::new("schtasks")
            .creation_flags(CREATE_NO_WINDOW)
            .args(["/Query", "/TN", TASK_NAME])
            .status()
            .map_err(|e| {
                ClipboardError::PlatformNotSupported(format!("Failed to launch schtasks: {}", e))
            })?;
        Ok(status.success())
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

        // Preferred path: Scheduled Task for earlier, more reliable start
        match Self::create_scheduled_task(&exe_path) {
            Ok(_) => return Ok(()),
            Err(err) => tracing::warn!(
                "Scheduled task setup failed, falling back to registry: {}",
                err
            ),
        }

        // Fallback: registry Run key
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
            // Quote the path to handle spaces
            let value_data_str = format!("\"{}\"", exe_path);
            let value_data: Vec<u16> = value_data_str
                .encode_utf16()
                .chain(std::iter::once(0))
                .collect();

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

            tracing::info!("Auto-start enabled via registry: {}", exe_path);
            Ok(())
        }
    }

    fn disable(&self) -> Result<()> {
        // Try to remove scheduled task first
        if let Ok(true) = Self::delete_scheduled_task() {
            tracing::info!("Auto-start disabled via scheduled task");
            return Ok(());
        }

        // Fallback to registry
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
                tracing::warn!(
                    "Auto-start entry not found or failed to delete: {:?}",
                    result
                );
            } else {
                tracing::info!("Auto-start disabled via registry");
            }

            Ok(())
        }
    }

    fn is_enabled(&self) -> Result<bool> {
        // Scheduled task is authoritative if present
        if let Ok(is_present) = Self::is_scheduled_task_present() {
            if is_present {
                return Ok(true);
            }
        }

        // Fallback: registry Run key
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
