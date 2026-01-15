use std::ffi::c_void;
use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Arc;

use parking_lot::RwLock;
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Gdi::*;
use windows::Win32::UI::Input::KeyboardAndMouse::{TrackMouseEvent, TME_LEAVE, TRACKMOUSEEVENT};
use windows::Win32::UI::WindowsAndMessaging::*;

use clipboard_core::domain::{ClipboardItem, ContentType};
use clipboard_core::ports::{UIEvent, UIEventCallback};

const ITEM_HEIGHT: i32 = 36;
const MENU_PADDING: i32 = 4;
const ITEM_PADDING_X: i32 = 12;
const SEPARATOR_HEIGHT: i32 = 9;
const POPUP_WIDTH: i32 = 280;

// Preview panel constants
const PREVIEW_WIDTH: i32 = 400;
const PREVIEW_HEIGHT: i32 = 300;
const PREVIEW_PADDING: i32 = 12;
const PREVIEW_GAP: i32 = 4;
const SCROLLBAR_WIDTH: i32 = 6;
const SCROLLBAR_MIN_HEIGHT: i32 = 20;

// Virtual key codes
const VK_UP: u32 = 0x26;
const VK_DOWN: u32 = 0x28;
const VK_RETURN: u32 = 0x0D;
const VK_ESCAPE: u32 = 0x1B;

// WM_MOUSELEAVE is 0x02A3
const WM_MOUSELEAVE: u32 = 0x02A3;

// Theme colors
#[derive(Clone)]
struct ThemeColors {
    background: u32,
    text: u32,
    hover: u32,
    separator: u32,
    selected: u32,
    scrollbar_track: u32,
    scrollbar_thumb: u32,
}

impl ThemeColors {
    fn dark() -> Self {
        Self {
            background: 0x002D2D2D,
            text: 0x00FFFFFF,
            hover: 0x00404040,
            separator: 0x00505050,
            selected: 0x00505050,
            scrollbar_track: 0x003A3A3A,
            scrollbar_thumb: 0x00888888,
        }
    }

    fn light() -> Self {
        Self {
            background: 0x00FFFFFF,
            text: 0x00000000,
            hover: 0x00E5E5E5,
            separator: 0x00D0D0D0,
            selected: 0x00CCE8FF,
            scrollbar_track: 0x00F0F0F0,
            scrollbar_thumb: 0x00888888,
        }
    }
}

struct PopupData {
    items: Vec<MenuItem>,
    hovered_index: Option<usize>,
    selected_index: Option<usize>,
    callback: Arc<RwLock<Option<UIEventCallback>>>,
    selected_id: Option<u32>,
    colors: ThemeColors,
    preview_hwnd: HWND,
    work_area: RECT,
    popup_rect: RECT,
}

struct MenuItem {
    id: u32,
    text: String,
    is_separator: bool,
    is_image: bool,
    thumbnail: Option<Vec<u8>>,
    original_data: Option<Vec<u8>>,
}

struct PreviewData {
    content: Option<PreviewContent>,
    colors: ThemeColors,
    scroll_y: i32,
    content_height: i32,
}

enum PreviewContent {
    Text(String),
    Image(Vec<u8>),
}

static POPUP_HWND: AtomicIsize = AtomicIsize::new(0);
static PREVIEW_HWND: AtomicIsize = AtomicIsize::new(0);
static POPUP_VISIBLE: AtomicBool = AtomicBool::new(false);
static MOUSE_IN_POPUP: AtomicBool = AtomicBool::new(false);
static MOUSE_IN_PREVIEW: AtomicBool = AtomicBool::new(false);
static KEYBOARD_NAV_ACTIVE: AtomicBool = AtomicBool::new(false);
static LAST_MOUSE_POS: AtomicIsize = AtomicIsize::new(-1);

fn is_dark_mode() -> bool {
    unsafe {
        use windows::Win32::System::Registry::*;

        let mut hkey = HKEY::default();
        let subkey =
            windows::core::w!("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");

        let result = RegOpenKeyExW(HKEY_CURRENT_USER, subkey, Some(0), KEY_READ, &mut hkey);

        if result.is_err() {
            return true;
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
            data == 0
        } else {
            true
        }
    }
}

