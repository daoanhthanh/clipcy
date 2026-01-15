use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Arc;
use std::thread;
use std::sync::mpsc;

use parking_lot::RwLock;
use windows::Win32::Foundation::*;
use windows::Win32::UI::Shell::*;
use windows::Win32::UI::WindowsAndMessaging::*;

use clipboard_core::domain::{ClipboardItem, Theme};
use clipboard_core::error::{ClipboardError, Result};
use clipboard_core::ports::{UIEvent, UIEventCallback, UIHandler};

use super::popup;

const WM_TRAYICON: u32 = WM_USER + 1;
const ID_TRAY_EXIT: u32 = 2000;
const ID_TRAY_SHOW: u32 = 2001;

enum TrayCommand {
    Show,
    Hide,
    SetTooltip(String),
    ShowPopup(Vec<ClipboardItem>),
}

/// Windows native popup menu implementation with system tray support
pub struct WindowsUIHandler {
    popup_visible: Arc<AtomicBool>,
    callback: Arc<RwLock<Option<UIEventCallback>>>,
    theme: Arc<RwLock<Theme>>,
    tray_visible: Arc<AtomicBool>,
    tray_hwnd: Arc<AtomicIsize>,
    tray_tx: mpsc::Sender<TrayCommand>,
}

impl WindowsUIHandler {
    pub fn new() -> Self {
        let (tray_tx, tray_rx) = mpsc::channel();
        let callback: Arc<RwLock<Option<UIEventCallback>>> = Arc::new(RwLock::new(None));
        let tray_hwnd = Arc::new(AtomicIsize::new(0));
        let tray_visible = Arc::new(AtomicBool::new(false));

        // Start tray message loop thread
        let callback_clone = Arc::clone(&callback);
        let hwnd_store = Arc::clone(&tray_hwnd);
        let visible_store = Arc::clone(&tray_visible);

        thread::spawn(move || {
            unsafe {
                Self::tray_thread(tray_rx, callback_clone, hwnd_store, visible_store);
            }
        });

        // Wait for window creation
        for _ in 0..50 {
            if tray_hwnd.load(Ordering::SeqCst) != 0 {
                break;
            }
            thread::sleep(std::time::Duration::from_millis(10));
        }

        Self {
            popup_visible: Arc::new(AtomicBool::new(false)),
            callback,
            theme: Arc::new(RwLock::new(Theme::Light)),
            tray_visible,
            tray_hwnd,
            tray_tx,
        }
    }

    unsafe fn tray_thread(
        rx: mpsc::Receiver<TrayCommand>,
        callback: Arc<RwLock<Option<UIEventCallback>>>,
        hwnd_store: Arc<AtomicIsize>,
        visible_store: Arc<AtomicBool>,
    ) {
        let class_name = windows::core::w!("ClipcyTrayClass");

        let wc = WNDCLASSEXW {
            cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
            lpfnWndProc: Some(tray_window_proc),
            lpszClassName: class_name,
            ..Default::default()
        };

        RegisterClassExW(&wc);

        let hwnd = match CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            class_name,
            windows::core::w!("Clipcy Tray"),
            WS_OVERLAPPED,
            0, 0, 0, 0,
            None,
            None,
            None,
            None,
        ) {
            Ok(h) => h,
            Err(e) => {
                tracing::error!("Failed to create tray window: {}", e);
                return;
            }
        };

        hwnd_store.store(hwnd.0 as isize, Ordering::SeqCst);

