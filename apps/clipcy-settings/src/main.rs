#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    // Single instance check on Windows
    #[cfg(windows)]
    {
        use windows::core::w;
        use windows::Win32::Foundation::GetLastError;
        use windows::Win32::System::Threading::CreateMutexW;
        use windows::Win32::UI::WindowsAndMessaging::{
            FindWindowW, SetForegroundWindow, ShowWindow, SW_RESTORE,
        };

        unsafe {
            // Try to create a mutex
            let _mutex = CreateMutexW(None, true, w!("ClipcySettingsMutex"));

            // Check if another instance is already running
            if GetLastError().0 == 183 {
                // ERROR_ALREADY_EXISTS = 183
                // Find and focus the existing window
                if let Ok(hwnd) = FindWindowW(None, w!("Clipcy Settings")) {
                    if !hwnd.is_invalid() {
                        let _ = ShowWindow(hwnd, SW_RESTORE);
                        let _ = SetForegroundWindow(hwnd);
                    }
                }
                return;
            }
        }
    }

    clipcy_settings_lib::run()
}
