use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, AtomicIsize, AtomicU32, Ordering};
use std::sync::Arc;
use std::thread;

use parking_lot::RwLock;
use std::sync::mpsc;
use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::UI::Input::KeyboardAndMouse::*;
use windows::Win32::UI::WindowsAndMessaging::*;

use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::{HotkeyCallback, HotkeyHandler};

// Custom message to wake up the message loop when a command is ready
const WM_HOTKEY_COMMAND: u32 = WM_USER + 100;

enum HotkeyCommand {
    Register {
        id: u32,
        modifiers: HOT_KEY_MODIFIERS,
        vk: VIRTUAL_KEY,
        response: mpsc::Sender<std::result::Result<(), String>>,
    },
    Unregister {
        id: u32,
    },
}

/// Windows global hotkey handler
pub struct WindowsHotkeyHandler {
    next_id: AtomicU32,
    callbacks: Arc<RwLock<HashMap<u32, HotkeyCallback>>>,
    // Store HWND as isize to avoid Send/Sync issues with raw pointers
    hwnd: Arc<AtomicIsize>,
    running: Arc<AtomicBool>,
    command_tx: mpsc::Sender<HotkeyCommand>,
}

impl WindowsHotkeyHandler {
    pub fn new() -> Self {
        let (command_tx, command_rx) = mpsc::channel();

        let handler = Self {
            next_id: AtomicU32::new(1),
            callbacks: Arc::new(RwLock::new(HashMap::new())),
            hwnd: Arc::new(AtomicIsize::new(0)),
            running: Arc::new(AtomicBool::new(false)),
            command_tx,
        };

        handler.start_message_loop(command_rx);
        handler
    }

    fn start_message_loop(&self, command_rx: mpsc::Receiver<HotkeyCommand>) {
        let callbacks = Arc::clone(&self.callbacks);
        let hwnd_store = Arc::clone(&self.hwnd);
        let running = Arc::clone(&self.running);

        running.store(true, Ordering::SeqCst);

        thread::spawn(move || {
            unsafe {
                let class_name = windows::core::w!("HotkeyHandlerClass");

                let wc = WNDCLASSEXW {
                    cbSize: size_of::<WNDCLASSEXW>() as u32,
                    lpfnWndProc: Some(hotkey_window_proc),
                    lpszClassName: class_name,
                    ..Default::default()
                };

                RegisterClassExW(&wc);

                let hwnd = match CreateWindowExW(
                    WINDOW_EX_STYLE::default(),
                    class_name,
                    windows::core::w!("HotkeyHandler"),
                    WS_OVERLAPPED,
                    0,
                    0,
                    0,
                    0,
                    None,
                    None,
                    None,
                    None,
                ) {
                    Ok(h) => h,
                    Err(e) => {
                        tracing::error!("Failed to create hotkey handler window: {}", e);
                        return;
                    }
                };

                hwnd_store.store(hwnd.0 as isize, Ordering::SeqCst);

                // Store callbacks reference
                let ctx = Box::new(callbacks.clone());
                SetWindowLongPtrW(hwnd, GWLP_USERDATA, Box::into_raw(ctx) as isize);

                // Message loop using proper Windows message wait
                tracing::info!("Hotkey message loop started");
                let mut msg = MSG::default();
                loop {
                    // Use GetMessageW to properly wait for messages
                    // This blocks until a message is available
                    let ret = GetMessageW(&mut msg, None, 0, 0);

                    if ret.0 == -1 {
                        tracing::error!("GetMessageW error");
                        break;
                    }

                    if ret.0 == 0 {
                        // WM_QUIT
                        break;
                    }

                    // Handle WM_HOTKEY directly since it's a thread message
                    if msg.message == WM_HOTKEY {
                        let id = msg.wParam.0 as u32;
                        tracing::info!("WM_HOTKEY received for id {}", id);
                        if let Some(callback) = callbacks.read().get(&id) {
                            tracing::info!("Invoking hotkey callback for id {}", id);
                            callback();
                        } else {
                            tracing::warn!("No callback found for hotkey id {}", id);
                        }
                    }

                    let _ = TranslateMessage(&msg);
                    DispatchMessageW(&msg);

                    // Check if we should stop (after processing current message)
                    if !running.load(Ordering::SeqCst) {
                        break;
                    }

                    // Process any pending hotkey commands
                    while let Ok(cmd) = command_rx.try_recv() {
                        match cmd {
                            HotkeyCommand::Register {
                                id,
                                modifiers,
                                vk,
                                response,
                            } => {
                                tracing::info!(
                                    "Attempting to register hotkey id={} modifiers={:?} vk={}",
                                    id,
                                    modifiers.0,
                                    vk.0
                                );
                                // Use HWND(0) / NULL to post to thread message queue
                                // This is more reliable for global hotkeys
                                let result =
                                    RegisterHotKey(None, id as i32, modifiers, vk.0 as u32);
                                match &result {
                                    Ok(()) => {
                                        tracing::info!("RegisterHotKey succeeded for id {}", id)
                                    }
                                    Err(e) => tracing::error!(
                                        "RegisterHotKey failed for id {}: {:?}",
                                        id,
                                        e
                                    ),
                                }
                                let _ = response.send(result.map_err(|e| e.to_string()));
                            }
                            HotkeyCommand::Unregister { id } => {
                                let _ = UnregisterHotKey(None, id as i32);
                            }
                        }
                    }
                }

                let _ = DestroyWindow(hwnd);
            }
        });

        // Wait for window creation with timeout
        for _ in 0..50 {
            if self.hwnd.load(Ordering::SeqCst) != 0 {
                break;
            }
            thread::sleep(std::time::Duration::from_millis(10));
        }
    }

