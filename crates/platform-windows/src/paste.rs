use std::sync::atomic::{AtomicIsize, Ordering};
use std::thread;
use std::time::Duration;

use windows::Win32::Foundation::*;
use windows::Win32::UI::Input::KeyboardAndMouse::*;
use windows::Win32::UI::WindowsAndMessaging::*;

use clipboard_core::error::Result;
use clipboard_core::ports::PasteHandler;

// Store the previous foreground window before showing popup
static PREVIOUS_FOREGROUND: AtomicIsize = AtomicIsize::new(0);

// Window messages for keyboard
const WM_KEYDOWN: u32 = 0x0100;
const WM_KEYUP: u32 = 0x0101;

/// Save the current foreground window (call this before showing popup)
pub fn save_foreground_window() {
    unsafe {
        let hwnd = GetForegroundWindow();
        if !hwnd.0.is_null() {
            PREVIOUS_FOREGROUND.store(hwnd.0 as isize, Ordering::SeqCst);
            tracing::debug!(
                "save_foreground_window: saved HWND {:?}",
                hwnd.0 as isize
            );
        } else {
            tracing::warn!("save_foreground_window: GetForegroundWindow returned null");
        }
    }
}

/// Get the saved foreground window
pub fn get_saved_foreground_window() -> Option<HWND> {
    let val = PREVIOUS_FOREGROUND.load(Ordering::SeqCst);
    if val != 0 {
        Some(HWND(val as *mut std::ffi::c_void))
    } else {
        None
    }
}

/// Clear the saved foreground window
pub fn clear_saved_foreground_window() {
    PREVIOUS_FOREGROUND.store(0, Ordering::SeqCst);
}

/// Windows paste simulation
pub struct WindowsPasteHandler;

impl WindowsPasteHandler {
    pub fn new() -> Self {
        Self
    }

    /// Method 1: Post WM_KEYDOWN/WM_KEYUP directly to window (bypasses keyboard hooks)
    fn post_ctrl_v_messages(&self, hwnd: HWND) -> bool {
        unsafe {
            tracing::debug!("post_ctrl_v_messages: posting keyboard messages to HWND {:?}", hwnd.0 as isize);

            // lParam format: repeat count (1) | scan code | extended key flag | context code | previous state | transition state
            let ctrl_down_lparam = (0x1D << 16) | 1;
            let ctrl_up_lparam = (0x1D << 16) | 1 | (1 << 30) | (1 << 31);
            let v_down_lparam = (0x2F << 16) | 1;
            let v_up_lparam = (0x2F << 16) | 1 | (1 << 30) | (1 << 31);

            // Post Ctrl down
            if PostMessageW(Some(hwnd), WM_KEYDOWN, WPARAM(VK_CONTROL.0 as usize), LPARAM(ctrl_down_lparam)).is_err() {
                tracing::warn!("post_ctrl_v_messages: failed to post Ctrl down");
                return false;
            }

            // Post V down
            if PostMessageW(Some(hwnd), WM_KEYDOWN, WPARAM(VK_V.0 as usize), LPARAM(v_down_lparam)).is_err() {
                tracing::warn!("post_ctrl_v_messages: failed to post V down");
                return false;
            }

            // Small delay
            thread::sleep(Duration::from_millis(10));

            // Post V up
            if PostMessageW(Some(hwnd), WM_KEYUP, WPARAM(VK_V.0 as usize), LPARAM(v_up_lparam)).is_err() {
                tracing::warn!("post_ctrl_v_messages: failed to post V up");
                return false;
            }

            // Post Ctrl up
            if PostMessageW(Some(hwnd), WM_KEYUP, WPARAM(VK_CONTROL.0 as usize), LPARAM(ctrl_up_lparam)).is_err() {
                tracing::warn!("post_ctrl_v_messages: failed to post Ctrl up");
                return false;
            }

            tracing::debug!("post_ctrl_v_messages: successfully posted all keyboard messages");
            true
        }
    }