        // Store callback in window data
        let ctx = Box::new(callback.clone());
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, Box::into_raw(ctx) as isize);

        // Message loop
        let mut msg = MSG::default();
        loop {
            // Process Windows messages
            while PeekMessageW(&mut msg, Some(hwnd), 0, 0, PM_REMOVE).as_bool() {
                let _ = TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }

            // Process tray commands
            while let Ok(cmd) = rx.try_recv() {
                match cmd {
                    TrayCommand::Show => {
                        if !visible_store.load(Ordering::SeqCst) {
                            Self::add_tray_icon(hwnd);
                            visible_store.store(true, Ordering::SeqCst);
                        }
                    }
                    TrayCommand::Hide => {
                        if visible_store.load(Ordering::SeqCst) {
                            Self::remove_tray_icon(hwnd);
                            visible_store.store(false, Ordering::SeqCst);
                        }
                    }
                    TrayCommand::SetTooltip(tip) => {
                        Self::update_tray_tooltip(hwnd, &tip);
                    }
                    TrayCommand::ShowPopup(items) => {
                        // Use the modern popup instead of classic TrackPopupMenu
                        popup::show_modern_popup(&items, callback.clone());
                    }
                }
            }

            thread::sleep(std::time::Duration::from_millis(10));
        }
    }

    unsafe fn add_tray_icon(hwnd: HWND) {
        let mut tip: [u16; 128] = [0; 128];
        let tip_str = "Clipcy - Clipboard Manager";
        for (i, c) in tip_str.encode_utf16().enumerate() {
            if i < 127 {
                tip[i] = c;
            }
        }

        // Use default application icon
        let icon = LoadIconW(None, IDI_APPLICATION).unwrap_or_default();

        let mut nid = NOTIFYICONDATAW {
            cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
            hWnd: hwnd,
            uID: 1,
            uFlags: NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage: WM_TRAYICON,
            hIcon: icon,
            szTip: tip,
            ..Default::default()
        };

        let _ = Shell_NotifyIconW(NIM_ADD, &mut nid);
        tracing::info!("System tray icon added");
    }

    unsafe fn remove_tray_icon(hwnd: HWND) {
        let mut nid = NOTIFYICONDATAW {
            cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
            hWnd: hwnd,
            uID: 1,
            ..Default::default()
        };

        let _ = Shell_NotifyIconW(NIM_DELETE, &mut nid);
        tracing::info!("System tray icon removed");
    }

    unsafe fn update_tray_tooltip(hwnd: HWND, tooltip: &str) {
        let mut tip: [u16; 128] = [0; 128];
        for (i, c) in tooltip.encode_utf16().enumerate() {
            if i < 127 {
                tip[i] = c;
            }
        }

        let mut nid = NOTIFYICONDATAW {
            cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
            hWnd: hwnd,
            uID: 1,
            uFlags: NIF_TIP,
            szTip: tip,
            ..Default::default()
        };

        let _ = Shell_NotifyIconW(NIM_MODIFY, &mut nid);
    }

    unsafe fn show_popup_menu(
        hwnd: HWND,
        items: &[ClipboardItem],
        callback: &Arc<RwLock<Option<UIEventCallback>>>,
    ) {
        tracing::info!("show_popup_menu called with {} items on tray thread", items.len());

        let menu = match CreatePopupMenu() {
            Ok(m) => m,
            Err(e) => {
                tracing::error!("Failed to create popup menu: {}", e);
                return;
            }
        };

        // Add clipboard items
        for (i, item) in items.iter().enumerate() {
            let text: Vec<u16> = item.preview
                .chars()
                .take(50)
                .collect::<String>()
                .encode_utf16()
                .chain(std::iter::once(0))
                .collect();

            let mii = MENUITEMINFOW {
                cbSize: std::mem::size_of::<MENUITEMINFOW>() as u32,
                fMask: MIIM_STRING | MIIM_ID,
                wID: (i + 1) as u32,
                dwTypeData: windows::core::PWSTR(text.as_ptr() as *mut u16),
                cch: text.len() as u32 - 1,
                ..Default::default()
            };

            let _ = InsertMenuItemW(menu, i as u32, true, &mii);
        }

        // Add separator
        let sep = MENUITEMINFOW {
            cbSize: std::mem::size_of::<MENUITEMINFOW>() as u32,
            fMask: MIIM_TYPE,
            fType: MFT_SEPARATOR,
            ..Default::default()
        };
        let _ = InsertMenuItemW(menu, items.len() as u32, true, &sep);

        // Add settings and clear options
        let settings_text: Vec<u16> = "Settings..."
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect();
        let settings_item = MENUITEMINFOW {
            cbSize: std::mem::size_of::<MENUITEMINFOW>() as u32,
            fMask: MIIM_STRING | MIIM_ID,
            wID: 1000,
            dwTypeData: windows::core::PWSTR(settings_text.as_ptr() as *mut u16),
            cch: settings_text.len() as u32 - 1,
            ..Default::default()
        };
        let _ = InsertMenuItemW(menu, items.len() as u32 + 1, true, &settings_item);

        let clear_text: Vec<u16> = "Clear History"
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect();
        let clear_item = MENUITEMINFOW {
            cbSize: std::mem::size_of::<MENUITEMINFOW>() as u32,
            fMask: MIIM_STRING | MIIM_ID,
            wID: 1001,
            dwTypeData: windows::core::PWSTR(clear_text.as_ptr() as *mut u16),
            cch: clear_text.len() as u32 - 1,
            ..Default::default()
        };
        let _ = InsertMenuItemW(menu, items.len() as u32 + 2, true, &clear_item);

        // Get cursor position
        let mut point = POINT::default();
        let _ = GetCursorPos(&mut point);
        tracing::info!("Showing popup at cursor position: ({}, {})", point.x, point.y);

        // Required for proper menu behavior
        let _ = SetForegroundWindow(hwnd);

        let items_len = items.len();
        let result = TrackPopupMenu(
            menu,
            TPM_LEFTALIGN | TPM_TOPALIGN | TPM_RETURNCMD,
            point.x,
            point.y,
            None,
            hwnd,
            None,
        );

        tracing::info!("TrackPopupMenu returned: {}", result.0);
        let _ = DestroyMenu(menu);

        // Post empty message to close menu properly
        let _ = PostMessageW(Some(hwnd), WM_NULL, WPARAM(0), LPARAM(0));

        // Handle selection
        let selected_id = result.0 as u32;
        if selected_id > 0 {
            if let Some(cb) = callback.read().as_ref() {
                let event = match selected_id {
                    1000 => UIEvent::SettingsClicked,
                    1001 => UIEvent::ClearHistoryClicked,
                    id if id <= items_len as u32 => UIEvent::ItemSelected((id - 1) as usize),
                    _ => return,
                };
                cb(event);
            }
        }
    }

    unsafe fn show_tray_menu(hwnd: HWND, callback: &Arc<RwLock<Option<UIEventCallback>>>) {
        let menu = match CreatePopupMenu() {
            Ok(m) => m,
            Err(_) => return,
        };

        // Add "Show Clipboard" option
        let show_text: Vec<u16> = "Show Clipboard (Ctrl+Shift+V)"
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect();
        let show_item = MENUITEMINFOW {
            cbSize: std::mem::size_of::<MENUITEMINFOW>() as u32,
            fMask: MIIM_STRING | MIIM_ID,
            wID: ID_TRAY_SHOW,
            dwTypeData: windows::core::PWSTR(show_text.as_ptr() as *mut u16),
            cch: show_text.len() as u32 - 1,
            ..Default::default()
        };
        let _ = InsertMenuItemW(menu, 0, true, &show_item);

        // Add separator
        let sep = MENUITEMINFOW {
            cbSize: std::mem::size_of::<MENUITEMINFOW>() as u32,
            fMask: MIIM_TYPE,
            fType: MFT_SEPARATOR,
            ..Default::default()
        };
        let _ = InsertMenuItemW(menu, 1, true, &sep);

        // Add "Exit" option
        let exit_text: Vec<u16> = "Exit"
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect();
        let exit_item = MENUITEMINFOW {
            cbSize: std::mem::size_of::<MENUITEMINFOW>() as u32,
            fMask: MIIM_STRING | MIIM_ID,
            wID: ID_TRAY_EXIT,
            dwTypeData: windows::core::PWSTR(exit_text.as_ptr() as *mut u16),
            cch: exit_text.len() as u32 - 1,
            ..Default::default()
        };
        let _ = InsertMenuItemW(menu, 2, true, &exit_item);

        // Get cursor position
        let mut point = POINT::default();
        let _ = GetCursorPos(&mut point);

        // Required for proper menu behavior
        let _ = SetForegroundWindow(hwnd);

        let result = TrackPopupMenu(
            menu,
            TPM_RIGHTALIGN | TPM_BOTTOMALIGN | TPM_RETURNCMD,
            point.x,
            point.y,
            None,
            hwnd,
            None,
        );

        let _ = DestroyMenu(menu);

        // Post empty message to close menu properly
        let _ = PostMessageW(Some(hwnd), WM_NULL, WPARAM(0), LPARAM(0));

        // Handle selection
        let selected_id = result.0 as u32;
        if selected_id > 0 {
            if let Some(cb) = callback.read().as_ref() {
                match selected_id {
                    ID_TRAY_EXIT => {
                        tracing::info!("Exit clicked from tray menu");
                        cb(UIEvent::QuitClicked);
                    }
                    ID_TRAY_SHOW => {
                        tracing::info!("Show clipboard clicked from tray menu");
                        // This would trigger showing the clipboard popup
                    }
                    _ => {}
                }
            }
        }
    }
}