pub fn show_modern_popup(
    items: &[ClipboardItem],
    callback: Arc<RwLock<Option<UIEventCallback>>>,
) -> Option<u32> {
    unsafe {
        close_popup();

        let colors = if is_dark_mode() {
            ThemeColors::dark()
        } else {
            ThemeColors::light()
        };

        // Build menu items with original data for preview
        let mut menu_items: Vec<MenuItem> = items
            .iter()
            .enumerate()
            .map(|(i, item)| {
                let is_image = item.content_type == ContentType::Image;
                MenuItem {
                    id: (i + 1) as u32,
                    text: item.preview.chars().take(50).collect(),
                    is_separator: false,
                    is_image,
                    thumbnail: item.thumbnail.clone(),
                    original_data: Some(item.data.clone()),
                }
            })
            .collect();

        // Add separator
        menu_items.push(MenuItem {
            id: 0,
            text: String::new(),
            is_separator: true,
            is_image: false,
            thumbnail: None,
            original_data: None,
        });

        // Add Settings and Clear History
        menu_items.push(MenuItem {
            id: 1000,
            text: "Settings...".to_string(),
            is_separator: false,
            is_image: false,
            thumbnail: None,
            original_data: None,
        });
        menu_items.push(MenuItem {
            id: 1001,
            text: "Clear History".to_string(),
            is_separator: false,
            is_image: false,
            thumbnail: None,
            original_data: None,
        });

        let width = POPUP_WIDTH;
        let height = calculate_menu_height(&menu_items);

        let mut point = POINT::default();
        let _ = GetCursorPos(&mut point);

        let monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        let mut monitor_info = MONITORINFO {
            cbSize: std::mem::size_of::<MONITORINFO>() as u32,
            ..Default::default()
        };
        let _ = GetMonitorInfoW(monitor, &mut monitor_info);

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

        let popup_rect = RECT {
            left: point.x,
            top: point.y,
            right: point.x + width,
            bottom: point.y + height,
        };

        // Register popup window class
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

        // Register preview window class
        let preview_class_name = windows::core::w!("ClipcyPreviewPanel");
        let preview_wc = WNDCLASSEXW {
            cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
            lpfnWndProc: Some(preview_window_proc),
            lpszClassName: preview_class_name,
            hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
            hbrBackground: HBRUSH(std::ptr::null_mut()),
            ..Default::default()
        };
        RegisterClassExW(&preview_wc);

        // Create preview window (initially hidden)
        let preview_hwnd = CreateWindowExW(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            preview_class_name,
            windows::core::w!(""),
            WS_POPUP,
            0,
            0,
            PREVIEW_WIDTH,
            PREVIEW_HEIGHT,
            None,
            None,
            None,
            None,
        );

        let preview_hwnd = match preview_hwnd {
            Ok(h) => h,
            Err(_) => HWND(std::ptr::null_mut()),
        };

        if !preview_hwnd.0.is_null() {
            PREVIEW_HWND.store(preview_hwnd.0 as isize, Ordering::SeqCst);
            apply_rounded_corners(preview_hwnd);

            let preview_data = Box::new(PreviewData {
                content: None,
                colors: colors.clone(),
                scroll_y: 0,
                content_height: 0,
            });
            SetWindowLongPtrW(
                preview_hwnd,
                GWLP_USERDATA,
                Box::into_raw(preview_data) as isize,
            );
        }

        // Create popup window
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
        apply_rounded_corners(hwnd);

        let data = Box::new(PopupData {
            items: menu_items,
            hovered_index: None,
            selected_index: None,
            callback,
            selected_id: None,
            colors,
            preview_hwnd,
            work_area,
            popup_rect,
        });
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, Box::into_raw(data) as isize);

        let _ = ShowWindow(hwnd, SW_SHOW);
        let _ = SetForegroundWindow(hwnd);
        let _ = UpdateWindow(hwnd);
        POPUP_VISIBLE.store(true, Ordering::SeqCst);
        MOUSE_IN_POPUP.store(false, Ordering::SeqCst);
        MOUSE_IN_PREVIEW.store(false, Ordering::SeqCst);

        tracing::info!("Modern popup shown at ({}, {})", point.x, point.y);

        let mut msg = MSG::default();
        let mut result: Option<u32> = None;

        loop {
            let ret = GetMessageW(&mut msg, None, 0, 0);
            if ret.0 <= 0 {
                break;
            }

            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);

            if !POPUP_VISIBLE.load(Ordering::SeqCst) {
                let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
                if !data_ptr.is_null() {
                    result = (*data_ptr).selected_id;
                }
                break;
            }
        }

        close_popup();

        result
    }
}

