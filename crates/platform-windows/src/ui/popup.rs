use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Arc;
use std::ffi::c_void;

use parking_lot::RwLock;
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Gdi::*;
use windows::Win32::UI::WindowsAndMessaging::*;

use clipboard_core::domain::ClipboardItem;
use clipboard_core::ports::{UIEvent, UIEventCallback};

const ITEM_HEIGHT: i32 = 36;
const MENU_PADDING: i32 = 4;
const ITEM_PADDING_X: i32 = 12;
const SEPARATOR_HEIGHT: i32 = 9;

// WM_MOUSELEAVE is 0x02A3
const WM_MOUSELEAVE: u32 = 0x02A3;

// Theme colors
struct ThemeColors {
    background: u32,
    text: u32,
    hover: u32,
    separator: u32,
    border: u32,
}

impl ThemeColors {
    fn dark() -> Self {
        Self {
            background: 0x002D2D2D,  // Dark gray
            text: 0x00FFFFFF,         // White
            hover: 0x00404040,        // Lighter gray
            separator: 0x00505050,    // Gray
            border: 0x00505050,       // Gray border
        }
    }

    fn light() -> Self {
        Self {
            background: 0x00FFFFFF,   // White
            text: 0x00000000,         // Black
            hover: 0x00E5E5E5,        // Light gray
            separator: 0x00D0D0D0,    // Light gray
            border: 0x00C0C0C0,       // Border gray
        }
    }
}

struct PopupData {
    items: Vec<MenuItem>,
    hovered_index: Option<usize>,
    callback: Arc<RwLock<Option<UIEventCallback>>>,
    selected_id: Option<u32>,
    colors: ThemeColors,
}

struct MenuItem {
    id: u32,
    text: String,
    is_separator: bool,
}

static POPUP_HWND: AtomicIsize = AtomicIsize::new(0);
static POPUP_VISIBLE: AtomicBool = AtomicBool::new(false);

fn is_dark_mode() -> bool {
    unsafe {
        use windows::Win32::System::Registry::*;

        let mut hkey = HKEY::default();
        let subkey = windows::core::w!("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");

        let result = RegOpenKeyExW(
            HKEY_CURRENT_USER,
            subkey,
            Some(0),
            KEY_READ,
            &mut hkey,
        );

        if result.is_err() {
            return true; // Default to dark mode if can't read
        }

        let mut data: u32 = 1;
        let mut data_size: u32 = std::mem::size_of::<u32>() as u32;
        let value_name = windows::core::w!("AppsUseLightTheme");

        let result = RegQueryValueExW(
            hkey,
            value_name,
            None,
            None,
            Some(&mut data as *mut u32 as *mut u8),
            Some(&mut data_size),
        );

        let _ = RegCloseKey(hkey);

        if result.is_ok() {
            data == 0 // 0 means dark mode, 1 means light mode
        } else {
            true // Default to dark mode
        }
    }
}

