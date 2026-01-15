use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Arc;
use std::thread;

use parking_lot::RwLock;
use windows::Win32::Foundation::{HGLOBAL, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::System::DataExchange::*;
use windows::Win32::System::Memory::*;
use windows::Win32::System::Ole::CF_UNICODETEXT;
use windows::Win32::UI::WindowsAndMessaging::*;

use clipboard_core::domain::{ClipboardItem, ContentType};
use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::{ClipboardCallback, ClipboardMonitor};

/// Windows clipboard monitor implementation
pub struct WindowsClipboardMonitor {
    running: Arc<AtomicBool>,
    callback: Arc<RwLock<Option<ClipboardCallback>>>,
    // Store HWND as isize to avoid Send/Sync issues with raw pointers
    hwnd: Arc<AtomicIsize>,
}

impl WindowsClipboardMonitor {
    pub fn new() -> Self {
        Self {
            running: Arc::new(AtomicBool::new(false)),
            callback: Arc::new(RwLock::new(None)),
            hwnd: Arc::new(AtomicIsize::new(0)),
        }
    }

    fn get_clipboard_text() -> Option<String> {
        unsafe {
            if OpenClipboard(None).is_err() {
                return None;
            }

            let handle = GetClipboardData(CF_UNICODETEXT.0 as u32);
            if handle.is_err() {
                let _ = CloseClipboard();
                return None;
            }

            let handle = handle.unwrap();
            if handle.0.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            let hglobal = HGLOBAL(handle.0);
            let ptr = GlobalLock(hglobal);
            if ptr.is_null() {
                let _ = CloseClipboard();
                return None;
            }

            // Read wide string
            let mut len = 0;
            let mut p = ptr as *const u16;
            while *p != 0 {
                len += 1;
                p = p.add(1);
            }

            let slice = std::slice::from_raw_parts(ptr as *const u16, len);
            let text = String::from_utf16_lossy(slice);

            let _ = GlobalUnlock(hglobal);
            let _ = CloseClipboard();

            Some(text)
        }
    }
}

impl Default for WindowsClipboardMonitor {
    fn default() -> Self {
        Self::new()
    }
}

unsafe impl Send for WindowsClipboardMonitor {}
unsafe impl Sync for WindowsClipboardMonitor {}

impl ClipboardMonitor for WindowsClipboardMonitor {
    fn start(&self) -> Result<()> {
        if self.running.load(Ordering::SeqCst) {
            return Ok(());
        }

        self.running.store(true, Ordering::SeqCst);

        let running = Arc::clone(&self.running);
        let callback = Arc::clone(&self.callback);
        let hwnd_store = Arc::clone(&self.hwnd);

        thread::spawn(move || {
            unsafe {
                // Create hidden window for clipboard messages
                let class_name = windows::core::w!("ClipboardMonitorClass");

                let wc = WNDCLASSEXW {
                    cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
                    lpfnWndProc: Some(window_proc),
                    lpszClassName: class_name,
                    ..Default::default()
                };

                RegisterClassExW(&wc);

                let hwnd = match CreateWindowExW(
                    WINDOW_EX_STYLE::default(),
                    class_name,
                    windows::core::w!("ClipboardMonitor"),
                    WS_OVERLAPPED,
                    0, 0, 0, 0,
                    None,
                    None,
                    None,
                    None,
                ) {
                    Ok(h) => h,
                    Err(e) => {
                        tracing::error!("Failed to create clipboard monitor window: {}", e);
                        return;
                    }
                };

                hwnd_store.store(hwnd.0 as isize, Ordering::SeqCst);

                // Register as clipboard listener
                let _ = AddClipboardFormatListener(hwnd);

                // Store context in window
                let ctx = Box::new((callback.clone(), running.clone()));
                SetWindowLongPtrW(hwnd, GWLP_USERDATA, Box::into_raw(ctx) as isize);

                // Message loop
                let mut msg = MSG::default();
                while running.load(Ordering::SeqCst) {
                    if PeekMessageW(&mut msg, Some(hwnd), 0, 0, PM_REMOVE).as_bool() {
                        let _ = TranslateMessage(&msg);
                        DispatchMessageW(&msg);
                    }
                    thread::sleep(std::time::Duration::from_millis(10));
                }

                // Cleanup
                let _ = RemoveClipboardFormatListener(hwnd);
                let _ = DestroyWindow(hwnd);
            }
        });

        Ok(())
    }

    fn stop(&self) -> Result<()> {
        self.running.store(false, Ordering::SeqCst);
        Ok(())
    }

    fn is_running(&self) -> bool {
        self.running.load(Ordering::SeqCst)
    }

    fn on_change(&self, callback: ClipboardCallback) {
        *self.callback.write() = Some(callback);
    }

    fn get_current(&self) -> Result<Option<ClipboardItem>> {
        if let Some(text) = Self::get_clipboard_text() {
            Ok(Some(ClipboardItem::new_text(text)))
        } else {
            Ok(None)
        }
    }

    fn set_content(&self, item: &ClipboardItem) -> Result<()> {
        if item.content_type != ContentType::Text {
            return Err(ClipboardError::ClipboardAccess(
                "Only text content supported for now".to_string()
            ));
        }

        unsafe {
            if OpenClipboard(None).is_err() {
                return Err(ClipboardError::ClipboardAccess("Failed to open clipboard".to_string()));
            }

            let _ = EmptyClipboard();

            // Convert to wide string
            let text = String::from_utf8_lossy(&item.data);
            let wide: Vec<u16> = text.encode_utf16().chain(std::iter::once(0)).collect();
            let size = wide.len() * 2;

            let handle = GlobalAlloc(GMEM_MOVEABLE, size);
            if handle.is_err() {
                let _ = CloseClipboard();
                return Err(ClipboardError::ClipboardAccess("Failed to allocate memory".to_string()));
            }

            let handle = handle.unwrap();
            let ptr = GlobalLock(handle);
            std::ptr::copy_nonoverlapping(wide.as_ptr(), ptr as *mut u16, wide.len());
            let _ = GlobalUnlock(handle);

            let _ = SetClipboardData(CF_UNICODETEXT.0 as u32, Some(windows::Win32::Foundation::HANDLE(handle.0)));

            let _ = CloseClipboard();
        }

        Ok(())
    }
}

unsafe extern "system" fn window_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_CLIPBOARDUPDATE => {
            let ctx_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *const (
                Arc<RwLock<Option<ClipboardCallback>>>,
                Arc<AtomicBool>,
            );

            if !ctx_ptr.is_null() {
                let (callback, _) = &*ctx_ptr;

                if let Some(text) = WindowsClipboardMonitor::get_clipboard_text() {
                    let item = ClipboardItem::new_text(text);

                    if let Some(cb) = callback.read().as_ref() {
                        cb(item);
                    }
                }
            }

            LRESULT(0)
        }
        WM_DESTROY => {
            let ctx_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut (
                Arc<RwLock<Option<ClipboardCallback>>>,
                Arc<AtomicBool>,
            );

            if !ctx_ptr.is_null() {
                drop(Box::from_raw(ctx_ptr));
            }

            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}