fn apply_rounded_corners(hwnd: HWND) {
    unsafe {
        #[repr(C)]
        struct DWM_WINDOW_CORNER_PREFERENCE(i32);
        const DWMWA_WINDOW_CORNER_PREFERENCE: u32 = 33;
        let corner_preference = DWM_WINDOW_CORNER_PREFERENCE(2);

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
        // Close preview first
        let preview_hwnd_val = PREVIEW_HWND.swap(0, Ordering::SeqCst);
        if preview_hwnd_val != 0 {
            let hwnd = HWND(preview_hwnd_val as *mut c_void);
            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PreviewData;
            if !data_ptr.is_null() {
                drop(Box::from_raw(data_ptr));
            }
            let _ = DestroyWindow(hwnd);
        }

        // Close popup
        let hwnd_val = POPUP_HWND.swap(0, Ordering::SeqCst);
        if hwnd_val != 0 {
            let hwnd = HWND(hwnd_val as *mut c_void);
            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                drop(Box::from_raw(data_ptr));
            }
            let _ = DestroyWindow(hwnd);
        }
        POPUP_VISIBLE.store(false, Ordering::SeqCst);
        MOUSE_IN_POPUP.store(false, Ordering::SeqCst);
        MOUSE_IN_PREVIEW.store(false, Ordering::SeqCst);
        KEYBOARD_NAV_ACTIVE.store(false, Ordering::SeqCst);
        LAST_MOUSE_POS.store(-1, Ordering::SeqCst);
    }
}

/// Calculate the height needed for content, returns (content_height, panel_height)
unsafe fn calculate_preview_height(content: &Option<PreviewContent>) -> i32 {
    let content_height = match content {
        Some(PreviewContent::Text(text)) => {
            // Create a temporary DC to measure text
            let screen_dc = GetDC(None);
            let font = CreateFontW(
                -14,
                0,
                0,
                0,
                400,
                0,
                0,
                0,
                FONT_CHARSET(1),
                FONT_OUTPUT_PRECISION(0),
                FONT_CLIP_PRECISION(0),
                FONT_QUALITY(0),
                0,
                windows::core::w!("Consolas"),
            );
            let old_font = SelectObject(screen_dc, font.into());

            let text_width = PREVIEW_WIDTH - PREVIEW_PADDING * 2 - SCROLLBAR_WIDTH - 4;
            let mut text_rect = RECT {
                left: 0,
                top: 0,
                right: text_width,
                bottom: 10000,
            };

            let mut text_wide: Vec<u16> = text.encode_utf16().chain(std::iter::once(0)).collect();
            let height = DrawTextW(
                screen_dc,
                &mut text_wide,
                &mut text_rect,
                DT_LEFT | DT_TOP | DT_WORDBREAK | DT_CALCRECT,
            );

            let _ = SelectObject(screen_dc, old_font);
            let _ = DeleteObject(font.into());
            let _ = ReleaseDC(None, screen_dc);

            height
        }
        Some(PreviewContent::Image(img_data)) => {
            // Calculate image height based on aspect ratio
            use image::io::Reader as ImageReader;
            use std::io::Cursor;

            if let Some(img) = ImageReader::new(Cursor::new(img_data))
                .with_guessed_format()
                .ok()
                .and_then(|r| r.decode().ok())
            {
                let img_width = img.width() as i32;
                let img_height = img.height() as i32;
                let available_width = PREVIEW_WIDTH - PREVIEW_PADDING * 2;

                let scale_x = available_width as f32 / img_width as f32;
                let scale = scale_x.min(1.0); // Don't upscale

                (img_height as f32 * scale) as i32
            } else {
                100 // Fallback height
            }
        }
        None => 50, // Minimum height for empty content
    };

    // Add padding and clamp to max height
    let panel_height = (content_height + PREVIEW_PADDING * 2)
        .min(PREVIEW_HEIGHT)
        .max(60);
    panel_height
}

/// Calculate the Y position of an item within the popup
fn get_item_y_position(items: &[MenuItem], index: usize) -> i32 {
    let mut y = MENU_PADDING;
    for (i, item) in items.iter().enumerate() {
        if i == index {
            return y;
        }
        if item.is_separator {
            y += SEPARATOR_HEIGHT;
        } else {
            y += ITEM_HEIGHT;
        }
    }
    y
}

fn show_preview(data: &PopupData, index: usize) {
    unsafe {
        if data.preview_hwnd.0.is_null() {
            return;
        }

        let item = &data.items[index];

        // Don't show preview for separators or menu items
        if item.is_separator || item.id >= 1000 {
            hide_preview(data.preview_hwnd);
            return;
        }

        // Get preview content
        let content = if item.is_image {
            item.original_data
                .as_ref()
                .map(|d| PreviewContent::Image(d.clone()))
        } else {
            item.original_data
                .as_ref()
                .map(|d| PreviewContent::Text(String::from_utf8_lossy(d).to_string()))
        };

        // Calculate dynamic height based on content
        let panel_height = calculate_preview_height(&content);

        // Update preview data
        let preview_data_ptr =
            GetWindowLongPtrW(data.preview_hwnd, GWLP_USERDATA) as *mut PreviewData;
        if !preview_data_ptr.is_null() {
            let preview_data = &mut *preview_data_ptr;
            preview_data.content = content;
            preview_data.scroll_y = 0;
        }

        // Calculate preview position (right of popup, or left if not enough space)
        let popup_right = data.popup_rect.right;
        let popup_left = data.popup_rect.left;
        let popup_top = data.popup_rect.top;

        let preview_x = if popup_right + PREVIEW_GAP + PREVIEW_WIDTH <= data.work_area.right {
            popup_right + PREVIEW_GAP
        } else {
            popup_left - PREVIEW_GAP - PREVIEW_WIDTH
        };

        // Calculate Y position aligned with the selected item
        let item_y_in_popup = get_item_y_position(&data.items, index);
        let preview_y = (popup_top + item_y_in_popup)
            .max(data.work_area.top)
            .min(data.work_area.bottom - panel_height);

        let _ = SetWindowPos(
            data.preview_hwnd,
            Some(HWND_TOPMOST),
            preview_x,
            preview_y,
            PREVIEW_WIDTH,
            panel_height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW,
        );

        let _ = InvalidateRect(Some(data.preview_hwnd), None, true);
    }
}

