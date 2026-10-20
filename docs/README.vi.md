<div align="center">

<img src="../apps/clipcy-windows/Assets/Square150x150Logo.png" alt="Logo Clipcy" width="128" />

# Clipcy

**Trình quản lý bảng nhớ tạm nhanh, hiện đại, sử dụng Rust + OS native UI.**

[![Giấy phép: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](../LICENSE)
[![Nền tảng: Windows](https://img.shields.io/badge/Platform-Windows-0078D4.svg)](https://www.microsoft.com/windows)
[![Rust](https://img.shields.io/badge/Rust-1.70%2B-orange.svg)](https://www.rust-lang.org/)

[English](../README.md) | Tiếng Việt

</div>

---

# [Clipcy](https://clipcy.dev/vi)

Clipcy là một trình quản lý bảng nhớ tạm nhanh, nhẹ, hiện đại dành cho window, được lấy cảm hứng từ [Maccy](https://github.com/p0deje/Maccy).

Nó cho phép bạn lưu trữ lịch sử bảng nhớ tạm, xem trước nội dung, ghim các mục quan trọng và dán chúng bằng phím tắt toàn hệ thống.

Clipcy hoạt động trên Windows 10 (19041) trở lên (các bản OS cũ hơn chưa test).

## Mục lục

- [Tính năng](#tính-năng)
- [Hỗ trợ nền tảng](#hỗ-trợ-nền-tảng)
- [Cài đặt](#cài-đặt)
- [Phát triển](#phát-triển)
- [Giấy phép](#giấy-phép)

---

## Tính năng

- **Nhẹ và rất nhanh**.
- **Thiết kế hướng đến bàn phím**, với phím tắt toàn hệ thống
- **Tạm dừng thu thập**: mục khay **Tạm dừng Clipcy** / **Tiếp tục Clipcy** chỉ chặn bản ghi mới. Lịch sử, dán và phím tắt vẫn dùng được. Khởi động lại thì tiếp tục thu thập. Clipcy chưa xóa mục khi trình quản lý mật khẩu xóa clipboard, vì chưa có tín hiệu gắn được với Bitwarden, 1Password hoặc NordPass.
- **Tôn trọng yêu cầu loại trừ clipboard**: trong **Cài đặt → Hoạt động**, bật **Tôn trọng yêu cầu loại trừ khỏi lịch sử bảng nhớ tạm** để bỏ qua bản sao mới có định dạng `ExcludeClipboardContentFromMonitorProcessing`. Mặc định tắt; lưu và áp dụng ngay, không xóa lịch sử cũ và không tự nhận diện trình quản lý mật khẩu.
- **UI gốc hệ điều hành**. Tương thích với theme và màu sắc của hệ điều hành.
- **Miễn phí, mã nguồn mở** (MIT)

## Hỗ trợ nền tảng

| Nền tảng | Công nghệ giao diện | Trạng thái |
| ---------- | --------------------- | ------------ |
| Windows | WinUI 3 (C#) | ✅ Đã triển khai |
| macOS | SwiftUI | 🔜 Dự kiến |
| Linux | GTK4 | 🔜 Dự kiến |

---

## Cài đặt

Tải ở trang [Releases]() hoặc dùng [winget](https://winget.run/pkg/clipcy/clipcy):

```powershell
winget install clipcy.clipcy
```

### Yêu cầu

#### Môi trường phát triển

- Windows 10 phiên bản 2004 trở lên (build 19041).
- [Rust](https://rustup.rs/) 1.75 trở lên.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Visual Studio 2022 với workload "Windows App SDK".

#### Môi trường chạy

- Windows 10 phiên bản 2004 trở lên (build 19041) hoặc Windows 11.
- Windows App SDK Runtime (được đóng gói kèm).

### Bắt đầu nhanh

Chạy các lệnh bên dưới từ thư mục gốc của dự án, trừ khi có hướng dẫn chuyển thư mục.

#### Sử dụng script build

```powershell
# Build Debug
.\scripts\build-windows-ui.ps1

# Build Release
.\scripts\build-windows-ui.ps1 -Release

# Build lại từ đầu
.\scripts\build-windows-ui.ps1 -CleanBuild
```

#### Tạo bộ cài đặt Windows

Cần Inno Setup 6 trở lên (`ISCC.exe` có trong PATH).

Script kiểm tra PATH của tiến trình hiện tại và các giá trị PATH đã lưu của người dùng/hệ thống, kể cả khi terminal được mở trước lúc cài Inno Setup. Dùng `-InnoSetupPath` để chỉ định trực tiếp trình biên dịch.

Quá trình publish bản Release vẫn bật trimming. Dự án WinUI ghim phiên bản gói Windows SDK projection trong `apps/clipcy-windows/clipcy-windows.csproj` để tránh cảnh báo trimming liên quan đến interop; cài đặt cập nhật, dữ liệu GitHub Releases và phím tắt cho mục đã ghim sử dụng metadata JSON được sinh từ mã nguồn.

```powershell
# powershell -ExecutionPolicy Bypass -File scripts/build-windows-installer.ps1
.\scripts\build-windows-installer.ps1

# Khi Inno Setup không có trong PATH
.\scripts\build-windows-installer.ps1 -InnoSetupPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Đầu ra:

- Ứng dụng đã publish: `dist\windows\win-x64`.
- Bộ cài đặt: `dist\installer`.

### Tự động cập nhật bằng MSIX (WinUI)

Clipcy hỗ trợ tự động cập nhật trên Windows thông qua MSIX + App Installer. Các gói được lưu trên GitHub Releases.

#### Tài nguyên bắt buộc

MSIX cần các tệp PNG sau trong `apps/clipcy-windows/Assets/`:

- `StoreLogo.png`
- `Square44x44Logo.png`
- `Square150x150Logo.png`
- `SplashScreen.png`

Tạo các tệp này từ `icon.ico`:

```powershell
./scripts/generate-msix-assets.ps1
```

#### Build MSIX trên máy local

```powershell
./scripts/build-msix-release.ps1 -Repo ORG/REPO -CertificatePath "path\to\cert.pfx" -CertificatePassword "password"
```

Đầu ra:

- `dist\msix\win-x64\Clipcy.msix`
- `dist\msix\win-x64\Clipcy.appinstaller`

Phiên bản được lấy từ `Cargo.toml` (`[workspace.package].version`) và chuyển sang định dạng MSIX gồm bốn thành phần (ví dụ: `0.1.0` → `0.1.0.0`).

#### Workflow phát hành trên GitHub

Workflow: [release-msix.yml](../.github/workflows/release-msix.yml).

Các secret cần có:

- `MSIX_CERT_BASE64` (chứng chỉ PFX được mã hóa base64).
- `MSIX_CERT_PASSWORD`.

Chạy workflow; có thể để trống `version` để sử dụng phiên bản trong `Cargo.toml`.

#### Các bước phát hành

1. Đảm bảo `Publisher` trong `apps/clipcy-windows/clipcy-windows.csproj` và `apps/clipcy-windows/Package.appxmanifest` khớp với subject của chứng chỉ.
2. Build và ký MSIX trên máy local hoặc CI.
3. Tải `Clipcy.msix` và `Clipcy.appinstaller` lên GitHub Release `vX.Y.Z`.
4. Chia sẻ URL của tệp `.appinstaller`. Windows sẽ tự kiểm tra cập nhật khi khởi chạy.

Lưu ý: Bộ cài Inno Setup được build riêng và sử dụng quy trình publish EXE. Script `scripts/build-windows-installer.ps1` buộc `WindowsPackageType=None` để không xung đột với quá trình đóng gói MSIX.

#### Build thủ công

1. **Build cầu nối Rust (cdylib)**

   Có thể dùng phiên bản khác tương thích với UniFFI v0.29. Xem [uniffi-bindgen-cs](https://github.com/NordSecurity/uniffi-bindgen-cs).

   ```bash
   cargo install uniffi-bindgen-cs --git https://github.com/NordSecurity/uniffi-bindgen-cs --tag v0.10.0+v0.29.4
   ```

   ```bash
   cargo build --release -p clipcy-bridge
   # Đầu ra: target/release/clipcy_core.dll
   ```

2. **Sinh bindings C#**

   ```bash
   # Cần uniffi-bindgen-cs
   uniffi-bindgen-cs --library target/release/clipcy_core.dll --out-dir apps/clipcy-windows/Bindings
   ```

3. **Build ứng dụng WinUI 3**

   ```bash
   cd apps/clipcy-windows
   dotnet run
   # Hoặc build bản Release
   dotnet build -c Release
   ```

### Cấu trúc dự án

```text
clipcy/
├── apps/
│   ├── clipcy-windows/       # Ứng dụng Windows WinUI 3 (C#)
│   ├── clipcy-macos/         # Ứng dụng macOS (thư mục dự phòng)
│   └── clipcy-linux/         # Ứng dụng Linux (thư mục dự phòng)
├── crates/
│   ├── bridge/               # Cầu nối UniFFI cho giao diện gốc
│   ├── core/                 # Logic miền nghiệp vụ và các port
│   ├── storage/              # Repository SQLite
│   ├── platform-windows/     # Thành phần xử lý riêng cho Windows
│   └── settings-common/      # Kiểu dữ liệu cài đặt dùng chung
└── scripts/
    └── build-windows-ui.ps1  # Script build Windows
```

### Cấu hình

Cài đặt được lưu tại `%LOCALAPPDATA%\clipcy\settings.json`:

```json
{
  "max_history_items": 100,
  "hotkey": "Alt+V",
  "theme": "system",
  "start_on_boot": true,
  "show_notifications": false,
  "show_preview_panel": true,
  "excluded_apps": []
}
```

Cơ sở dữ liệu: `%LOCALAPPDATA%\clipcy\clipcy.db`.

### Cách sử dụng

1. Chạy `Clipcy.exe`.
2. Biểu tượng xuất hiện trong khay hệ thống.
3. Nhấn `Alt+V` để mở lịch sử bảng nhớ tạm.
4. Nhấp vào một mục để dán.
5. Nhấp chuột phải vào biểu tượng khay để mở menu (Cài đặt, Xóa lịch sử, Thoát).

### Khắc phục sự cố

#### Phím tắt không hoạt động

- Kiểm tra xem ứng dụng khác có đang sử dụng cùng phím tắt hay không.
- Đổi phím tắt trong phần Cài đặt.

#### Không thấy biểu tượng khay hệ thống

- Nhấp vào mũi tên `^` trên thanh tác vụ Windows để xem các biểu tượng bị ẩn.
- Kéo biểu tượng ra vùng hiển thị trên thanh tác vụ.

#### Ứng dụng bị lỗi khi khởi động

- Đảm bảo đã cài .NET 10 Runtime.
- Kiểm tra log tại `%LOCALAPPDATA%\clipcy\logs\`.

---

## macOS

*Sắp có — dự kiến triển khai bằng SwiftUI.*

### Tính năng dự kiến

- Ứng dụng trên thanh menu với `NSStatusItem`.
- `MenuBarExtra` cho macOS 13 trở lên.
- `NSPanel` nổi cho cửa sổ bật lên.

---

## Linux

*Sắp có — dự kiến triển khai bằng GTK4.*

---

### Kiến trúc lục giác

Dự án tuân theo kiến trúc Hexagonal (Ports & Adapters):

- **Core**: Mô hình miền nghiệp vụ, port (trait) và dịch vụ ứng dụng.
- **Adapters**: Triển khai riêng cho từng nền tảng và lưu trữ SQLite.
- **Bridge**: Lớp FFI UniFFI để kết nối giao diện gốc.
- **Apps**: Ứng dụng giao diện gốc cho từng nền tảng.

### Thêm nền tảng mới

1. Tạo các thành phần xử lý nền tảng trong `crates/platform-{os}/`.
2. Triển khai các trait của core (`ClipboardMonitor`, `HotkeyHandler`, v.v.).
3. Build cầu nối: `cargo build -p clipcy-bridge`.
4. Sinh bindings cho ngôn ngữ tương ứng (Swift, Kotlin, v.v.).
5. Tạo ứng dụng giao diện gốc trong `apps/clipcy-{os}/`.

---

## Giấy phép

[MIT](../LICENSE)