pub fn show_modern_popup(
    items: &[ClipboardItem],
    callback: Arc<RwLock<Option<UIEventCallback>>>,
) -> Option<u32> {
    unsafe {
        // Close existing popup if any
        close_popup();

        // Detect system theme
        let colors = if is_dark_mode() {
            ThemeColors::dark()
        } else {
            ThemeColors::light()
        };

        // Build menu items
        let mut menu_items: Vec<MenuItem> = items
            .iter()
            .enumerate()
            .map(|(i, item)| MenuItem {
                id: (i + 1) as u32,
                text: item.preview.chars().take(50).collect(),
                is_separator: false,
            })
            .collect();

        // Add separator
        menu_items.push(MenuItem {
            id: 0,
            text: String::new(),
            is_separator: true,
        });

        // Add Settings and Clear History
        menu_items.push(MenuItem {
            id: 1000,
            text: "Settings...".to_string(),
            is_separator: false,
        });
        menu_items.push(MenuItem {
            id: 1001,
            text: "Clear History".to_string(),
            is_separator: false,
        });

        // Calculate window size
        let width = 280;
        let height = calculate_menu_height(&menu_items);

        // Get cursor position
        let mut point = POINT::default();
        let _ = GetCursorPos(&mut point);

        // Get the monitor where the cursor is located
        let monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        let mut monitor_info = MONITORINFO {
            cbSize: std::mem::size_of::<MONITORINFO>() as u32,
            ..Default::default()
        };
        let _ = GetMonitorInfoW(monitor, &mut monitor_info);

        // Adjust position to stay on the current monitor's screen
        let work_area = monitor_info.rcWork;

        if point.x + width > work_area.right {
            point.x = work_area.right - width;
        }
        if point.x < work_area.left {
            point.x = work_area.left;
        }
        if point.y + height > work_area.bottom {
            point.y = work_area.bottom - height;
        }
        if point.y < work_area.top {
            point.y = work_area.top;
        }

        // Register window class
        let class_name = windows::core::w!("ClipcyModernPopup");
        let wc = WNDCLASSEXW {
            cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
            lpfnWndProc: Some(popup_window_proc),
            lpszClassName: class_name,
            hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
            hbrBackground: HBRUSH(std::ptr::null_mut()),
            ..Default::default()
        };
        RegisterClassExW(&wc);

        // Create popup window - remove WS_EX_NOACTIVATE so we can capture focus
        let hwnd = CreateWindowExW(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            class_name,
            windows::core::w!(""),
            WS_POPUP,
            point.x,
            point.y,
            width,
            height,
            None,
            None,
            None,
            None,
        );

        let hwnd = match hwnd {
            Ok(h) => h,
            Err(e) => {
                tracing::error!("Failed to create popup window: {}", e);
                return None;
            }
        };

        POPUP_HWND.store(hwnd.0 as isize, Ordering::SeqCst);

        // Enable rounded corners on Windows 11 via DwmSetWindowAttribute
        #[repr(C)]
        struct DWM_WINDOW_CORNER_PREFERENCE(i32);
        const DWMWA_WINDOW_CORNER_PREFERENCE: u32 = 33;
        let corner_preference = DWM_WINDOW_CORNER_PREFERENCE(2); // DWMWCP_ROUND

        #[link(name = "dwmapi")]
        extern "system" {
            fn DwmSetWindowAttribute(
                hwnd: HWND,
                dwAttribute: u32,
                pvAttribute: *const c_void,
                cbAttribute: u32,
            ) -> i32;
        }

        let _ = DwmSetWindowAttribute(
            hwnd,
            DWMWA_WINDOW_CORNER_PREFERENCE,
            &corner_preference as *const _ as *const c_void,
            std::mem::size_of::<DWM_WINDOW_CORNER_PREFERENCE>() as u32,
        );

        // Store popup data
        let data = Box::new(PopupData {
            items: menu_items,
            hovered_index: None,
            callback,
            selected_id: None,
            colors,
        });
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, Box::into_raw(data) as isize);

        // Show window and set foreground
        let _ = ShowWindow(hwnd, SW_SHOW);
        let _ = SetForegroundWindow(hwnd);
        let _ = UpdateWindow(hwnd);
        POPUP_VISIBLE.store(true, Ordering::SeqCst);

        tracing::info!("Modern popup shown at ({}, {})", point.x, point.y);

        // Run message loop for this popup
        let mut msg = MSG::default();
        let mut result: Option<u32> = None;

        loop {
            let ret = GetMessageW(&mut msg, None, 0, 0);
            if ret.0 <= 0 {
                break;
            }

            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);

            // Check if popup was closed
            if !POPUP_VISIBLE.load(Ordering::SeqCst) {
                // Get selected item before cleanup
                let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
                if !data_ptr.is_null() {
                    result = (*data_ptr).selected_id;
                }
                break;
            }
        }

        // Cleanup
        close_popup();

        result
    }
}

fn calculate_menu_height(items: &[MenuItem]) -> i32 {
    let mut height = MENU_PADDING * 2;
    for item in items {
        if item.is_separator {
            height += SEPARATOR_HEIGHT;
        } else {
            height += ITEM_HEIGHT;
        }
    }
    height
}

pub fn close_popup() {
    unsafe {
        let hwnd_val = POPUP_HWND.swap(0, Ordering::SeqCst);
        if hwnd_val != 0 {
            let hwnd = HWND(hwnd_val as *mut c_void);

            // Clean up data
            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                drop(Box::from_raw(data_ptr));
            }

            let _ = DestroyWindow(hwnd);
        }
        POPUP_VISIBLE.store(false, Ordering::SeqCst);
    }
}