fn hide_preview(hwnd: HWND) {
    unsafe {
        if !hwnd.0.is_null() {
            let _ = ShowWindow(hwnd, SW_HIDE);
        }
    }
}

fn check_hide_preview() {
    if !MOUSE_IN_POPUP.load(Ordering::SeqCst) && !MOUSE_IN_PREVIEW.load(Ordering::SeqCst) {
        unsafe {
            let preview_hwnd_val = PREVIEW_HWND.load(Ordering::SeqCst);
            if preview_hwnd_val != 0 {
                let hwnd = HWND(preview_hwnd_val as *mut c_void);
                let _ = ShowWindow(hwnd, SW_HIDE);
            }
        }
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
            MOUSE_IN_POPUP.store(true, Ordering::SeqCst);

            let current_pos = lparam.0 as isize;
            let last_pos = LAST_MOUSE_POS.load(Ordering::SeqCst);

            // Check if mouse actually moved (not just a spurious event)
            let mouse_moved = last_pos != current_pos;

            if mouse_moved {
                LAST_MOUSE_POS.store(current_pos, Ordering::SeqCst);
                // Mouse moved, disable keyboard nav mode
                KEYBOARD_NAV_ACTIVE.store(false, Ordering::SeqCst);
            }

            let y = ((lparam.0 >> 16) & 0xFFFF) as i16 as i32;

            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;

                // Only update selection if keyboard nav is not active
                if !KEYBOARD_NAV_ACTIVE.load(Ordering::SeqCst) {
                    let new_index = get_item_at_y(&data.items, y);

                    if new_index != data.hovered_index {
                        data.hovered_index = new_index;
                        data.selected_index = new_index;
                        let _ = InvalidateRect(Some(hwnd), None, false);

                        // Show/update preview
                        if let Some(idx) = new_index {
                            show_preview(data, idx);
                        }
                    }
                }

                // Track mouse leave
                let mut tme = TRACKMOUSEEVENT {
                    cbSize: std::mem::size_of::<TRACKMOUSEEVENT>() as u32,
                    dwFlags: TME_LEAVE,
                    hwndTrack: hwnd,
                    dwHoverTime: 0,
                };
                let _ = TrackMouseEvent(&mut tme);
            }

            LRESULT(0)
        }
        WM_MOUSELEAVE => {
            MOUSE_IN_POPUP.store(false, Ordering::SeqCst);

            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;
                data.hovered_index = None;
                let _ = InvalidateRect(Some(hwnd), None, false);
            }

            // Delay check to allow mouse to move to preview
            std::thread::spawn(|| {
                std::thread::sleep(std::time::Duration::from_millis(50));
                check_hide_preview();
            });

            LRESULT(0)
        }
        WM_LBUTTONUP => {
            let y = ((lparam.0 >> 16) & 0xFFFF) as i16 as i32;

            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;
                if let Some(index) = get_item_at_y(&data.items, y) {
                    select_item(data, index, hwnd);
                }
            }
            LRESULT(0)
        }
        WM_KEYDOWN => {
            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;

                match wparam.0 as u32 {
                    VK_ESCAPE => {
                        POPUP_VISIBLE.store(false, Ordering::SeqCst);
                        let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
                    }
                    VK_UP => {
                        navigate_selection(data, hwnd, -1);
                    }
                    VK_DOWN => {
                        navigate_selection(data, hwnd, 1);
                    }
                    VK_RETURN => {
                        if let Some(index) = data.selected_index {
                            select_item(data, index, hwnd);
                        }
                    }
                    _ => {}
                }
            }
            LRESULT(0)
        }
        WM_ACTIVATE => {
            if (wparam.0 & 0xFFFF) == 0 {
                // Check if we're activating the preview window
                let preview_hwnd = PREVIEW_HWND.load(Ordering::SeqCst);
                let activating_hwnd = HWND(lparam.0 as *mut c_void);
                if preview_hwnd != 0 && activating_hwnd.0 as isize == preview_hwnd {
                    return LRESULT(0);
                }

                POPUP_VISIBLE.store(false, Ordering::SeqCst);
                let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
            }
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

unsafe extern "system" fn preview_window_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_PAINT => {
            let mut ps = PAINTSTRUCT::default();
            let hdc = BeginPaint(hwnd, &mut ps);

            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PreviewData;
            if !data_ptr.is_null() {
                let data = &*data_ptr;
                paint_preview(hwnd, hdc, data);
            }

            let _ = EndPaint(hwnd, &ps);
            LRESULT(0)
        }
        WM_MOUSEMOVE => {
            MOUSE_IN_PREVIEW.store(true, Ordering::SeqCst);

            let mut tme = TRACKMOUSEEVENT {
                cbSize: std::mem::size_of::<TRACKMOUSEEVENT>() as u32,
                dwFlags: TME_LEAVE,
                hwndTrack: hwnd,
                dwHoverTime: 0,
            };
            let _ = TrackMouseEvent(&mut tme);

            LRESULT(0)
        }
        WM_MOUSELEAVE => {
            MOUSE_IN_PREVIEW.store(false, Ordering::SeqCst);

            std::thread::spawn(|| {
                std::thread::sleep(std::time::Duration::from_millis(50));
                check_hide_preview();
            });

            LRESULT(0)
        }
        WM_MOUSEWHEEL => {
            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PreviewData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;
                let delta = ((wparam.0 >> 16) as i16) as i32;
                let scroll_amount = delta / 3;

                data.scroll_y = (data.scroll_y - scroll_amount)
                    .max(0)
                    .min((data.content_height - PREVIEW_HEIGHT + PREVIEW_PADDING * 2).max(0));

                let _ = InvalidateRect(Some(hwnd), None, true);
            }
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

fn navigate_selection(data: &mut PopupData, hwnd: HWND, direction: i32) {
    // Enable keyboard navigation mode
    KEYBOARD_NAV_ACTIVE.store(true, Ordering::SeqCst);

    let selectable_items: Vec<usize> = data
        .items
        .iter()
        .enumerate()
        .filter(|(_, item)| !item.is_separator)
        .map(|(i, _)| i)
        .collect();

    if selectable_items.is_empty() {
        return;
    }

    let current_pos = data
        .selected_index
        .and_then(|idx| selectable_items.iter().position(|&i| i == idx))
        .unwrap_or(if direction > 0 {
            selectable_items.len() - 1
        } else {
            0
        });

    let new_pos = if direction > 0 {
        (current_pos + 1) % selectable_items.len()
    } else {
        if current_pos == 0 {
            selectable_items.len() - 1
        } else {
            current_pos - 1
        }
    };

    let new_index = selectable_items[new_pos];
    data.selected_index = Some(new_index);
    data.hovered_index = Some(new_index);

    unsafe {
        let _ = InvalidateRect(Some(hwnd), None, false);
        show_preview(data, new_index);
    }
}

fn select_item(data: &mut PopupData, index: usize, hwnd: HWND) {
    let item = &data.items[index];
    if !item.is_separator {
        data.selected_id = Some(item.id);

        if let Some(cb) = data.callback.read().as_ref() {
            let event = match item.id {
                1000 => UIEvent::SettingsClicked,
                1001 => UIEvent::ClearHistoryClicked,
                id => UIEvent::ItemSelected((id - 1) as usize),
            };
            cb(event);
        }

        POPUP_VISIBLE.store(false, Ordering::SeqCst);
        unsafe {
            let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
        }
    }
}

fn get_item_at_y(items: &[MenuItem], y: i32) -> Option<usize> {
    let mut current_y = MENU_PADDING;

    for (i, item) in items.iter().enumerate() {
        let item_height = if item.is_separator {
            SEPARATOR_HEIGHT
        } else {
            ITEM_HEIGHT
        };

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

    let mem_dc = CreateCompatibleDC(Some(hdc));
    let mem_bitmap = CreateCompatibleBitmap(hdc, rect.right, rect.bottom);
    let old_bitmap = SelectObject(mem_dc, mem_bitmap.into());

    let bg_brush = CreateSolidBrush(COLORREF(data.colors.background));
    let _ = FillRect(mem_dc, &rect, bg_brush);
    let _ = DeleteObject(bg_brush.into());

    let mut y = MENU_PADDING;

    for (i, item) in data.items.iter().enumerate() {
        if item.is_separator {
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
            // Draw selection/hover background
            let is_selected = data.selected_index == Some(i);
            let is_hovered = data.hovered_index == Some(i);

            if is_selected || is_hovered {
                let bg_color = if is_selected {
                    data.colors.selected
                } else {
                    data.colors.hover
                };

                let hover_rect = RECT {
                    left: MENU_PADDING,
                    top: y,
                    right: rect.right - MENU_PADDING,
                    bottom: y + ITEM_HEIGHT,
                };

                let hover_brush = CreateSolidBrush(COLORREF(bg_color));
                let rgn = CreateRoundRectRgn(
                    hover_rect.left,
                    hover_rect.top,
                    hover_rect.right,
                    hover_rect.bottom,
                    4,
                    4,
                );
                let _ = FillRgn(mem_dc, rgn, hover_brush);
                let _ = DeleteObject(rgn.into());
                let _ = DeleteObject(hover_brush.into());
            }

            let _ = SetBkMode(mem_dc, TRANSPARENT);
            let _ = SetTextColor(mem_dc, COLORREF(data.colors.text));

            let font = CreateFontW(
                -14,
                0,
                0,
                0,
                400,
                0,
                0,
                0,
                FONT_CHARSET(1),
                FONT_OUTPUT_PRECISION(0),
                FONT_CLIP_PRECISION(0),
                FONT_QUALITY(0),
                0,
                windows::core::w!("Segoe UI"),
            );
            let old_font = SelectObject(mem_dc, font.into());

            let text_left = if item.is_image && item.thumbnail.is_some() {
                if let Some(ref thumb_data) = item.thumbnail {
                    let slot_size = ITEM_HEIGHT - 4;
                    draw_thumbnail_scaled(mem_dc, thumb_data, ITEM_PADDING_X, y + 2, slot_size);
                }
                ITEM_PADDING_X + ITEM_HEIGHT
            } else {
                ITEM_PADDING_X
            };

            let mut text_rect = RECT {
                left: text_left,
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

    let _ = BitBlt(
        hdc,
        0,
        0,
        rect.right,
        rect.bottom,
        Some(mem_dc),
        0,
        0,
        SRCCOPY,
    );

    let _ = SelectObject(mem_dc, old_bitmap);
    let _ = DeleteObject(mem_bitmap.into());
    let _ = DeleteDC(mem_dc);
}

unsafe fn paint_preview(hwnd: HWND, hdc: HDC, data: &PreviewData) {
    let mut rect = RECT::default();
    let _ = GetClientRect(hwnd, &mut rect);

    let mem_dc = CreateCompatibleDC(Some(hdc));
    let mem_bitmap = CreateCompatibleBitmap(hdc, rect.right, rect.bottom);
    let old_bitmap = SelectObject(mem_dc, mem_bitmap.into());

    let bg_brush = CreateSolidBrush(COLORREF(data.colors.background));
    let _ = FillRect(mem_dc, &rect, bg_brush);
    let _ = DeleteObject(bg_brush.into());

    let mut needs_scrollbar = false;
    let mut content_height = 0i32;

    if let Some(ref content) = data.content {
        match content {
            PreviewContent::Text(text) => {
                let _ = SetBkMode(mem_dc, TRANSPARENT);
                let _ = SetTextColor(mem_dc, COLORREF(data.colors.text));

                let font = CreateFontW(
                    -14,
                    0,
                    0,
                    0,
                    400,
                    0,
                    0,
                    0,
                    FONT_CHARSET(1),
                    FONT_OUTPUT_PRECISION(0),
                    FONT_CLIP_PRECISION(0),
                    FONT_QUALITY(0),
                    0,
                    windows::core::w!("Consolas"),
                );
                let old_font = SelectObject(mem_dc, font.into());

                // Leave space for scrollbar on the right
                let text_right = rect.right - PREVIEW_PADDING - SCROLLBAR_WIDTH - 4;

                let mut text_rect = RECT {
                    left: PREVIEW_PADDING,
                    top: PREVIEW_PADDING - data.scroll_y,
                    right: text_right,
                    bottom: rect.bottom * 10, // Large enough for calculation
                };

                let mut text_wide: Vec<u16> =
                    text.encode_utf16().chain(std::iter::once(0)).collect();

                // Calculate text height
                let height = DrawTextW(
                    mem_dc,
                    &mut text_wide,
                    &mut text_rect,
                    DT_LEFT | DT_TOP | DT_WORDBREAK | DT_CALCRECT,
                );

                content_height = height;

                // Store content height for scrolling
                let preview_data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PreviewData;
                if !preview_data_ptr.is_null() {
                    (*preview_data_ptr).content_height = height;
                }

                // Check if scrollbar is needed
                let view_height = rect.bottom - PREVIEW_PADDING * 2;
                needs_scrollbar = height > view_height;

                // Now actually draw
                text_rect.top = PREVIEW_PADDING - data.scroll_y;
                text_rect.bottom = rect.bottom * 10;
                let _ = DrawTextW(
                    mem_dc,
                    &mut text_wide,
                    &mut text_rect,
                    DT_LEFT | DT_TOP | DT_WORDBREAK,
                );

                let _ = SelectObject(mem_dc, old_font);
                let _ = DeleteObject(font.into());
            }
            PreviewContent::Image(img_data) => {
                draw_preview_image(mem_dc, img_data, &rect, PREVIEW_PADDING);
            }
        }
    }

    // Draw scrollbar if needed
    if needs_scrollbar {
        let view_height = rect.bottom - PREVIEW_PADDING * 2;
        let scrollbar_x = rect.right - SCROLLBAR_WIDTH - 4;
        let scrollbar_top = PREVIEW_PADDING;
        let scrollbar_height = view_height;

        // Draw track
        let track_rect = RECT {
            left: scrollbar_x,
            top: scrollbar_top,
            right: scrollbar_x + SCROLLBAR_WIDTH,
            bottom: scrollbar_top + scrollbar_height,
        };
        let track_brush = CreateSolidBrush(COLORREF(data.colors.scrollbar_track));
        let track_rgn = CreateRoundRectRgn(
            track_rect.left,
            track_rect.top,
            track_rect.right,
            track_rect.bottom,
            SCROLLBAR_WIDTH,
            SCROLLBAR_WIDTH,
        );
        let _ = FillRgn(mem_dc, track_rgn, track_brush);
        let _ = DeleteObject(track_rgn.into());
        let _ = DeleteObject(track_brush.into());

        // Calculate thumb size and position
        let max_scroll = (content_height - view_height).max(0);
        let thumb_height = ((view_height as f32 / content_height as f32) * scrollbar_height as f32)
            .max(SCROLLBAR_MIN_HEIGHT as f32) as i32;

        let scroll_ratio = if max_scroll > 0 {
            data.scroll_y as f32 / max_scroll as f32
        } else {
            0.0
        };
        let thumb_travel = scrollbar_height - thumb_height;
        let thumb_top = scrollbar_top + (scroll_ratio * thumb_travel as f32) as i32;

        // Draw thumb
        let thumb_rect = RECT {
            left: scrollbar_x,
            top: thumb_top,
            right: scrollbar_x + SCROLLBAR_WIDTH,
            bottom: thumb_top + thumb_height,
        };
        let thumb_brush = CreateSolidBrush(COLORREF(data.colors.scrollbar_thumb));
        let thumb_rgn = CreateRoundRectRgn(
            thumb_rect.left,
            thumb_rect.top,
            thumb_rect.right,
            thumb_rect.bottom,
            SCROLLBAR_WIDTH,
            SCROLLBAR_WIDTH,
        );
        let _ = FillRgn(mem_dc, thumb_rgn, thumb_brush);
        let _ = DeleteObject(thumb_rgn.into());
        let _ = DeleteObject(thumb_brush.into());
    }

    let _ = BitBlt(
        hdc,
        0,
        0,
        rect.right,
        rect.bottom,
        Some(mem_dc),
        0,
        0,
        SRCCOPY,
    );

    let _ = SelectObject(mem_dc, old_bitmap);
    let _ = DeleteObject(mem_bitmap.into());
    let _ = DeleteDC(mem_dc);
}

/// Draw thumbnail scaled to fit within slot while maintaining aspect ratio
unsafe fn draw_thumbnail_scaled(hdc: HDC, png_data: &[u8], x: i32, y: i32, max_size: i32) {
    use image::io::Reader as ImageReader;
    use std::io::Cursor;

    let img = match ImageReader::new(Cursor::new(png_data))
        .with_guessed_format()
        .ok()
        .and_then(|r| r.decode().ok())
    {
        Some(img) => img,
        None => return,
    };

    let rgba = img.to_rgba8();
    let img_width = rgba.width() as i32;
    let img_height = rgba.height() as i32;

    // Calculate scaled dimensions maintaining aspect ratio
    let (draw_width, draw_height) = if img_width > img_height {
        let scale = max_size as f32 / img_width as f32;
        (max_size, (img_height as f32 * scale) as i32)
    } else {
        let scale = max_size as f32 / img_height as f32;
        ((img_width as f32 * scale) as i32, max_size)
    };

    // Center within slot
    let draw_x = x + (max_size - draw_width) / 2;
    let draw_y = y + (max_size - draw_height) / 2;

    let bmi = BITMAPINFO {
        bmiHeader: BITMAPINFOHEADER {
            biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
            biWidth: img_width,
            biHeight: -img_height,
            biPlanes: 1,
            biBitCount: 32,
            biCompression: 0,
            biSizeImage: 0,
            biXPelsPerMeter: 0,
            biYPelsPerMeter: 0,
            biClrUsed: 0,
            biClrImportant: 0,
        },
        bmiColors: [RGBQUAD::default()],
    };

    let mut bits: *mut c_void = std::ptr::null_mut();
    let dib = CreateDIBSection(Some(hdc), &bmi, DIB_RGB_COLORS, &mut bits, None, 0);

    if dib.is_err() || bits.is_null() {
        return;
    }

    let dib = dib.unwrap();

    let pixel_count = (img_width * img_height) as usize;
    let dst = std::slice::from_raw_parts_mut(bits as *mut u8, pixel_count * 4);

    for i in 0..pixel_count {
        let src_idx = i * 4;
        let dst_idx = i * 4;
        dst[dst_idx] = rgba.as_raw()[src_idx + 2];
        dst[dst_idx + 1] = rgba.as_raw()[src_idx + 1];
        dst[dst_idx + 2] = rgba.as_raw()[src_idx];
        dst[dst_idx + 3] = rgba.as_raw()[src_idx + 3];
    }

    let thumb_dc = CreateCompatibleDC(Some(hdc));
    let old_bmp = SelectObject(thumb_dc, dib.into());

    let _ = SetStretchBltMode(hdc, HALFTONE);
    let _ = StretchBlt(
        hdc,
        draw_x,
        draw_y,
        draw_width,
        draw_height,
        Some(thumb_dc),
        0,
        0,
        img_width,
        img_height,
        SRCCOPY,
    );

    let _ = SelectObject(thumb_dc, old_bmp);
    let _ = DeleteDC(thumb_dc);
    let _ = DeleteObject(dib.into());
}

/// Draw image in preview panel scaled to fit
unsafe fn draw_preview_image(hdc: HDC, png_data: &[u8], rect: &RECT, padding: i32) {
    use image::io::Reader as ImageReader;
    use std::io::Cursor;

    let img = match ImageReader::new(Cursor::new(png_data))
        .with_guessed_format()
        .ok()
        .and_then(|r| r.decode().ok())
    {
        Some(img) => img,
        None => return,
    };

    let rgba = img.to_rgba8();
    let img_width = rgba.width() as i32;
    let img_height = rgba.height() as i32;

    let available_width = rect.right - rect.left - padding * 2;
    let available_height = rect.bottom - rect.top - padding * 2;

    // Calculate scaled dimensions
    let scale_x = available_width as f32 / img_width as f32;
    let scale_y = available_height as f32 / img_height as f32;
    let scale = scale_x.min(scale_y).min(1.0); // Don't upscale

    let draw_width = (img_width as f32 * scale) as i32;
    let draw_height = (img_height as f32 * scale) as i32;

    // Center in preview
    let draw_x = padding + (available_width - draw_width) / 2;
    let draw_y = padding + (available_height - draw_height) / 2;

    let bmi = BITMAPINFO {
        bmiHeader: BITMAPINFOHEADER {
            biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
            biWidth: img_width,
            biHeight: -img_height,
            biPlanes: 1,
            biBitCount: 32,
            biCompression: 0,
            biSizeImage: 0,
            biXPelsPerMeter: 0,
            biYPelsPerMeter: 0,
            biClrUsed: 0,
            biClrImportant: 0,
        },
        bmiColors: [RGBQUAD::default()],
    };

    let mut bits: *mut c_void = std::ptr::null_mut();
    let dib = CreateDIBSection(Some(hdc), &bmi, DIB_RGB_COLORS, &mut bits, None, 0);

    if dib.is_err() || bits.is_null() {
        return;
    }

    let dib = dib.unwrap();

    let pixel_count = (img_width * img_height) as usize;
    let dst = std::slice::from_raw_parts_mut(bits as *mut u8, pixel_count * 4);

    for i in 0..pixel_count {
        let src_idx = i * 4;
        let dst_idx = i * 4;
        dst[dst_idx] = rgba.as_raw()[src_idx + 2];
        dst[dst_idx + 1] = rgba.as_raw()[src_idx + 1];
        dst[dst_idx + 2] = rgba.as_raw()[src_idx];
        dst[dst_idx + 3] = rgba.as_raw()[src_idx + 3];
    }

    let img_dc = CreateCompatibleDC(Some(hdc));
    let old_bmp = SelectObject(img_dc, dib.into());

    let _ = SetStretchBltMode(hdc, HALFTONE);
    let _ = StretchBlt(
        hdc,
        draw_x,
        draw_y,
        draw_width,
        draw_height,
        Some(img_dc),
        0,
        0,
        img_width,
        img_height,
        SRCCOPY,
    );

    let _ = SelectObject(img_dc, old_bmp);
    let _ = DeleteDC(img_dc);
    let _ = DeleteObject(dib.into());
}
