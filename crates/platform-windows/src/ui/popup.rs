use std::ffi::c_void;
use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Arc;

use parking_lot::RwLock;
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Gdi::{PS_SOLID, *};
use windows::Win32::UI::Input::KeyboardAndMouse::{TrackMouseEvent, TME_LEAVE, TRACKMOUSEEVENT};
use windows::Win32::UI::WindowsAndMessaging::*;

use clipboard_core::domain::{ClipboardItem, ContentType, SearchType};
use clipboard_core::ports::{UIEvent, UIEventCallback};

const ITEM_HEIGHT: i32 = 36;
const MENU_PADDING: i32 = 4;
const ITEM_PADDING_X: i32 = 12;
const SEPARATOR_HEIGHT: i32 = 9;
const POPUP_WIDTH: i32 = 320;
const POPUP_MAX_ITEMS_HEIGHT: i32 = 400; // Max height for scrollable items section
const FIXED_SECTION_HEIGHT: i32 = SEPARATOR_HEIGHT + ITEM_HEIGHT * 2; // Separator + Settings + Clear History

// Search section constants
const SEARCH_SECTION_HEIGHT: i32 = 32;
const SEARCH_INPUT_HEIGHT: i32 = 24;
const DROPDOWN_WIDTH: i32 = 70;
const SEARCH_GAP: i32 = 6;

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

const WM_MOUSELEAVE: u32 = 0x02A3;

// Window messages for custom controls
const WM_COMMAND: u32 = 0x0111;
const WM_CTLCOLOREDIT: u32 = 0x0133;
const WM_CTLCOLORLISTBOX: u32 = 0x0134;
const WM_TIMER: u32 = 0x0113;
const WM_CHAR: u32 = 0x0102;
const WM_LBUTTONDOWN: u32 = 0x0201;

// Virtual key codes for text editing
const VK_BACK: u32 = 0x08;
const VK_DELETE: u32 = 0x2E;
const VK_LEFT: u32 = 0x25;
const VK_RIGHT: u32 = 0x27;
const VK_HOME: u32 = 0x24;
const VK_END: u32 = 0x23;

// Theme colors - WinUI 3 style
#[derive(Clone)]
struct ThemeColors {
    background: u32,
    text: u32,
    hover: u32,
    separator: u32,
    selected: u32,
    scrollbar_track: u32,
    scrollbar_thumb: u32,
    search_bg: u32,
    search_border: u32,
    search_border_focused: u32,  // WinUI 3 accent color (blue underline)
    placeholder: u32,
    clear_button: u32,           // X button color
    clear_button_hover: u32,     // X button hover color
    dropdown_arrow: u32,         // Dropdown chevron color
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
            search_bg: 0x00383838,
            search_border: 0x00606060,
            search_border_focused: 0x00C67D4F,  // WinUI 3 accent blue (BGR: 0x4F7DC6)
            placeholder: 0x00888888,
            clear_button: 0x00888888,
            clear_button_hover: 0x00FFFFFF,
            dropdown_arrow: 0x00AAAAAA,
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
            search_bg: 0x00F5F5F5,
            search_border: 0x00D0D0D0,
            search_border_focused: 0x00C67D4F,  // WinUI 3 accent blue (BGR: 0x4F7DC6)
            placeholder: 0x00888888,
            clear_button: 0x00666666,
            clear_button_hover: 0x00000000,
            dropdown_arrow: 0x00666666,
        }
    }
}

struct PopupData {
    items: Vec<MenuItem>,
    filtered_indices: Vec<usize>,  // Indices of items matching search
    hovered_index: Option<usize>,  // Index in filtered_indices
    selected_index: Option<usize>, // Index in filtered_indices
    callback: Arc<RwLock<Option<UIEventCallback>>>,
    selected_id: Option<u32>,
    colors: ThemeColors,
    preview_hwnd: HWND,
    work_area: RECT,
    popup_rect: RECT,
    scroll_y: i32,
    items_content_height: i32,   // Total height of scrollable items
    items_visible_height: i32,   // Visible height of scrollable section
    scrollable_item_count: usize, // Number of items in scrollable section (clipboard items only)
    // WinUI 3 custom control rects
    search_input_rect: RECT,     // Search input area (custom drawn)
    dropdown_rect: RECT,         // Dropdown button area (custom drawn)
    clear_button_rect: RECT,     // Clear (X) button rect inside search input
    // WinUI 3 control states
    search_focused: bool,        // Is search input focused
    clear_button_hovered: bool,  // Is clear button being hovered
    dropdown_hovered: bool,      // Is dropdown being hovered
    dropdown_open: bool,         // Is dropdown list open
    dropdown_item_rects: Vec<RECT>, // Rects for dropdown items
    dropdown_hovered_index: Option<usize>, // Hovered item in dropdown
    // Brushes for control colors (owned by PopupData, must be deleted on cleanup)
    edit_bg_brush: HBRUSH,
    combo_bg_brush: HBRUSH,
    // Search fields
    search_query: String,
    search_type: SearchType,
    // Caret/cursor
    caret_pos: usize,            // Caret position in search query
    caret_visible: bool,         // For caret blinking
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
        let mut data_size: u32 = size_of::<u32>() as u32;
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

        // Calculate scrollable items count (all items except separator, Settings, Clear History)
        let scrollable_item_count = menu_items.len().saturating_sub(3);

        // Initialize filtered indices with all clipboard items
        let filtered_indices: Vec<usize> = (0..scrollable_item_count).collect();

        // Calculate heights for scrollable section (clipboard items only)
        let items_content_height: i32 = (scrollable_item_count as i32) * ITEM_HEIGHT;

        let items_visible_height = items_content_height.min(POPUP_MAX_ITEMS_HEIGHT);

        // Total popup height = search section + padding + scrollable section + fixed section + padding
        let total_height = SEARCH_SECTION_HEIGHT + MENU_PADDING + items_visible_height + FIXED_SECTION_HEIGHT + MENU_PADDING;

        let width = POPUP_WIDTH;

        let mut point = POINT::default();
        let _ = GetCursorPos(&mut point);