unsafe extern "system" fn popup_window_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_PAINT => {
            let mut ps = PAINTSTRUCT::default();
            let hdc = BeginPaint(hwnd, &mut ps);

            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &*data_ptr;
                paint_popup(hwnd, hdc, data);
            }

            let _ = EndPaint(hwnd, &ps);
            LRESULT(0)
        }
        WM_MOUSEMOVE => {
            let y = ((lparam.0 >> 16) & 0xFFFF) as i16 as i32;

            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;
                let new_index = get_item_at_y(&data.items, y);

                if new_index != data.hovered_index {
                    data.hovered_index = new_index;
                    let _ = InvalidateRect(Some(hwnd), None, false);
                }
            }

            LRESULT(0)
        }
        WM_MOUSELEAVE => {
            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;
                if data.hovered_index.is_some() {
                    data.hovered_index = None;
                    let _ = InvalidateRect(Some(hwnd), None, false);
                }
            }
            LRESULT(0)
        }
        WM_LBUTTONUP => {
            let y = ((lparam.0 >> 16) & 0xFFFF) as i16 as i32;

            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;
                if let Some(index) = get_item_at_y(&data.items, y) {
                    let item = &data.items[index];
                    if !item.is_separator {
                        data.selected_id = Some(item.id);

                        // Trigger callback
                        if let Some(cb) = data.callback.read().as_ref() {
                            let event = match item.id {
                                1000 => UIEvent::SettingsClicked,
                                1001 => UIEvent::ClearHistoryClicked,
                                id => UIEvent::ItemSelected((id - 1) as usize),
                            };
                            cb(event);
                        }

                        POPUP_VISIBLE.store(false, Ordering::SeqCst);
                        let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
                    }
                }
            }
            LRESULT(0)
        }
        WM_KEYDOWN => {
            if wparam.0 as u32 == 0x1B { // VK_ESCAPE
                POPUP_VISIBLE.store(false, Ordering::SeqCst);
                let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
            }
            LRESULT(0)
        }
        WM_ACTIVATE => {
            // WA_INACTIVE = 0 means window is being deactivated (lost focus)
            if (wparam.0 & 0xFFFF) == 0 {
                POPUP_VISIBLE.store(false, Ordering::SeqCst);
                let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
            }
            LRESULT(0)
        }
        WM_KILLFOCUS => {
            // Window lost keyboard focus
            POPUP_VISIBLE.store(false, Ordering::SeqCst);
            let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

fn get_item_at_y(items: &[MenuItem], y: i32) -> Option<usize> {
    let mut current_y = MENU_PADDING;

    for (i, item) in items.iter().enumerate() {
        let item_height = if item.is_separator { SEPARATOR_HEIGHT } else { ITEM_HEIGHT };

        if y >= current_y && y < current_y + item_height {
            if !item.is_separator {
                return Some(i);
            }
            return None;
        }

        current_y += item_height;
    }

    None
}

unsafe fn paint_popup(hwnd: HWND, hdc: HDC, data: &PopupData) {
    let mut rect = RECT::default();
    let _ = GetClientRect(hwnd, &mut rect);

    // Create memory DC for double buffering
    let mem_dc = CreateCompatibleDC(Some(hdc));
    let mem_bitmap = CreateCompatibleBitmap(hdc, rect.right, rect.bottom);
    let old_bitmap = SelectObject(mem_dc, mem_bitmap.into());

    // Fill background
    let bg_brush = CreateSolidBrush(COLORREF(data.colors.background));
    let _ = FillRect(mem_dc, &rect, bg_brush);
    let _ = DeleteObject(bg_brush.into());

    // Draw items
    let mut y = MENU_PADDING;

    for (i, item) in data.items.iter().enumerate() {
        if item.is_separator {
            // Draw separator line
            let sep_rect = RECT {
                left: ITEM_PADDING_X,
                top: y + SEPARATOR_HEIGHT / 2,
                right: rect.right - ITEM_PADDING_X,
                bottom: y + SEPARATOR_HEIGHT / 2 + 1,
            };
            let sep_brush = CreateSolidBrush(COLORREF(data.colors.separator));
            let _ = FillRect(mem_dc, &sep_rect, sep_brush);
            let _ = DeleteObject(sep_brush.into());
            y += SEPARATOR_HEIGHT;
        } else {
            // Draw hover background
            if data.hovered_index == Some(i) {
                let hover_rect = RECT {
                    left: MENU_PADDING,
                    top: y,
                    right: rect.right - MENU_PADDING,
                    bottom: y + ITEM_HEIGHT,
                };

                // Create rounded rect region for hover
                let hover_brush = CreateSolidBrush(COLORREF(data.colors.hover));
                let rgn = CreateRoundRectRgn(
                    hover_rect.left,
                    hover_rect.top,
                    hover_rect.right,
                    hover_rect.bottom,
                    4, 4,
                );
                let _ = FillRgn(mem_dc, rgn, hover_brush);
                let _ = DeleteObject(rgn.into());
                let _ = DeleteObject(hover_brush.into());
            }

            // Draw text
            let _ = SetBkMode(mem_dc, TRANSPARENT);
            let _ = SetTextColor(mem_dc, COLORREF(data.colors.text));

            // Use Segoe UI font
            let font = CreateFontW(
                -14, 0, 0, 0,
                400, // FW_NORMAL
                0, 0, 0,
                FONT_CHARSET(1), // DEFAULT_CHARSET
                FONT_OUTPUT_PRECISION(0),
                FONT_CLIP_PRECISION(0),
                FONT_QUALITY(0),
                0,
                windows::core::w!("Segoe UI"),
            );
            let old_font = SelectObject(mem_dc, font.into());

            let mut text_rect = RECT {
                left: ITEM_PADDING_X,
                top: y,
                right: rect.right - ITEM_PADDING_X,
                bottom: y + ITEM_HEIGHT,
            };

            let mut text: Vec<u16> = item.text.encode_utf16().chain(std::iter::once(0)).collect();
            let _ = DrawTextW(
                mem_dc,
                &mut text,
                &mut text_rect,
                DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS,
            );

            let _ = SelectObject(mem_dc, old_font);
            let _ = DeleteObject(font.into());

            y += ITEM_HEIGHT;
        }
    }

    // Copy to screen
    let _ = BitBlt(hdc, 0, 0, rect.right, rect.bottom, Some(mem_dc), 0, 0, SRCCOPY);

    // Cleanup
    let _ = SelectObject(mem_dc, old_bitmap);
    let _ = DeleteObject(mem_bitmap.into());
    let _ = DeleteDC(mem_dc);
}