    fn parse_hotkey(hotkey: &str) -> Option<(HOT_KEY_MODIFIERS, VIRTUAL_KEY)> {
        let parts: Vec<String> = hotkey.split('+').map(|s| s.trim().to_lowercase()).collect();

        let mut modifiers = HOT_KEY_MODIFIERS(0);
        let mut key = None;

        for part in parts {
            match part.as_str() {
                "ctrl" | "control" => modifiers |= MOD_CONTROL,
                "alt" => modifiers |= MOD_ALT,
                "shift" => modifiers |= MOD_SHIFT,
                "win" | "super" => modifiers |= MOD_WIN,
                k => {
                    key = match k {
                        "a" => Some(VK_A),
                        "b" => Some(VK_B),
                        "c" => Some(VK_C),
                        "v" => Some(VK_V),
                        "x" => Some(VK_X),
                        "z" => Some(VK_Z),
                        _ if k.len() == 1 => {
                            let c = k.chars().next().unwrap().to_ascii_uppercase() as u16;
                            Some(VIRTUAL_KEY(c))
                        }
                        _ => None,
                    };
                }
            }
        }

        key.map(|k| (modifiers, k))
    }
}

impl Default for WindowsHotkeyHandler {
    fn default() -> Self {
        Self::new()
    }
}

unsafe impl Send for WindowsHotkeyHandler {}
unsafe impl Sync for WindowsHotkeyHandler {}

impl HotkeyHandler for WindowsHotkeyHandler {
    fn register(&self, hotkey: &str, callback: HotkeyCallback) -> Result<u32> {
        let (modifiers, vk) = Self::parse_hotkey(hotkey).ok_or_else(|| {
            ClipboardError::HotkeyRegistration(format!("Invalid hotkey format: {}", hotkey))
        })?;

        let id = self.next_id.fetch_add(1, Ordering::SeqCst);

        let hwnd_val = self.hwnd.load(Ordering::SeqCst);
        if hwnd_val == 0 {
            return Err(ClipboardError::HotkeyRegistration(
                "Window not ready".to_string(),
            ));
        }

        let hwnd = HWND(hwnd_val as *mut std::ffi::c_void);

        // Send register command to the message loop thread
        let (response_tx, response_rx) = mpsc::channel();
        self.command_tx
            .send(HotkeyCommand::Register {
                id,
                modifiers,
                vk,
                response: response_tx,
            })
            .map_err(|_| {
                ClipboardError::HotkeyRegistration("Failed to send command".to_string())
            })?;

        // Post a message to wake up the message loop
        unsafe {
            let _ = PostMessageW(Some(hwnd), WM_HOTKEY_COMMAND, WPARAM(0), LPARAM(0));
        }

        // Wait for response with timeout
        match response_rx.recv_timeout(std::time::Duration::from_secs(1)) {
            Ok(Ok(())) => {
                self.callbacks.write().insert(id, callback);
                tracing::info!("Registered hotkey {} with id {}", hotkey, id);
                Ok(id)
            }
            Ok(Err(e)) => {
                Err(ClipboardError::HotkeyRegistration(
                    format!("Failed to register hotkey '{}': {} (hotkey may already be in use by another application)", hotkey, e)
                ))
            }
            Err(_) => {
                Err(ClipboardError::HotkeyRegistration("Timeout waiting for hotkey registration".to_string()))
            }
        }
    }

    fn unregister(&self, handle: u32) -> Result<()> {
        let _ = self
            .command_tx
            .send(HotkeyCommand::Unregister { id: handle });
        self.callbacks.write().remove(&handle);
        Ok(())
    }

    fn unregister_all(&self) -> Result<()> {
        let ids: Vec<u32> = self.callbacks.read().keys().copied().collect();
        for id in ids {
            self.unregister(id)?;
        }
        Ok(())
    }
}

unsafe extern "system" fn hotkey_window_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    unsafe {
        match msg {
            WM_HOTKEY => {
                let id = wparam.0 as u32;
                tracing::debug!("WM_HOTKEY received for id {}", id);

                let ctx_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA)
                    as *const Arc<RwLock<HashMap<u32, HotkeyCallback>>>;

                if !ctx_ptr.is_null() {
                    let callbacks = &*ctx_ptr;
                    if let Some(callback) = callbacks.read().get(&id) {
                        tracing::debug!("Invoking callback for hotkey id {}", id);
                        callback();
                    } else {
                        tracing::warn!("No callback found for hotkey id {}", id);
                    }
                } else {
                    tracing::warn!("Callbacks context is null");
                }

                LRESULT(0)
            }
            _ => DefWindowProcW(hwnd, msg, wparam, lparam),
        }
    }
}
