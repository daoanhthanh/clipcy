use std::thread;
use std::time::Duration;

use windows::Win32::UI::Input::KeyboardAndMouse::*;

use clipboard_core::error::Result;
use clipboard_core::ports::PasteHandler;

/// Windows paste simulation using SendInput
pub struct WindowsPasteHandler;

impl WindowsPasteHandler {
    pub fn new() -> Self {
        Self
    }
}

impl Default for WindowsPasteHandler {
    fn default() -> Self {
        Self::new()
    }
}

impl PasteHandler for WindowsPasteHandler {
    fn simulate_paste(&self) -> Result<()> {
        self.simulate_paste_delayed(0)
    }

    fn simulate_paste_delayed(&self, delay_ms: u64) -> Result<()> {
        if delay_ms > 0 {
            thread::sleep(Duration::from_millis(delay_ms));
        }

        unsafe {
            let inputs = [
                // Ctrl down
                INPUT {
                    r#type: INPUT_KEYBOARD,
                    Anonymous: INPUT_0 {
                        ki: KEYBDINPUT {
                            wVk: VK_CONTROL,
                            dwFlags: KEYBD_EVENT_FLAGS(0),
                            ..Default::default()
                        },
                    },
                },
                // V down
                INPUT {
                    r#type: INPUT_KEYBOARD,
                    Anonymous: INPUT_0 {
                        ki: KEYBDINPUT {
                            wVk: VK_V,
                            dwFlags: KEYBD_EVENT_FLAGS(0),
                            ..Default::default()
                        },
                    },
                },
                // V up
                INPUT {
                    r#type: INPUT_KEYBOARD,
                    Anonymous: INPUT_0 {
                        ki: KEYBDINPUT {
                            wVk: VK_V,
                            dwFlags: KEYEVENTF_KEYUP,
                            ..Default::default()
                        },
                    },
                },
                // Ctrl up
                INPUT {
                    r#type: INPUT_KEYBOARD,
                    Anonymous: INPUT_0 {
                        ki: KEYBDINPUT {
                            wVk: VK_CONTROL,
                            dwFlags: KEYEVENTF_KEYUP,
                            ..Default::default()
                        },
                    },
                },
            ];

            SendInput(&inputs, std::mem::size_of::<INPUT>() as i32);
        }

        Ok(())
    }
}
