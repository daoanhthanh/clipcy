/**
 * Simplified Chinese website content catalog
 */

export default {
  meta: {
    title: 'Clipcy - Windows 剪贴板管理器',
    description: '查找复制的文本、图片和文件。固定常用项。使用 Alt+V 粘贴。',
    ogTitle: '复制一次，随时再用。',
    ogDescription: 'Windows 剪贴板管理器。搜索历史记录，固定项目，粘贴前预览。'
  },
  
  nav: {
    features: '功能',
    howItWorks: '工作原理',
    download: '下载',
    privacy: '隐私',
    chooseLanguage: '选择语言',
    useBrowserLanguage: '使用浏览器语言'
  },
  
  hero: {
    eyebrow: '免费的 Windows 剪贴板管理器',
    title: '复制一次，随时再用。',
    subtitle: '只做好一件事：把复制过的内容留在手边。就这么简单！',
    demoPinHint: '按 Ctrl+P 固定',
    demoUnpinHint: '按 Ctrl+P 取消固定',
    windowsSupport: '支持 Windows 10 及以上版本',
    freeOpenSource: '免费且开源',
    ctaOr: '或',
    ctaStore: '从 Microsoft Store 获取',
    ctaDownload: '立即下载',
    ctaPrimary: '下载 Windows 版',
    ctaSecondary: '查看构建说明',
    noReleaseNotice: '当前没有公开发布版本。请参阅下面的构建说明。',
    imageAlt: 'Clipcy 弹出窗口显示固定项、搜索字段、历史列表和预览面板',
    imageCaption: '使用 Alt+V 打开。搜索、选择、粘贴。'
  },
  
  problem: {
    title: '复制过的文字、图片和文件，随时都能找回来。',
    body: '不用切换窗口，也不会打断手头的工作，就能找回之前复制的内容。'
  },
  
  features: {
    heading: '功能',
    find: {
      title: '查找所需的副本',
      body: '搜索剪贴板历史记录。文本、图片和文件。',
      label: '历史记录和搜索'
    },
    pin: {
      title: '将常用粘贴固定在顶部',
      body: '按 Ctrl+P 固定常用项目。',
      label: '固定'
    },
    preview: {
      title: '粘贴前预览',
      body: '粘贴前查看文本、图片和文件详情。',
      label: '文本、图片、文件'
    }
  },
  
  workflow: {
    heading: '复制。查找。粘贴。',
    step1: {
      title: '1. 在其他应用中复制',
      body: '像往常一样复制文本、图片或文件。'
    },
    step2: {
      title: '2. 使用 Alt+V 打开 Clipcy',
      body: '按 Alt+V（可配置）并输入以搜索。'
    },
    step3: {
      title: '3. 选择并粘贴',
      body: '使用箭头键选择。按 Enter 粘贴。'
    },
    shortcuts: {
      open: 'Alt+V',
      navigate: '上/下',
      paste: 'Enter',
      close: 'Esc',
      pin: 'Ctrl+P'
    }
  },
  
  trust: {
    heading: '您的历史记录存储在此电脑上。',
    storage: '剪贴板历史记录本地保存在 %LOCALAPPDATA%\\clipcy\\clipcy.db 的 SQLite 数据库中。',
    warning: '剪贴板历史记录可能包含敏感内容。本地存储并不意味着加密。',
    support: {
      windows: 'Windows 10 版本 1809+ 或 Windows 11',
      languages: '应用界面：English、简体中文、日本語、Русский、한국어、Tiếng Việt'
    },
    privacyLink: '阅读我们的隐私政策'
  },
  
  faq: {
    heading: '常见问题',
    q1: {
      question: '支持哪些平台？',
      answer: 'Windows 已实现。macOS 和 Linux 已计划但尚未提供。'
    },
    q2: {
      question: '如何打开 Clipcy？',
      answer: '按 Alt+V（默认）。您可以在设置中更改热键。'
    },
    q3: {
      question: '剪贴板历史记录存储在哪里？',
      answer: '本地存储在您的电脑上 %LOCALAPPDATA%\\clipcy\\clipcy.db。没有云同步或远程存储。'
    },
    q4: {
      question: '复制的文件是否被归档？',
      answer: '否。文件预览读取当前文件路径。如果您移动或删除文件，预览将不再工作。'
    }
  },
  
  comparison: {
    heading: '比 Win+V 更顺手的选择',
    body: '剪贴板管理，该有的功能都有。',
    eyebrow: '对比',
    windows: 'Windows 剪贴板',
    rows: [
      {
        label: '历史记录',
        clipcy: '最多保存 1,000 条。需要时随时可用。',
        windows: '最多保存最近 25 条；未固定的内容重启后就会消失。',
        clipcyPro: true,
        windowsPro: false
      },
      {
        label: '内容预览',
        clipcy: '粘贴前先预览文本、图片和文件。你总是知道自己要粘贴什么。',
        windows: '无',
        clipcyPro: true,
        windowsPro: false
      },
      {
        label: '速度',
        clipcy: '原生。真的很快。',
        windows: '不错',
        clipcyPro: true,
        windowsPro: true
      }
    ],
    source: 'Microsoft 剪贴板文档',
    benefits: [
      {
        title: '简单不折腾',
        body: '专心管理复制内容。不塞一堆用不上的功能进来。'
      },
      {
        title: '隐私优先',
        body: 'Clipcy 尊重你的隐私。如果密码管理器从剪贴板里清除了密码，Clipcy 也会同步清除。所有内容都保存在你的电脑上。'
      },
      {
        title: '键盘优先',
        body: '全程用键盘就能完成操作。不用伸手去够鼠标。'
      },
      {
        title: '开源免费',
        body: 'Clipcy 采用 MIT 开源许可证。永久免费。'
      }
    ]
  },

  finalCta: {
  "heading": "想参与功能开发？",
  "body": "分享想法、报告问题，或一起开发新功能。让我们共同改进 Clipcy。",
  "contribute": "在 GitHub 上贡献"
},
  
  footer: {
    version: '版本',
    repository: '仓库',
    privacy: '隐私',
    license: 'MIT 许可证'
  },
  
  download: {
    heading: '获取 Windows 版 Clipcy',
    noRelease: {
      title: '没有公开发布版本',
      body: '当前没有经过验证的公开软件包。您可以使用下面的说明从源代码构建 Clipcy。'
    },
    requirements: {
      heading: '系统要求',
      runtime: 'Windows 10 版本 1809+ 或 Windows 11',
      runtimeNote: 'Windows App SDK Runtime（内置于构建中）'
    },
    buildInstructions: {
      heading: '从源代码构建',
      prerequisites: {
        heading: '开发前提条件',
        items: [
          'Windows 10 版本 1809+（构建 17763）',
          'Rust 1.75+',
          '.NET 10 SDK',
          'Visual Studio 2022 带"Windows App SDK"工作负载'
        ]
      },
      steps: {
        heading: '构建步骤',
        step1: '克隆仓库',
        step2: '构建 Rust 桥',
        step3: '生成 C# 绑定（需要 uniffi-bindgen-cs）',
        step4: '构建 WinUI 应用',
        runNote: '从构建输出目录运行 Clipcy.exe。'
      }
    },
    usage: {
      heading: '使用方法',
      step1: '运行 Clipcy.exe',
      step2: '图标出现在系统托盘中',
      step3: '按 Alt+V 打开剪贴板历史记录',
      step4: '单击项目进行粘贴',
      step5: '右键单击托盘图标以访问设置、清除历史记录或退出'
    }
  },
  
  privacy: {
    heading: '隐私政策',
    effectiveDate: '生效日期',
    appData: {
      heading: '剪贴板数据',
      clipboard: 'Clipcy 在您复制时捕获剪贴板内容（文本、图片、文件路径）。',
      storage: '历史记录本地存储在 %LOCALAPPDATA%\\clipcy\\clipcy.db 中。',
      previews: '文件预览读取原始路径上的当前文件。文件不会被归档或复制。',
      sensitive: '剪贴板历史记录可能包含敏感内容。Clipcy 不加密本地数据库。',
      settings: '设置本地存储在 %LOCALAPPDATA%\\clipcy\\settings.json 中。'
    },
    websiteData: {
      heading: '网站数据',
      noAnalytics: '此网站不使用分析、跟踪 cookie 或第三方脚本。',
      noFonts: '无远程字体请求。',
      noForms: '无表单提交或帐户创建。',
      language: '语言偏好本地存储在您的浏览器中（localStorage），不会发送到任何服务器。',
      hosting: '标准 Web 服务器访问日志可能记录 IP 地址和请求路径。日志记录策略由托管提供商确定。'
    },
    changes: {
      heading: '本政策的变更',
      body: '隐私政策更新将发布在此页面上，并附有新的生效日期。'
    }
  },
  
  actions: {
    copy: '复制',
    copied: '已复制！',
    copyFailed: '复制失败',
    copyPage: '复制页面',
    copyUrl: '复制 URL',
    viewMarkdown: '以 Markdown 查看',
    share: '分享',
    shareVia: '分享方式',
    email: '电子邮件',
    aiActions: 'AI 操作',
    askChatGPT: '询问 ChatGPT',
    askClaude: '询问 Claude',
    askPerplexity: '询问 Perplexity',
    askGemini: '询问 Gemini',
    permissionDenied: '剪贴板权限被拒绝',
    fallbackInstructions: '选择并复制以下文本：',
    close: '关闭'
  },
  
  a11y: {
    skipToMain: '跳至主要内容',
    logo: 'Clipcy 主页',
    menu: '导航菜单',
    languageMenu: '语言菜单',
    externalLink: '在新标签页中打开',
    loading: '加载中...'
  }
};