        let monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        let mut monitor_info = MONITORINFO {
            cbSize: size_of::<MONITORINFO>() as u32,
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
        if point.y + total_height > work_area.bottom {
            point.y = work_area.bottom - total_height;
        }
        if point.y < work_area.top {
            point.y = work_area.top;
        }

        let popup_rect = RECT {
            left: point.x,
            top: point.y,
            right: point.x + width,
            bottom: point.y + total_height,
        };

        // Register popup window class
        let class_name = windows::core::w!("ClipcyModernPopup");
        let wc = WNDCLASSEXW {
            cbSize: size_of::<WNDCLASSEXW>() as u32,
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
            cbSize: size_of::<WNDCLASSEXW>() as u32,
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
            total_height,
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

        // WinUI 3 style: Custom-drawn controls (no native COMBOBOX or EDIT)
        // Calculate rects for custom WinUI 3 controls
        let dropdown_rect = RECT {
            left: MENU_PADDING,
            top: MENU_PADDING,
            right: MENU_PADDING + DROPDOWN_WIDTH,
            bottom: MENU_PADDING + SEARCH_INPUT_HEIGHT,
        };

        let search_input_x = MENU_PADDING + DROPDOWN_WIDTH + SEARCH_GAP;
        let search_input_width = width - MENU_PADDING * 2 - DROPDOWN_WIDTH - SEARCH_GAP;
        let clear_button_size = SEARCH_INPUT_HEIGHT - 8;

        let search_input_rect = RECT {
            left: search_input_x,
            top: MENU_PADDING,
            right: search_input_x + search_input_width,
            bottom: MENU_PADDING + SEARCH_INPUT_HEIGHT,
        };

        let clear_button_rect = RECT {
            left: search_input_rect.right - clear_button_size - 6,
            top: MENU_PADDING + 4,
            right: search_input_rect.right - 6,
            bottom: MENU_PADDING + 4 + clear_button_size,
        };

        // Create brushes for control backgrounds
        let edit_bg_brush = CreateSolidBrush(COLORREF(colors.search_bg));
        let combo_bg_brush = CreateSolidBrush(COLORREF(colors.search_bg));

        let data = Box::new(PopupData {
            items: menu_items,
            filtered_indices,
            hovered_index: None,
            selected_index: None,
            callback,
            selected_id: None,
            colors,
            preview_hwnd,
            work_area,
            popup_rect,
            scroll_y: 0,
            items_content_height,
            items_visible_height,
            scrollable_item_count,
            search_input_rect,
            dropdown_rect,
            clear_button_rect,
            search_focused: true,  // Start focused
            clear_button_hovered: false,
            dropdown_hovered: false,
            dropdown_open: false,
            dropdown_item_rects: Vec::new(),
            dropdown_hovered_index: None,
            edit_bg_brush,
            combo_bg_brush,
            search_query: String::new(),
            search_type: SearchType::Fuzzy,
            caret_pos: 0,
            caret_visible: true,
        });
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, Box::into_raw(data) as isize);

        // Start caret blink timer (ID = 1, 500ms interval)
        let _ = SetTimer(Some(hwnd), 1, 500, None);

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

            // Check if message is for dialog-like behavior (Tab, etc)
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
            size_of::<DWM_WINDOW_CORNER_PREFERENCE>() as u32,
        );
    }
}

/// Check if a point is within a rect
fn point_in_rect(x: i32, y: i32, rect: &RECT) -> bool {
    x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom
}

/// Perform fuzzy search - case insensitive substring matching
fn fuzzy_match(text: &str, query: &str) -> bool {
    if query.is_empty() {
        return true;
    }
    let text_lower = text.to_lowercase();
    let query_lower = query.to_lowercase();

    // Simple fuzzy: check if all query chars appear in order
    let mut text_chars = text_lower.chars().peekable();
    for qc in query_lower.chars() {
        loop {
            match text_chars.next() {
                Some(tc) if tc == qc => break,
                Some(_) => continue,
                None => return false,
            }
        }
    }
    true
}

/// Perform regex search
fn regex_match(text: &str, pattern: &str) -> bool {
    if pattern.is_empty() {
        return true;
    }
    // Simple regex implementation without external crate
    // For now, treat it as case-insensitive contains
    // A full regex would require the `regex` crate
    match regex_simple_match(text, pattern) {
        Ok(matched) => matched,
        Err(_) => false, // Invalid pattern
    }
}

/// Simple regex-like matching (supports basic patterns)
fn regex_simple_match(text: &str, pattern: &str) -> Result<bool, ()> {
    // Basic patterns: ^ (start), $ (end), . (any char), * (zero or more)
    let text_lower = text.to_lowercase();
    let pattern_lower = pattern.to_lowercase();

    let must_start = pattern_lower.starts_with('^');
    let must_end = pattern_lower.ends_with('$') && !pattern_lower.ends_with("\\$");

    let pattern_clean: String = pattern_lower
        .trim_start_matches('^')
        .trim_end_matches('$')
        .to_string();

    if pattern_clean.is_empty() {
        return Ok(true);
    }

    // Convert simple pattern to check
    // For simplicity, just do contains check with . as any char
    let pattern_escaped = pattern_clean.replace(".*", "\x00");
    let parts: Vec<&str> = pattern_escaped.split('\x00').collect();

    if parts.len() == 1 {
        // No wildcards, simple contains
        let _search = pattern_clean.replace(".", "?"); // . matches any single char
        if must_start && must_end {
            Ok(text_lower == pattern_clean || pattern_clean.contains('.') && text_lower.len() == pattern_clean.len())
        } else if must_start {
            Ok(text_lower.starts_with(&pattern_clean.replace(".", "")))
        } else if must_end {
            Ok(text_lower.ends_with(&pattern_clean.replace(".", "")))
        } else {
            Ok(text_lower.contains(&pattern_clean.replace(".", "")))
        }
    } else {
        // Has .* wildcards - check if all parts appear in order
        let mut pos = 0;
        for (i, part) in parts.iter().enumerate() {
            if part.is_empty() {
                continue;
            }
            if let Some(found_pos) = text_lower[pos..].find(part) {
                if i == 0 && must_start && found_pos != 0 {
                    return Ok(false);
                }
                pos += found_pos + part.len();
            } else {
                return Ok(false);
            }
        }
        if must_end && pos != text_lower.len() {
            return Ok(false);
        }
        Ok(true)
    }
}