impl Default for WindowsUIHandler {
    fn default() -> Self {
        Self::new()
    }
}

unsafe impl Send for WindowsUIHandler {}
unsafe impl Sync for WindowsUIHandler {}

impl UIHandler for WindowsUIHandler {
    fn show_popup(&self, items: &[ClipboardItem]) -> Result<()> {
        tracing::info!("show_popup called with {} items, sending to tray thread", items.len());

        // Send the popup request to the tray thread where we have a proper message pump
        self.tray_tx.send(TrayCommand::ShowPopup(items.to_vec()))
            .map_err(|_| ClipboardError::UI("Failed to send popup command to tray thread".to_string()))?;

        Ok(())
    }

    fn hide_popup(&self) -> Result<()> {
        self.popup_visible.store(false, Ordering::SeqCst);
        // Popup auto-closes when clicking outside
        Ok(())
    }

    fn is_popup_visible(&self) -> bool {
        self.popup_visible.load(Ordering::SeqCst)
    }

    fn set_theme(&self, theme: Theme) -> Result<()> {
        *self.theme.write() = theme;
        // Theme is applied when popup is shown
        Ok(())
    }

    fn on_event(&self, callback: UIEventCallback) {
        *self.callback.write() = Some(callback);
    }

    fn show_tray(&self) -> Result<()> {
        self.tray_tx.send(TrayCommand::Show)
            .map_err(|_| ClipboardError::UI("Failed to send tray command".to_string()))?;
        Ok(())
    }

    fn hide_tray(&self) -> Result<()> {
        self.tray_tx.send(TrayCommand::Hide)
            .map_err(|_| ClipboardError::UI("Failed to send tray command".to_string()))?;
        Ok(())
    }

    fn set_tray_tooltip(&self, tooltip: &str) -> Result<()> {
        self.tray_tx.send(TrayCommand::SetTooltip(tooltip.to_string()))
            .map_err(|_| ClipboardError::UI("Failed to send tray command".to_string()))?;
        Ok(())
    }
}

unsafe extern "system" fn tray_window_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_TRAYICON => {
            let event = lparam.0 as u32;
            if event == WM_RBUTTONUP || event == WM_CONTEXTMENU {
                let ctx_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA)
                    as *const Arc<RwLock<Option<UIEventCallback>>>;

                if !ctx_ptr.is_null() {
                    let callback = &*ctx_ptr;
                    WindowsUIHandler::show_tray_menu(hwnd, callback);
                }
            }
            LRESULT(0)
        }
        WM_DESTROY => {
            let ctx_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA)
                as *mut Arc<RwLock<Option<UIEventCallback>>>;

            if !ctx_ptr.is_null() {
                drop(Box::from_raw(ctx_ptr));
            }
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}
