fn main() {
    #[cfg(windows)]
    {
        let mut res = winres::WindowsResource::new();
        res.set_icon("assets/app.ico");
        res.set("ProductName", "Clipcy");
        res.set("FileDescription", "Clipcy - Cross-platform Clipboard Manager");
        res.set("LegalCopyright", "Copyright (c) 2024 ThanhDA");
        res.compile().expect("Failed to compile Windows resources");
    }
}
