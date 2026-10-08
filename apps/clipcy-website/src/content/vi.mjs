/**
 * Vietnamese website content catalog
 */

export default {
  meta: {
    title: 'Clipcy - Trình quản lý clipboard cho Windows',
    description: 'Tìm văn bản, hình ảnh và tệp đã sao chép. Ghim những gì bạn sử dụng lại. Dán bằng Alt+V.',
    ogTitle: 'Sao chép một lần. Dùng lại bất cứ lúc nào.',
    ogDescription: 'Trình quản lý clipboard cho Windows. Tìm kiếm lịch sử, ghim mục, xem trước trước khi dán.'
  },
  nav: {
    features: 'Tính năng',
    howItWorks: 'Cách hoạt động',
    download: 'Tải xuống',
    privacy: 'Quyền riêng tư',
    chooseLanguage: 'Chọn ngôn ngữ',
    useBrowserLanguage: 'Sử dụng ngôn ngữ trình duyệt'
  },
  hero: {
    eyebrow: 'Trình quản lý clipboard miễn phí cho Windows',
    title: 'Sao chép một lần. Dùng lại bất cứ lúc nào.',
    subtitle: 'Làm đúng một việc: giữ nội dung bạn đã copy trong tầm tay. Chấm hết!',
    demoPinHint: 'Nhấn Ctrl+P để ghim',
    demoUnpinHint: 'Nhấn Ctrl+P để bỏ ghim',
    windowsSupport: 'Hỗ trợ Windows 10+',
    freeOpenSource: 'Miễn phí và mã nguồn mở',
    ctaOr: 'hoặc',
    ctaStore: 'Tải từ Microsoft Store',
    ctaDownload: 'Tải xuống ngay',
    ctaPrimary: 'Tải xuống cho Windows',
    ctaSecondary: 'Xem hướng dẫn xây dựng',
    noReleaseNotice: 'Hiện không có bản phát hành công khai. Xem hướng dẫn xây dựng bên dưới.',
    imageAlt: 'Cửa sổ bật lên Clipcy hiển thị các mục đã ghim, trường tìm kiếm, danh sách lịch sử và bảng xem trước',
    imageCaption: 'Mở bằng Alt+V. Tìm kiếm, chọn, dán.'
  },
  problem: {
    title: 'Giữ nội dung sao chép ngay trong tầm tay. Hỗ trợ file, ảnh, text ...',
    body: 'Lấy các bản sao trước đó mà không cần chuyển cửa sổ hoặc mất vị trí làm việc.'
  },
  features: {
    heading: 'Tính năng',
    find: {
      title: 'Tìm bản sao bạn cần',
      body: 'Tìm kiếm trong lịch sử clipboard của bạn. Văn bản, hình ảnh và tệp.',
      label: 'Lịch sử & tìm kiếm'
    },
    pin: {
      title: 'Giữ các lần dán lặp lại ở trên cùng',
      body: 'Nhấn Ctrl+P để ghim các mục bạn thường sử dụng.',
      label: 'Ghim'
    },
    preview: {
      title: 'Xem trước trước khi dán',
      body: 'Xem văn bản, hình ảnh và chi tiết tệp trước khi dán.',
      label: 'Văn bản, hình ảnh, tệp'
    }
  },
  workflow: {
    heading: 'Sao chép. Tìm. Dán.',
    step1: {
      title: '1. Sao chép trong ứng dụng khác',
      body: 'Sao chép văn bản, hình ảnh hoặc tệp như thường lệ.'
    },
    step2: {
      title: '2. Mở Clipcy bằng Alt+V',
      body: 'Nhấn Alt+V (có thể cấu hình) và nhập để tìm kiếm.'
    },
    step3: {
      title: '3. Chọn và dán',
      body: 'Sử dụng phím mũi tên để chọn. Nhấn Enter để dán.'
    },
    shortcuts: {
      open: 'Alt+V',
      navigate: 'Lên/Xuống',
      paste: 'Enter',
      close: 'Esc',
      pin: 'Ctrl+P'
    }
  },
  trust: {
    heading: 'Lịch sử của bạn được lưu trữ trên PC này.',
    storage: 'Lịch sử clipboard được lưu cục bộ trong cơ sở dữ liệu SQLite tại %LOCALAPPDATA%\\clipcy\\clipcy.db.',
    warning: 'Lịch sử clipboard có thể chứa nội dung nhạy cảm. Lưu trữ cục bộ không có nghĩa là mã hóa.',
    support: {
      windows: 'Windows 10 phiên bản 1809+ hoặc Windows 11',
      languages: 'Giao diện ứng dụng: English, 简体中文, 日本語, Русский, 한국어, Tiếng Việt'
    },
    privacyLink: 'Đọc chính sách quyền riêng tư của chúng tôi'
  },
  faq: {
    heading: 'Câu hỏi thường gặp',
    q1: {
      question: 'Những nền tảng nào được hỗ trợ?',
      answer: 'Sẵn có cho Windows 10, 11. MacOS và Linux đang trong kế hoạch phát triển'
    },
    q2: {
      question: 'Làm thế nào để mở Clipcy?',
      answer: 'Nhấn Alt+V (mặc định). Bạn có thể thay đổi phím tắt trong Cài đặt.'
    },
    q3: {
      question: 'Lịch sử clipboard được lưu ở đâu?',
      answer: 'Cục bộ trên PC của bạn tại %LOCALAPPDATA%\\clipcy\\clipcy.db. Không có đồng bộ đám mây hoặc lưu trữ từ xa.'
    },
    q4: {
      question: 'Clipcy có lưu tệp khi được sao chép không?',
      answer: 'Không. Tính năng xem trước tệp đọc đường dẫn của tệp hiện tại. Nếu bạn di chuyển hoặc xóa tệp, xem trước sẽ không còn hoạt động.'
    }
  },
  comparison: {
    "heading": "Thay thế hoàn hảo cho Win+V",
    "body": "Tất cả những điều bạn cần ở một bộ quản lý clipboard",
    "eyebrow": "So sánh",
    "windows": "Clipboard Windows",
    "rows": [
      {
        "label": "Lịch sử",
        "clipcy": "Lên đến 1.000 mục. Luôn sẵn sàng khi bạn cần.",
        "windows": "Tối đa 25 mục gần nhất; mục chưa ghim bị mất khi khởi động lại.",
        "clipcyPro": true,
        "windowsPro": false
      },
      {
        "label": "Xem trước nội dung",
        "clipcy": "Xem trước cho văn bản, ảnh và tệp. Bạn luôn biết mình sẽ dán nội dung gì.",
        "windows": "Không có",
        "clipcyPro": true,
        "windowsPro": false
      },
      {
        "label": "Tốc độ",
        "clipcy": "Native, rất rất nhanh",
        "windows": "Ổn",
        "clipcyPro": true,
        "windowsPro": true
      }
    ],
    "source": "Tài liệu clipboard của Microsoft",
    "benefits": [
      {
        "title": "Không rườm rà",
        "body": "Làm tốt đúng một việc: quản lý nội dung copy. Không có các tính năng rườm rà khác."
      },
      {
        "title": "Bảo mật & riêng tư",
        "body": "Clipcy tôn trọng sự riêng tư của bạn. Nếu app quản lý mật khẩu của bạn xóa mật khẩu khỏi clipboard, Clipcy cũng sẽ làm vậy. Tất cả mọi thứ đều được lưu trên máy của bạn"
      },
      {
        "title": "Ưu tiên bàn phím",
        "body": "Tất cả các thao tác đều thực hiện bằng bàn phím. Bạn không cần mất thời gian để rờ tay vào chuột"
      },
      {
        "title": "Mã nguồn mở",
        "body": "Clipcy là mã nguồn mở, sử dụng giấy phép MIT. Luôn miễn phí."
      }
    ]
  },
  finalCta: {
    "heading": "Bạn cần tính năng mới?",
    "body": "Chia sẻ ý tưởng, báo lỗi hoặc cùng xây dựng tính năng mới. Hãy cùng làm Clipcy tốt hơn.",
    "contribute": "Đóng góp trên GitHub"
  },
  footer: {
    version: 'Phiên bản',
    repository: 'Kho lưu trữ',
    privacy: 'Quyền riêng tư',
    license: 'Giấy phép MIT'
  },
  download: {
    heading: 'Tải Clipcy cho Windows',
    noRelease: {
      title: 'Không có bản phát hành công khai',
      body: 'Gói công khai đã xác minh hiện không có sẵn. Bạn có thể xây dựng Clipcy từ nguồn bằng các hướng dẫn bên dưới.'
    },
    requirements: {
      heading: 'Yêu cầu',
      runtime: 'Windows 10 phiên bản 1809+ hoặc Windows 11',
      runtimeNote: 'Windows App SDK Runtime (đi kèm trong bản dựng)'
    },
    buildInstructions: {
      heading: 'Xây dựng từ nguồn',
      prerequisites: {
        heading: 'Điều kiện tiên quyết phát triển',
        items: [
          'Windows 10 phiên bản 1809+ (bản dựng 17763)',
          'Rust 1.75+',
          '.NET 10 SDK',
          'Visual Studio 2022 with "Windows App SDK" workload'
        ]
      },
      steps: {
        heading: 'Các bước xây dựng',
        step1: 'Sao chép kho lưu trữ',
        step2: 'Xây dựng cầu nối Rust',
        step3: 'Tạo liên kết C# (yêu cầu uniffi-bindgen-cs)',
        step4: 'Xây dựng ứng dụng WinUI',
        runNote: 'Chạy Clipcy.exe từ thư mục đầu ra bản dựng.'
      }
    },
    usage: {
      heading: 'Cách sử dụng',
      step1: 'Chạy Clipcy.exe',
      step2: 'Biểu tượng xuất hiện trong khay hệ thống',
      step3: 'Nhấn Alt+V để mở lịch sử clipboard',
      step4: 'Nhấp vào một mục để dán',
      step5: 'Nhấp chuột phải vào biểu tượng khay để Cài đặt, Xóa lịch sử hoặc Thoát'
    }
  },
  privacy: {
    heading: 'Chính sách quyền riêng tư',
    effectiveDate: 'Ngày có hiệu lực',
    appData: {
      heading: 'Dữ liệu clipboard',
      clipboard: 'Clipcy ghi lại nội dung clipboard (văn bản, hình ảnh, đường dẫn tệp) khi bạn sao chép.',
      storage: 'Lịch sử được lưu cục bộ trong %LOCALAPPDATA%\\clipcy\\clipcy.db.',
      previews: 'Xem trước tệp đọc tệp hiện tại tại đường dẫn gốc. Tệp không được lưu trữ hoặc sao chép.',
      sensitive: 'Lịch sử clipboard có thể chứa nội dung nhạy cảm. Clipcy không mã hóa cơ sở dữ liệu cục bộ.',
      settings: 'Cài đặt được lưu cục bộ trong %LOCALAPPDATA%\\clipcy\\settings.json.'
    },
    websiteData: {
      heading: 'Dữ liệu trang web',
      noAnalytics: 'Trang web này không sử dụng phân tích, cookie theo dõi hoặc tập lệnh của bên thứ ba.',
      noFonts: 'Không có yêu cầu phông chữ từ xa.',
      noForms: 'Không có gửi biểu mẫu hoặc tạo tài khoản.',
      language: 'Tùy chọn ngôn ngữ được lưu cục bộ trong trình duyệt của bạn (localStorage) và không được gửi đến bất kỳ máy chủ nào.',
      hosting: 'Nhật ký truy cập máy chủ web tiêu chuẩn có thể ghi lại địa chỉ IP và đường dẫn yêu cầu. Chính sách ghi nhật ký được xác định bởi nhà cung cấp dịch vụ lưu trữ.'
    },
    changes: {
      heading: 'Thay đổi đối với chính sách này',
      body: 'Cập nhật chính sách quyền riêng tư sẽ được đăng trên trang này với ngày có hiệu lực mới.'
    }
  },
  actions: {
    copy: 'Sao chép',
    copied: 'Đã sao chép!',
    copyFailed: 'Sao chép thất bại',
    copyPage: 'Sao chép trang',
    copyUrl: 'Sao chép URL',
    viewMarkdown: 'Xem dưới dạng Markdown',
    share: 'Chia sẻ',
    shareVia: 'Chia sẻ qua',
    email: 'Email',
    aiActions: 'Hành động AI',
    askChatGPT: 'Hỏi ChatGPT',
    askClaude: 'Hỏi Claude',
    askPerplexity: 'Hỏi Perplexity',
    askGemini: 'Hỏi Gemini',
    permissionDenied: 'Quyền clipboard bị từ chối',
    fallbackInstructions: 'Chọn và sao chép văn bản bên dưới:',
    close: 'Đóng'
  },
  a11y: {
    skipToMain: 'Bỏ qua đến nội dung chính',
    logo: 'Trang chủ Clipcy',
    menu: 'Menu điều hướng',
    languageMenu: 'Menu ngôn ngữ',
    externalLink: 'Mở trong tab mới',
    loading: 'Đang tải...'
  }
};