    /// Method 2: SendInput with special dwExtraInfo to signal "pass through"
    fn simulate_ctrl_v_with_extra_info(&self) {
        unsafe {
            tracing::debug!("simulate_ctrl_v_with_extra_info: sending Ctrl+V with dwExtraInfo");

            // Some remapping software ignores input with non-zero dwExtraInfo
            let extra_info: usize = 0xFFC3D44F;

            let inputs = [
                INPUT {
                    r#type: INPUT_KEYBOARD,
                    Anonymous: INPUT_0 {
                        ki: KEYBDINPUT {
                            wVk: VK_CONTROL,
                            wScan: 0x1D,
                            dwFlags: KEYBD_EVENT_FLAGS(0),
                            dwExtraInfo: extra_info,
                            time: 0,
                        },
                    },
                },
                INPUT {
                    r#type: INPUT_KEYBOARD,
                    Anonymous: INPUT_0 {
                        ki: KEYBDINPUT {
                            wVk: VK_V,
                            wScan: 0x2F,
                            dwFlags: KEYBD_EVENT_FLAGS(0),
                            dwExtraInfo: extra_info,
                            time: 0,
                        },
                    },
                },
                INPUT {
                    r#type: INPUT_KEYBOARD,
                    Anonymous: INPUT_0 {
                        ki: KEYBDINPUT {
                            wVk: VK_V,
                            wScan: 0x2F,
                            dwFlags: KEYEVENTF_KEYUP,
                            dwExtraInfo: extra_info,
                            time: 0,
                        },
                    },
                },
                INPUT {
                    r#type: INPUT_KEYBOARD,
                    Anonymous: INPUT_0 {
                        ki: KEYBDINPUT {
                            wVk: VK_CONTROL,
                            wScan: 0x1D,
                            dwFlags: KEYEVENTF_KEYUP,
                            dwExtraInfo: extra_info,
                            time: 0,
                        },
                    },
                },
            ];

            let sent = SendInput(&inputs, size_of::<INPUT>() as i32);
            tracing::debug!("simulate_ctrl_v_with_extra_info: SendInput sent {} events", sent);
        }
    }

    /// Paste to the saved previous foreground window
    fn do_paste(&self) {
        unsafe {
            tracing::info!("do_paste: starting paste operation");

            let target = match get_saved_foreground_window() {
                Some(hwnd) => {
                    tracing::debug!("do_paste: using saved foreground window HWND {:?}", hwnd.0 as isize);
                    hwnd
                }
                None => {
                    tracing::warn!("do_paste: no saved foreground window, aborting");
                    return;
                }
            };

            if !IsWindow(Some(target)).as_bool() {
                tracing::error!("do_paste: target window is NOT valid!");
                clear_saved_foreground_window();
                return;
            }

            // Restore focus to target window
            tracing::debug!("do_paste: calling SetForegroundWindow");
            let _ = SetForegroundWindow(target);

            // Wait for focus switch to complete
            thread::sleep(Duration::from_millis(100));

            clear_saved_foreground_window();

            // Use SendInput with dwExtraInfo - this should bypass remapping
            self.simulate_ctrl_v_with_extra_info();

            tracing::info!("do_paste: paste operation completed");
        }
    }
}

impl Default for WindowsPasteHandler {
    fn default() -> Self {
        Self::new()
    }
}

impl PasteHandler for WindowsPasteHandler {
    fn simulate_paste(&self) -> Result<()> {
        tracing::debug!("simulate_paste: called");
        self.simulate_paste_delayed(0)
    }

    fn simulate_paste_delayed(&self, delay_ms: u64) -> Result<()> {
        tracing::debug!("simulate_paste_delayed: delay_ms={}", delay_ms);
        if delay_ms > 0 {
            thread::sleep(Duration::from_millis(delay_ms));
        }

        self.do_paste();

        Ok(())
    }
}