/// Filter items based on search query and type
fn filter_items(data: &mut PopupData) {
    data.filtered_indices.clear();

    let query = data.search_query.trim();

    // Always include all clipboard items if query is empty
    if query.is_empty() {
        for i in 0..data.scrollable_item_count {
            data.filtered_indices.push(i);
        }
    } else {
        for i in 0..data.scrollable_item_count {
            let item = &data.items[i];
            let matches = match data.search_type {
                SearchType::Fuzzy => fuzzy_match(&item.text, query),
                SearchType::Regex => regex_match(&item.text, query),
            };
            if matches {
                data.filtered_indices.push(i);
            }
        }
    }

    // Reset scroll and selection
    data.scroll_y = 0;
    data.selected_index = if data.filtered_indices.is_empty() { None } else { Some(0) };
    data.hovered_index = None;

    // Recalculate content height based on filtered items
    data.items_content_height = (data.filtered_indices.len() as i32) * ITEM_HEIGHT;
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
                let data = Box::from_raw(data_ptr);
                // Clean up brushes
                let _ = DeleteObject(data.edit_bg_brush.into());
                let _ = DeleteObject(data.combo_bg_brush.into());
                // Native controls are children and destroyed with parent
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
        let popup_bottom = data.popup_rect.bottom;

        let preview_x = if popup_right + PREVIEW_GAP + PREVIEW_WIDTH <= data.work_area.right {
            popup_right + PREVIEW_GAP
        } else {
            popup_left - PREVIEW_GAP - PREVIEW_WIDTH
        };

        // Calculate Y position aligned with the selected item's visible position
        let item_y_in_content = get_item_y_position(&data.items, index);

        // For scrollable items, adjust for scroll offset to get visible position
        let visible_item_y = if index < data.scrollable_item_count {
            // Item is in scrollable section - adjust for scroll
            (item_y_in_content - data.scroll_y).max(MENU_PADDING)
        } else {
            // Item is in fixed section - calculate position relative to fixed section start
            let fixed_section_start = MENU_PADDING + data.items_visible_height;
            let mut y = fixed_section_start;
            for (i, item) in data.items.iter().enumerate().skip(data.scrollable_item_count) {
                if i == index {
                    break;
                }
                y += if item.is_separator { SEPARATOR_HEIGHT } else { ITEM_HEIGHT };
            }
            y
        };

        // Apply max Y threshold: preview should not start below the popup's bottom
        let max_preview_y = popup_bottom - panel_height;
        let preview_y = (popup_top + visible_item_y)
            .max(data.work_area.top)
            .min(max_preview_y)
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
        WM_TIMER => {
            // Caret blink timer (ID = 1)
            if wparam.0 == 1 {
                let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
                if !data_ptr.is_null() {
                    let data = &mut *data_ptr;
                    if data.search_focused {
                        data.caret_visible = !data.caret_visible;
                        let _ = InvalidateRect(Some(hwnd), Some(&data.search_input_rect), false);
                    }
                }
            }
            LRESULT(0)
        }
        WM_CHAR => {
            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;

                if data.search_focused && !data.dropdown_open {
                    let ch = wparam.0 as u32;
                    // Printable characters (space and above, excluding control chars)
                    if ch >= 32 && ch != 127 {
                        if let Some(c) = char::from_u32(ch) {
                            // Insert character at caret position
                            let byte_pos = data.search_query.char_indices()
                                .nth(data.caret_pos)
                                .map(|(i, _)| i)
                                .unwrap_or(data.search_query.len());
                            data.search_query.insert(byte_pos, c);
                            data.caret_pos += 1;
                            data.caret_visible = true;
                            filter_items(data);
                            let _ = InvalidateRect(Some(hwnd), None, true);
                        }
                    }
                }
            }
            LRESULT(0)
        }
        WM_LBUTTONDOWN => {
            let x = (lparam.0 & 0xFFFF) as i16 as i32;
            let y = ((lparam.0 >> 16) & 0xFFFF) as i16 as i32;

            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;

                // Check if clicking on dropdown
                if point_in_rect(x, y, &data.dropdown_rect) {
                    data.dropdown_open = !data.dropdown_open;
                    data.search_focused = false;

                    // Update dropdown item rects when opening
                    if data.dropdown_open {
                        let dropdown_list_item_height = 28;
                        let dropdown_list_top = data.dropdown_rect.bottom + 2;
                        let dropdown_list_left = data.dropdown_rect.left;
                        let dropdown_list_right = data.dropdown_rect.right;

                        data.dropdown_item_rects.clear();
                        data.dropdown_item_rects.push(RECT {
                            left: dropdown_list_left + 2,
                            top: dropdown_list_top + 2,
                            right: dropdown_list_right - 2,
                            bottom: dropdown_list_top + 2 + dropdown_list_item_height,
                        });
                        data.dropdown_item_rects.push(RECT {
                            left: dropdown_list_left + 2,
                            top: dropdown_list_top + 2 + dropdown_list_item_height,
                            right: dropdown_list_right - 2,
                            bottom: dropdown_list_top + 2 + dropdown_list_item_height * 2,
                        });
                        data.dropdown_hovered_index = None;
                    }

                    let _ = InvalidateRect(Some(hwnd), None, true);
                    return LRESULT(0);
                }

                // Check if clicking on clear button
                if point_in_rect(x, y, &data.clear_button_rect) && !data.search_query.is_empty() {
                    data.search_query.clear();
                    data.caret_pos = 0;
                    filter_items(data);
                    let _ = InvalidateRect(Some(hwnd), None, true);
                    return LRESULT(0);
                }

                // Check if clicking on search input
                if point_in_rect(x, y, &data.search_input_rect) {
                    data.search_focused = true;
                    data.dropdown_open = false;
                    data.caret_visible = true;
                    // Set caret position based on click location (simplified: end of text)
                    data.caret_pos = data.search_query.chars().count();
                    let _ = InvalidateRect(Some(hwnd), None, true);
                    return LRESULT(0);
                }

                // Check if clicking on dropdown item
                if data.dropdown_open {
                    for (i, rect) in data.dropdown_item_rects.iter().enumerate() {
                        if point_in_rect(x, y, rect) {
                            data.search_type = if i == 0 { SearchType::Fuzzy } else { SearchType::Regex };
                            data.dropdown_open = false;
                            filter_items(data);
                            let _ = InvalidateRect(Some(hwnd), None, true);
                            return LRESULT(0);
                        }
                    }
                    // Clicked outside dropdown items, close it
                    data.dropdown_open = false;
                    let _ = InvalidateRect(Some(hwnd), None, true);
                }
            }
            // Fall through to handle item clicks
            DefWindowProcW(hwnd, msg, wparam, lparam)
        }
        WM_CTLCOLOREDIT | WM_CTLCOLORLISTBOX => {
            // Not used anymore with custom controls, but keep for safety
            DefWindowProcW(hwnd, msg, wparam, lparam)
        }
        WM_COMMAND => {
            // Not used anymore with custom controls
            LRESULT(0)
        }
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

            let x = (lparam.0 & 0xFFFF) as i16 as i32;
            let y = ((lparam.0 >> 16) & 0xFFFF) as i16 as i32;

            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;

                // Check hover states for custom WinUI 3 controls
                let new_clear_hover = point_in_rect(x, y, &data.clear_button_rect) && !data.search_query.is_empty();
                let new_dropdown_hover = point_in_rect(x, y, &data.dropdown_rect);

                // Check dropdown item hover
                let mut new_dropdown_item_hover = None;
                if data.dropdown_open {
                    for (i, rect) in data.dropdown_item_rects.iter().enumerate() {
                        if point_in_rect(x, y, rect) {
                            new_dropdown_item_hover = Some(i);
                            break;
                        }
                    }
                }

                // Update hover states if changed
                let needs_redraw = new_clear_hover != data.clear_button_hovered
                    || new_dropdown_hover != data.dropdown_hovered
                    || new_dropdown_item_hover != data.dropdown_hovered_index;

                data.clear_button_hovered = new_clear_hover;
                data.dropdown_hovered = new_dropdown_hover;
                if data.dropdown_open {
                    data.dropdown_hovered_index = new_dropdown_item_hover;
                }

                if needs_redraw {
                    let _ = InvalidateRect(Some(hwnd), None, false);
                }

                // Only update selection if keyboard nav is not active and not in control area
                if !KEYBOARD_NAV_ACTIVE.load(Ordering::SeqCst) && y >= SEARCH_SECTION_HEIGHT {
                    // Use the new function that handles both sections correctly
                    let new_index = get_item_at_y_with_scroll(data, y);

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
                    cbSize: size_of::<TRACKMOUSEEVENT>() as u32,
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

                // Check if click is on a menu item (native controls handle their own clicks)
                if let Some(index) = get_item_at_y_with_scroll(data, y) {
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
                        if data.dropdown_open {
                            data.dropdown_open = false;
                            let _ = InvalidateRect(Some(hwnd), None, true);
                        } else {
                            POPUP_VISIBLE.store(false, Ordering::SeqCst);
                            let _ = PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0));
                        }
                    }
                    VK_UP => {
                        if data.dropdown_open {
                            // Navigate dropdown
                            let current = data.dropdown_hovered_index.unwrap_or(1);
                            data.dropdown_hovered_index = Some(if current == 0 { 1 } else { 0 });
                            let _ = InvalidateRect(Some(hwnd), None, true);
                        } else {
                            navigate_selection(data, hwnd, -1);
                        }
                    }
                    VK_DOWN => {
                        if data.dropdown_open {
                            // Navigate dropdown
                            let current = data.dropdown_hovered_index.unwrap_or(0);
                            data.dropdown_hovered_index = Some(if current == 1 { 0 } else { 1 });
                            let _ = InvalidateRect(Some(hwnd), None, true);
                        } else {
                            navigate_selection(data, hwnd, 1);
                        }
                    }
                    VK_RETURN => {
                        if data.dropdown_open {
                            // Select dropdown item
                            if let Some(idx) = data.dropdown_hovered_index {
                                data.search_type = if idx == 0 { SearchType::Fuzzy } else { SearchType::Regex };
                                data.dropdown_open = false;
                                filter_items(data);
                                let _ = InvalidateRect(Some(hwnd), None, true);
                            }
                        } else if let Some(index) = data.selected_index {
                            select_item(data, index, hwnd);
                        }
                    }
                    VK_BACK => {
                        // Backspace - delete character before caret
                        if data.search_focused && data.caret_pos > 0 {
                            let char_count = data.search_query.chars().count();
                            if data.caret_pos <= char_count {
                                // Find byte position of character to remove
                                let remove_pos = data.caret_pos - 1;
                                let byte_start = data.search_query.char_indices()
                                    .nth(remove_pos)
                                    .map(|(i, _)| i)
                                    .unwrap_or(0);
                                let byte_end = data.search_query.char_indices()
                                    .nth(remove_pos + 1)
                                    .map(|(i, _)| i)
                                    .unwrap_or(data.search_query.len());
                                data.search_query.replace_range(byte_start..byte_end, "");
                                data.caret_pos -= 1;
                                data.caret_visible = true;
                                filter_items(data);
                                let _ = InvalidateRect(Some(hwnd), None, true);
                            }
                        }
                    }
                    VK_DELETE => {
                        // Delete - delete character after caret
                        if data.search_focused {
                            let char_count = data.search_query.chars().count();
                            if data.caret_pos < char_count {
                                let byte_start = data.search_query.char_indices()
                                    .nth(data.caret_pos)
                                    .map(|(i, _)| i)
                                    .unwrap_or(0);
                                let byte_end = data.search_query.char_indices()
                                    .nth(data.caret_pos + 1)
                                    .map(|(i, _)| i)
                                    .unwrap_or(data.search_query.len());
                                data.search_query.replace_range(byte_start..byte_end, "");
                                data.caret_visible = true;
                                filter_items(data);
                                let _ = InvalidateRect(Some(hwnd), None, true);
                            }
                        }
                    }
                    VK_LEFT => {
                        // Move caret left
                        if data.search_focused && data.caret_pos > 0 {
                            data.caret_pos -= 1;
                            data.caret_visible = true;
                            let _ = InvalidateRect(Some(hwnd), Some(&data.search_input_rect), false);
                        }
                    }
                    VK_RIGHT => {
                        // Move caret right
                        if data.search_focused {
                            let char_count = data.search_query.chars().count();
                            if data.caret_pos < char_count {
                                data.caret_pos += 1;
                                data.caret_visible = true;
                                let _ = InvalidateRect(Some(hwnd), Some(&data.search_input_rect), false);
                            }
                        }
                    }
                    VK_HOME => {
                        // Move caret to start
                        if data.search_focused {
                            data.caret_pos = 0;
                            data.caret_visible = true;
                            let _ = InvalidateRect(Some(hwnd), Some(&data.search_input_rect), false);
                        }
                    }
                    VK_END => {
                        // Move caret to end
                        if data.search_focused {
                            data.caret_pos = data.search_query.chars().count();
                            data.caret_visible = true;
                            let _ = InvalidateRect(Some(hwnd), Some(&data.search_input_rect), false);
                        }
                    }
                    _ => {}
                }
            }
            LRESULT(0)
        }
        WM_MOUSEWHEEL => {
            let data_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut PopupData;
            if !data_ptr.is_null() {
                let data = &mut *data_ptr;
                let delta = ((wparam.0 >> 16) as i16) as i32;
                let scroll_amount = delta / 3;
                let max_scroll = (data.items_content_height - data.items_visible_height).max(0);

                data.scroll_y = (data.scroll_y - scroll_amount).max(0).min(max_scroll);
                let _ = InvalidateRect(Some(hwnd), None, true);
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
                cbSize: size_of::<TRACKMOUSEEVENT>() as u32,
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

    // Build list of selectable items: filtered clipboard items + fixed items (Settings, Clear History)
    let mut selectable_items: Vec<usize> = data.filtered_indices.clone();

    // Add fixed menu items (skip separator)
    for (i, item) in data.items.iter().enumerate().skip(data.scrollable_item_count) {
        if !item.is_separator {
            selectable_items.push(i);
        }
    }

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

    // Auto-scroll to keep selected item visible (only for scrollable items)
    if new_index < data.scrollable_item_count {
        // Find position in filtered_indices
        if let Some(filter_pos) = data.filtered_indices.iter().position(|&i| i == new_index) {
            let item_y = (filter_pos as i32) * ITEM_HEIGHT;
            let item_bottom = item_y + ITEM_HEIGHT;
            let max_scroll = (data.items_content_height - data.items_visible_height).max(0);

            if item_y < data.scroll_y {
                // Item is above visible area, scroll up
                data.scroll_y = item_y.max(0);
            } else if item_bottom > data.scroll_y + data.items_visible_height {
                // Item is below visible area, scroll down
                data.scroll_y = (item_bottom - data.items_visible_height).min(max_scroll);
            }
        }
    }

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

/// Get item at screen y coordinate, handling scrollable and fixed sections
fn get_item_at_y_with_scroll(data: &PopupData, screen_y: i32) -> Option<usize> {
    // Account for search section at the top
    let scrollable_section_top = SEARCH_SECTION_HEIGHT + MENU_PADDING;
    let scrollable_section_bottom = scrollable_section_top + data.items_visible_height;
    let fixed_section_top = scrollable_section_bottom;

    if screen_y < scrollable_section_top {
        return None;
    }

    // Check if in scrollable section (filtered items)
    if screen_y < scrollable_section_bottom {
        // Calculate which filtered item is at this position
        let relative_y = screen_y - scrollable_section_top + data.scroll_y;
        let filter_idx = relative_y / ITEM_HEIGHT;

        if filter_idx >= 0 && (filter_idx as usize) < data.filtered_indices.len() {
            let item_idx = data.filtered_indices[filter_idx as usize];
            if !data.items[item_idx].is_separator {
                return Some(item_idx);
            }
        }
        return None;
    }

    // Check if in fixed section (no scroll offset)
    let mut current_y = fixed_section_top;
    for (i, item) in data.items.iter().enumerate().skip(data.scrollable_item_count) {
        let item_height = if item.is_separator { SEPARATOR_HEIGHT } else { ITEM_HEIGHT };

        if screen_y >= current_y && screen_y < current_y + item_height {
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

    // Create font
    let font = CreateFontW(
        -14, 0, 0, 0, 400, 0, 0, 0,
        FONT_CHARSET(1), FONT_OUTPUT_PRECISION(0), FONT_CLIP_PRECISION(0), FONT_QUALITY(0),
        0, windows::core::w!("Segoe UI"),
    );
    let old_font = SelectObject(mem_dc, font.into());
    let _ = SetBkMode(mem_dc, TRANSPARENT);

    // === Draw WinUI 3 Style Search Controls ===

    // Draw Dropdown (WinUI 3 ComboBox style)
    let dropdown = &data.dropdown_rect;
    let dropdown_bg_color = if data.dropdown_hovered || data.dropdown_open {
        data.colors.hover
    } else {
        data.colors.search_bg
    };
    let dropdown_brush = CreateSolidBrush(COLORREF(dropdown_bg_color));
    let dropdown_rgn = CreateRoundRectRgn(
        dropdown.left, dropdown.top,
        dropdown.right + 1, dropdown.bottom + 1,
        6, 6,
    );
    let _ = FillRgn(mem_dc, dropdown_rgn, dropdown_brush);
    let _ = DeleteObject(dropdown_rgn.into());
    let _ = DeleteObject(dropdown_brush.into());

    // Draw dropdown text
    let _ = SetTextColor(mem_dc, COLORREF(data.colors.text));
    let dropdown_text = match data.search_type {
        SearchType::Fuzzy => "Fuzzy",
        SearchType::Regex => "Regex",
    };
    let mut dropdown_text_rect = RECT {
        left: dropdown.left + 8,
        top: dropdown.top,
        right: dropdown.right - 20,
        bottom: dropdown.bottom,
    };
    let mut text_vec: Vec<u16> = dropdown_text.encode_utf16().chain(std::iter::once(0)).collect();
    let _ = DrawTextW(mem_dc, &mut text_vec, &mut dropdown_text_rect,
                      DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    // Draw dropdown chevron (▼)
    let chevron_x = dropdown.right - 16;
    let chevron_y = dropdown.top + (SEARCH_INPUT_HEIGHT - 8) / 2;
    let chevron_pen = CreatePen(PS_SOLID, 1, COLORREF(data.colors.dropdown_arrow));
    let old_pen = SelectObject(mem_dc, chevron_pen.into());
    // Draw chevron as two lines forming a V
    let _ = MoveToEx(mem_dc, chevron_x, chevron_y, None);
    let _ = LineTo(mem_dc, chevron_x + 4, chevron_y + 4);
    let _ = MoveToEx(mem_dc, chevron_x + 4, chevron_y + 4, None);
    let _ = LineTo(mem_dc, chevron_x + 8, chevron_y);
    let _ = SelectObject(mem_dc, old_pen);
    let _ = DeleteObject(chevron_pen.into());

    // Draw Search Input (WinUI 3 TextBox style with underline)
    let search_rect = &data.search_input_rect;
    let search_brush = CreateSolidBrush(COLORREF(data.colors.search_bg));
    let search_rgn = CreateRoundRectRgn(
        search_rect.left, search_rect.top,
        search_rect.right + 1, search_rect.bottom + 1,
        6, 6,
    );
    let _ = FillRgn(mem_dc, search_rgn, search_brush);
    let _ = DeleteObject(search_rgn.into());
    let _ = DeleteObject(search_brush.into());

    // Draw underline (WinUI 3 accent color when focused)
    let underline_color = if data.search_focused {
        data.colors.search_border_focused
    } else {
        data.colors.search_border
    };
    let underline_height = if data.search_focused { 2 } else { 1 };
    let underline_rect = RECT {
        left: search_rect.left + 2,
        top: search_rect.bottom - underline_height,
        right: search_rect.right - 2,
        bottom: search_rect.bottom,
    };
    let underline_brush = CreateSolidBrush(COLORREF(underline_color));
    let _ = FillRect(mem_dc, &underline_rect, underline_brush);
    let _ = DeleteObject(underline_brush.into());

    // Draw search text or placeholder
    let text_padding = 10;
    let clear_btn_width = if data.search_query.is_empty() { 0 } else { 24 };
    let mut text_rect = RECT {
        left: search_rect.left + text_padding,
        top: search_rect.top,
        right: search_rect.right - text_padding - clear_btn_width,
        bottom: search_rect.bottom - underline_height,
    };

    if data.search_query.is_empty() {
        // Draw placeholder
        let _ = SetTextColor(mem_dc, COLORREF(data.colors.placeholder));
        let placeholder = "Search...";
        let mut placeholder_vec: Vec<u16> = placeholder.encode_utf16().chain(std::iter::once(0)).collect();
        let _ = DrawTextW(mem_dc, &mut placeholder_vec, &mut text_rect,
                          DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    } else {
        // Draw search text
        let _ = SetTextColor(mem_dc, COLORREF(data.colors.text));
        let mut search_text_vec: Vec<u16> = data.search_query.encode_utf16().chain(std::iter::once(0)).collect();
        let _ = DrawTextW(mem_dc, &mut search_text_vec, &mut text_rect,
                          DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    }

    // Draw caret if focused and visible
    if data.search_focused && data.caret_visible {
        // Calculate caret X position using GetTextExtentPoint32W for accurate measurement
        let caret_text: String = data.search_query.chars().take(data.caret_pos).collect();
        let caret_text_wide: Vec<u16> = caret_text.encode_utf16().collect();

        let mut text_size = SIZE::default();
        if caret_text_wide.is_empty() {
            text_size.cx = 0;
        } else {
            let _ = GetTextExtentPoint32W(mem_dc, &caret_text_wide, &mut text_size);
        }

        let caret_x = text_rect.left + text_size.cx;
        let caret_y1 = search_rect.top + 5;
        let caret_y2 = search_rect.bottom - 5 - underline_height;

        let caret_pen = CreatePen(PS_SOLID, 1, COLORREF(data.colors.text));
        let old_pen = SelectObject(mem_dc, caret_pen.into());
        let _ = MoveToEx(mem_dc, caret_x, caret_y1, None);
        let _ = LineTo(mem_dc, caret_x, caret_y2);
        let _ = SelectObject(mem_dc, old_pen);
        let _ = DeleteObject(caret_pen.into());
    }

    // Draw clear button (X) if there's text
    if !data.search_query.is_empty() {
        let clear_rect = &data.clear_button_rect;
        let clear_color = if data.clear_button_hovered {
            data.colors.clear_button_hover
        } else {
            data.colors.clear_button
        };

        // Draw X
        let x_pen = CreatePen(PS_SOLID, 1, COLORREF(clear_color));
        let old_pen = SelectObject(mem_dc, x_pen.into());
        let x_margin = 4;
        // First line of X
        let _ = MoveToEx(mem_dc, clear_rect.left + x_margin, clear_rect.top + x_margin, None);
        let _ = LineTo(mem_dc, clear_rect.right - x_margin, clear_rect.bottom - x_margin);
        // Second line of X
        let _ = MoveToEx(mem_dc, clear_rect.right - x_margin, clear_rect.top + x_margin, None);
        let _ = LineTo(mem_dc, clear_rect.left + x_margin, clear_rect.bottom - x_margin);
        let _ = SelectObject(mem_dc, old_pen);
        let _ = DeleteObject(x_pen.into());
    }

    // === Draw Items Section ===

    let needs_scrollbar = data.items_content_height > data.items_visible_height;
    let content_right = if needs_scrollbar {
        rect.right - SCROLLBAR_WIDTH - 4
    } else {
        rect.right
    };

    // Calculate section boundaries (accounting for search section)
    let scrollable_section_top = SEARCH_SECTION_HEIGHT + MENU_PADDING;
    let scrollable_section_bottom = scrollable_section_top + data.items_visible_height;
    let fixed_section_top = scrollable_section_bottom;

    // Set clipping region for scrollable section
    let clip_rgn = CreateRectRgn(0, scrollable_section_top, rect.right, scrollable_section_bottom);
    let _ = SelectClipRgn(mem_dc, Some(clip_rgn));

    // Draw filtered scrollable items with scroll offset
    let _ = SetTextColor(mem_dc, COLORREF(data.colors.text));
    let mut y = scrollable_section_top - data.scroll_y;

    for (_filter_idx, &item_idx) in data.filtered_indices.iter().enumerate() {
        let item = &data.items[item_idx];

        // Skip items completely outside visible area
        if y + ITEM_HEIGHT < scrollable_section_top {
            y += ITEM_HEIGHT;
            continue;
        }
        if y >= scrollable_section_bottom {
            break;
        }

        // Draw selection/hover background - compare with actual item index
        let is_selected = data.selected_index == Some(item_idx);
        let is_hovered = data.hovered_index == Some(item_idx);

        if is_selected || is_hovered {
            let bg_color = if is_selected {
                data.colors.selected
            } else {
                data.colors.hover
            };

            let hover_rect = RECT {
                left: MENU_PADDING,
                top: y,
                right: content_right - MENU_PADDING,
                bottom: y + ITEM_HEIGHT,
            };

            let hover_brush = CreateSolidBrush(COLORREF(bg_color));
            let rgn = CreateRoundRectRgn(
                hover_rect.left, hover_rect.top,
                hover_rect.right, hover_rect.bottom, 4, 4,
            );
            let _ = FillRgn(mem_dc, rgn, hover_brush);
            let _ = DeleteObject(rgn.into());
            let _ = DeleteObject(hover_brush.into());
        }

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
            right: content_right - ITEM_PADDING_X,
            bottom: y + ITEM_HEIGHT,
        };

        // Trim whitespace for display only
        let display_text = item.text.trim();
        let mut text: Vec<u16> = display_text.encode_utf16().chain(std::iter::once(0)).collect();
        let _ = DrawTextW(mem_dc, &mut text, &mut text_rect,
                          DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);

        y += ITEM_HEIGHT;
    }

    // Remove clipping for fixed section
    let _ = SelectClipRgn(mem_dc, None);
    let _ = DeleteObject(clip_rgn.into());

    // Draw fixed section (separator + Settings + Clear History)
    let mut fixed_y = fixed_section_top;
    for (i, item) in data.items.iter().enumerate().skip(data.scrollable_item_count) {
        if item.is_separator {
            let sep_rect = RECT {
                left: ITEM_PADDING_X,
                top: fixed_y + SEPARATOR_HEIGHT / 2,
                right: content_right - ITEM_PADDING_X,
                bottom: fixed_y + SEPARATOR_HEIGHT / 2 + 1,
            };
            let sep_brush = CreateSolidBrush(COLORREF(data.colors.separator));
            let _ = FillRect(mem_dc, &sep_rect, sep_brush);
            let _ = DeleteObject(sep_brush.into());
            fixed_y += SEPARATOR_HEIGHT;
        } else {
            // Use actual item index for comparison
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
                    top: fixed_y,
                    right: content_right - MENU_PADDING,
                    bottom: fixed_y + ITEM_HEIGHT,
                };

                let hover_brush = CreateSolidBrush(COLORREF(bg_color));
                let rgn = CreateRoundRectRgn(
                    hover_rect.left, hover_rect.top,
                    hover_rect.right, hover_rect.bottom, 4, 4,
                );
                let _ = FillRgn(mem_dc, rgn, hover_brush);
                let _ = DeleteObject(rgn.into());
                let _ = DeleteObject(hover_brush.into());
            }

            let mut text_rect = RECT {
                left: ITEM_PADDING_X,
                top: fixed_y,
                right: content_right - ITEM_PADDING_X,
                bottom: fixed_y + ITEM_HEIGHT,
            };

            let mut text: Vec<u16> = item.text.encode_utf16().chain(std::iter::once(0)).collect();
            let _ = DrawTextW(mem_dc, &mut text, &mut text_rect,
                              DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);

            fixed_y += ITEM_HEIGHT;
        }
    }

    // Cleanup font
    let _ = SelectObject(mem_dc, old_font);
    let _ = DeleteObject(font.into());

    // Draw scrollbar in scrollable section only
    if needs_scrollbar {
        let scrollbar_x = rect.right - SCROLLBAR_WIDTH - 2;
        let scrollbar_top = scrollable_section_top;
        let scrollbar_height = data.items_visible_height;

        // Draw track
        let track_rect = RECT {
            left: scrollbar_x,
            top: scrollbar_top,
            right: scrollbar_x + SCROLLBAR_WIDTH,
            bottom: scrollbar_top + scrollbar_height,
        };
        let track_brush = CreateSolidBrush(COLORREF(data.colors.scrollbar_track));
        let track_rgn = CreateRoundRectRgn(
            track_rect.left, track_rect.top,
            track_rect.right, track_rect.bottom,
            SCROLLBAR_WIDTH, SCROLLBAR_WIDTH,
        );
        let _ = FillRgn(mem_dc, track_rgn, track_brush);
        let _ = DeleteObject(track_rgn.into());
        let _ = DeleteObject(track_brush.into());

        // Calculate thumb
        let max_scroll = (data.items_content_height - data.items_visible_height).max(0);
        let thumb_height = ((data.items_visible_height as f32 / data.items_content_height as f32)
            * scrollbar_height as f32).max(SCROLLBAR_MIN_HEIGHT as f32) as i32;

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
            thumb_rect.left, thumb_rect.top,
            thumb_rect.right, thumb_rect.bottom,
            SCROLLBAR_WIDTH, SCROLLBAR_WIDTH,
        );
        let _ = FillRgn(mem_dc, thumb_rgn, thumb_brush);
        let _ = DeleteObject(thumb_rgn.into());
        let _ = DeleteObject(thumb_brush.into());
    }

    // Draw dropdown list if open (drawn on top)
    if data.dropdown_open {
        // Recreate font for dropdown list
        let dropdown_font = CreateFontW(
            -14, 0, 0, 0, 400, 0, 0, 0,
            FONT_CHARSET(1), FONT_OUTPUT_PRECISION(0), FONT_CLIP_PRECISION(0), FONT_QUALITY(0),
            0, windows::core::w!("Segoe UI"),
        );
        let old_font = SelectObject(mem_dc, dropdown_font.into());

        let dropdown_list_item_height = 28;
        let dropdown_list_top = data.dropdown_rect.bottom + 2;
        let dropdown_list_left = data.dropdown_rect.left;
        let dropdown_list_right = data.dropdown_rect.right;
        let dropdown_list_bottom = dropdown_list_top + dropdown_list_item_height * 2 + 4;

        // Draw dropdown list background with shadow effect
        let shadow_rect = RECT {
            left: dropdown_list_left + 2,
            top: dropdown_list_top + 2,
            right: dropdown_list_right + 2,
            bottom: dropdown_list_bottom + 2,
        };
        let shadow_brush = CreateSolidBrush(COLORREF(0x00202020));
        let shadow_rgn = CreateRoundRectRgn(
            shadow_rect.left, shadow_rect.top,
            shadow_rect.right, shadow_rect.bottom,
            6, 6,
        );
        let _ = FillRgn(mem_dc, shadow_rgn, shadow_brush);
        let _ = DeleteObject(shadow_rgn.into());
        let _ = DeleteObject(shadow_brush.into());

        // Draw dropdown list background
        let dropdown_list_rect = RECT {
            left: dropdown_list_left,
            top: dropdown_list_top,
            right: dropdown_list_right,
            bottom: dropdown_list_bottom,
        };
        let dropdown_list_brush = CreateSolidBrush(COLORREF(data.colors.background));
        let dropdown_list_rgn = CreateRoundRectRgn(
            dropdown_list_rect.left, dropdown_list_rect.top,
            dropdown_list_rect.right, dropdown_list_rect.bottom,
            6, 6,
        );
        let _ = FillRgn(mem_dc, dropdown_list_rgn, dropdown_list_brush);
        let _ = DeleteObject(dropdown_list_rgn.into());
        let _ = DeleteObject(dropdown_list_brush.into());

        // Draw border
        let border_pen = CreatePen(PS_SOLID, 1, COLORREF(data.colors.search_border));
        let old_pen = SelectObject(mem_dc, border_pen.into());
        let border_rgn = CreateRoundRectRgn(
            dropdown_list_rect.left, dropdown_list_rect.top,
            dropdown_list_rect.right, dropdown_list_rect.bottom,
            6, 6,
        );
        let _ = FrameRgn(mem_dc, border_rgn, HBRUSH(border_pen.0 as *mut c_void), 1, 1);
        let _ = DeleteObject(border_rgn.into());
        let _ = SelectObject(mem_dc, old_pen);
        let _ = DeleteObject(border_pen.into());

        // Draw dropdown items
        let items = ["Fuzzy", "Regex"];
        let mut item_y = dropdown_list_top + 2;

        for (i, item_text) in items.iter().enumerate() {
            let item_rect = RECT {
                left: dropdown_list_left + 2,
                top: item_y,
                right: dropdown_list_right - 2,
                bottom: item_y + dropdown_list_item_height,
            };

            // Determine if item is selected or hovered
            let is_current = (i == 0 && data.search_type == SearchType::Fuzzy)
                || (i == 1 && data.search_type == SearchType::Regex);
            let is_hovered = data.dropdown_hovered_index == Some(i);

            // Draw hover/selected background
            if is_hovered || is_current {
                let item_bg_color = if is_hovered {
                    data.colors.hover
                } else {
                    data.colors.selected
                };
                let item_brush = CreateSolidBrush(COLORREF(item_bg_color));
                let item_rgn = CreateRoundRectRgn(
                    item_rect.left, item_rect.top,
                    item_rect.right, item_rect.bottom,
                    4, 4,
                );
                let _ = FillRgn(mem_dc, item_rgn, item_brush);
                let _ = DeleteObject(item_rgn.into());
                let _ = DeleteObject(item_brush.into());
            }

            // Draw checkmark if current selection
            if is_current {
                let check_x = item_rect.left + 6;
                let check_y = item_rect.top + dropdown_list_item_height / 2;
                let check_pen = CreatePen(PS_SOLID, 2, COLORREF(data.colors.search_border_focused));
                let old_pen = SelectObject(mem_dc, check_pen.into());
                // Draw checkmark
                let _ = MoveToEx(mem_dc, check_x, check_y, None);
                let _ = LineTo(mem_dc, check_x + 4, check_y + 4);
                let _ = MoveToEx(mem_dc, check_x + 4, check_y + 4, None);
                let _ = LineTo(mem_dc, check_x + 12, check_y - 4);
                let _ = SelectObject(mem_dc, old_pen);
                let _ = DeleteObject(check_pen.into());
            }

            // Draw item text
            let _ = SetTextColor(mem_dc, COLORREF(data.colors.text));
            let mut text_item_rect = RECT {
                left: item_rect.left + 22,
                top: item_rect.top,
                right: item_rect.right - 4,
                bottom: item_rect.bottom,
            };
            let mut text_vec: Vec<u16> = item_text.encode_utf16().chain(std::iter::once(0)).collect();
            let _ = DrawTextW(mem_dc, &mut text_vec, &mut text_item_rect,
                              DT_LEFT | DT_VCENTER | DT_SINGLELINE);

            item_y += dropdown_list_item_height;
        }

        let _ = SelectObject(mem_dc, old_font);
        let _ = DeleteObject(dropdown_font.into());
    }

    let _ = BitBlt(hdc, 0, 0, rect.right, rect.bottom, Some(mem_dc), 0, 0, SRCCOPY);

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
            biSize: size_of::<BITMAPINFOHEADER>() as u32,
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
            biSize: size_of::<BITMAPINFOHEADER>() as u32,
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
