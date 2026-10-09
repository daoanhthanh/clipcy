/**
 * English website content catalog
 */

export default {
  theme: {
    "label": "Toggle dark theme",
    "light": "Light",
    "dark": "Dark"
  },
  demo: {
    "search": "Search clipboard history...",
    "settings": "Settings",
    "text": "Text",
    "pin": "Ctrl+P to pin",
    "unpin": "Ctrl+P to unpin",
    "copied": "Copied just now",
    "delete": "Del to delete",
    "greeting": "Hello, clipboard.",
    "speed": "Really, really fast.",
    "checklist": "Project checklist",
    "steps": "Build → Test → Ship",
    "nativeHistory": "Native clipboard history",
    "reuse": "Copy once. Use it again.",
    "windowsClipboard": "Windows clipboard",
    "fileContent": "Demo notes\nCopy text, images, and files.",
    "itemCount": {
      "one": "{count} item",
      "other": "{count} items"
    }
  },
  meta: {
    title: 'Clipcy: Clipboard Manager for Windows | Clipboard History',
    description: 'Clipcy is a free, open-source clipboard manager for Windows. Search your clipboard history for text, images, and files, pin favorites, and paste with Alt+V.',
    ogTitle: 'Copy once. Use it again anytime.',
    ogDescription: 'Clipboard manager for Windows. Search your history, pin items, and preview before you paste.'
  },

  nav: {
    features: 'Features',
    howItWorks: 'How it works',
    download: 'Download',
    privacy: 'Privacy',
    chooseLanguage: 'Choose language',
    useBrowserLanguage: 'Use browser language'
  },

  hero: {
    eyebrow: 'Free clipboard manager for Windows',
    title: 'Copy once. Reuse anytime.',
    subtitle: 'One job. Done right. Everything you copy, within easy reach.',
    demoPinHint: 'Press Ctrl+P to pin',
    demoUnpinHint: 'Press Ctrl+P to unpin',
    windowsSupport: 'Supports Windows 10+',
    freeOpenSource: 'Free and open source',
    ctaOr: 'or',
    ctaStore: 'Get it from Microsoft Store',
    ctaDownload: 'Download now',
    ctaPrimary: 'Download for Windows',
    ctaSecondary: 'See build instructions',
    noReleaseNotice: 'No public release is currently available. See build instructions below.',
    imageAlt: 'Clipcy popup showing pinned items, search field, history list, and preview panel',
    imageCaption: 'Open with Alt+V. Search, choose, paste.'
  },

  problem: {
    title: 'Text, images, files. Everything you copy, at your fingertips.',
    body: 'Get back to something you copied earlier without switching windows or breaking your flow.'
  },

  features: {
    heading: 'Features',
    find: {
      title: 'Find what you copied',
      body: 'Search your clipboard history for text, images, and files.',
      label: 'History & search'
    },
    pin: {
      title: 'Keep your go-to items at the top',
      body: 'Press Ctrl+P to pin anything you use again and again.',
      label: 'Pinning'
    },
    preview: {
      title: 'Preview before you paste',
      body: 'See text, images, and file details before pasting.',
      label: 'Text, images, files'
    }
  },

  workflow: {
    heading: 'Copy. Find. Paste.',
    step1: {
      title: '1. Copy from any app',
      body: 'Copy text, images, or files as usual.'
    },
    step2: {
      title: '2. Open Clipcy with Alt+V',
      body: 'Press Alt+V, then type to search. You can change the shortcut in Settings.'
    },
    step3: {
      title: '3. Choose and paste',
      body: 'Choose an item with the arrow keys, then press Enter to paste.'
    },
    shortcuts: {
      open: 'Alt+V',
      navigate: 'Up/Down',
      paste: 'Enter',
      close: 'Esc',
      pin: 'Ctrl+P'
    }
  },

  trust: {
    heading: 'Your clipboard history stays on your PC.',
    storage: 'Clipboard history is saved locally in a SQLite database at %LOCALAPPDATA%\\clipcy\\clipcy.db.',
    warning: 'Clipboard history can contain sensitive information. Stored locally does not mean encrypted.',
    support: {
      windows: 'Windows 10 version 1809+ or Windows 11',
      languages: 'Available app languages: English, 简体中文, 日本語, Русский, 한국어, Tiếng Việt'
    },
    privacyLink: 'Read our privacy policy'
  },

  faq: {
    heading: 'Frequently asked questions',
    q1: {
      question: 'Which platforms are supported?',
      answer: 'Clipcy is available for Windows 10 and 11. Support for macOS and Linux is planned.'
    },
    q2: {
      question: 'How do I open Clipcy?',
      answer: 'Press Alt+V (default). You can change the hotkey in Settings.'
    },
    q3: {
      question: 'Where is clipboard history stored?',
      answer: 'Clipcy stores clipboard history locally on your PC in %LOCALAPPDATA%\\clipcy\\clipcy.db, without cloud synchronization or remote storage. The local database is not encrypted, so consider what sensitive content you keep in your history.'
    },
    q4: {
      question: 'Are copied files archived?',
      answer: 'Clipcy saves copied file paths rather than archiving the files themselves, and its preview reads the file at its current location. If you move or delete the original file, the saved path can no longer provide a working preview.'
    }
  },

  comparison: {
    heading: 'The best alternative to Win+V',
    body: 'Everything you need in a clipboard manager.',
    eyebrow: 'Compare',
    windows: 'Windows clipboard',
    rows: [
      {
        label: 'History',
        clipcy: 'Up to 1,000 items, ready when you need them.',
        windows: 'Up to 25 recent items. Unpinned items are lost when you restart.',
        clipcyPro: true,
        windowsPro: false
      },
      {
        label: 'Content preview',
        clipcy: 'Preview text, images, and files so you know what you are about to paste.',
        windows: 'None',
        clipcyPro: true,
        windowsPro: false
      },
      {
        label: 'Speed',
        clipcy: 'A native app. Really, really fast.',
        windows: 'Good',
        clipcyPro: true,
        windowsPro: true
      }
    ],
    source: 'Microsoft clipboard documentation',
    benefits: [
      {
        title: 'Just the essentials',
        body: 'One focus: managing what you copy. No extra features getting in your way.'
      },
      {
        title: 'Privacy first',
        body: 'Your clipboard stays on your machine. When your password manager clears a password from the clipboard, Clipcy removes it from history too.'
      },
      {
        title: 'Keyboard first',
        body: 'Search, select, pin, and paste without reaching for the mouse. Every action is available from the keyboard.'
      },
      {
        title: 'Open source',
        body: 'Open source, MIT licensed, and always free.'
      }
    ]
  },

  finalCta: {
    "heading": "Missing a feature?",
    "body": "Share your idea, report a bug, or help build something new. Let’s make Clipcy better together.",
    "contribute": "Contribute on GitHub"
  },

  footer: {
    version: 'Version',
    repository: 'Repository',
    privacy: 'Privacy',
    license: 'MIT License'
  },

  download: {
    heading: 'Get Clipcy for Windows',
    noRelease: {
      title: 'No public release available',
      body: 'No verified public download is available right now. You can build Clipcy from source by following the steps below.'
    },
    requirements: {
      heading: 'Requirements',
      runtime: 'Windows 10 version 1809+ or Windows 11',
      runtimeNote: 'Windows App SDK Runtime (included with the build)'
    },
    buildInstructions: {
      heading: 'Build from source',
      prerequisites: {
        heading: 'What you need to build',
        items: [
          'Windows 10 version 1809+ (build 17763)',
          'Rust 1.75+',
          '.NET 10 SDK',
          'Visual Studio 2022 with "Windows App SDK" workload'
        ]
      },
      steps: {
        heading: 'Build steps',
        step1: 'Clone the repository',
        step2: 'Build the Rust bridge',
        step3: 'Generate C# bindings (requires uniffi-bindgen-cs)',
        step4: 'Build the WinUI app',
        runNote: 'Run Clipcy.exe from the build output directory.'
      }
    },
    usage: {
      heading: 'Getting started',
      step1: 'Run Clipcy.exe',
      step2: 'Look for the Clipcy icon in the system tray',
      step3: 'Press Alt+V to open clipboard history',
      step4: 'Click an item to paste',
      step5: 'Right-click the tray icon to open Settings, clear your history, or exit'
    }
  },

  privacy: {
    heading: 'Privacy Policy',
    effectiveDate: 'Effective date',
    appData: {
      heading: 'Clipboard data',
      clipboard: 'When you copy, Clipcy records the clipboard content: text, images, and file paths.',
      storage: 'History is stored locally in %LOCALAPPDATA%\\clipcy\\clipcy.db.',
      previews: 'File previews read files at their original location. Clipcy does not archive or make copies of those files.',
      sensitive: 'Clipboard history can contain sensitive information. Clipcy does not encrypt its local database.',
      settings: 'Settings are stored locally in %LOCALAPPDATA%\\clipcy\\settings.json.'
    },
    websiteData: {
      heading: 'Website data',
      noAnalytics: 'This website does not use analytics, tracking cookies, or third-party scripts.',
      noFonts: 'The website does not load fonts from remote servers.',
      noForms: 'There are no forms to submit or accounts to create.',
      language: 'Your language preference is saved in your browser using localStorage. It is not sent to a server.',
      hosting: 'The hosting provider may record IP addresses and requested paths in standard web server access logs. Its logging policy determines how those logs are handled.'
    },
    changes: {
      heading: 'Changes to this policy',
      body: 'Privacy policy updates will be posted on this page with a new effective date.'
    }
  },

  actions: {
    copy: 'Copy',
    copied: 'Copied!',
    copyFailed: 'Copy failed',
    copyPage: 'Copy page',
    copyUrl: 'Copy URL',
    viewMarkdown: 'View as Markdown',
    share: 'Share',
    shareVia: 'Share via',
    email: 'Email',
    aiActions: 'AI actions',
    askChatGPT: 'Ask ChatGPT',
    askClaude: 'Ask Claude',
    askPerplexity: 'Ask Perplexity',
    askGemini: 'Ask Gemini',
    permissionDenied: 'Clipboard permission denied',
    fallbackInstructions: 'Select and copy the text below:',
    close: 'Close'
  },

  a11y: {
    skipToMain: 'Skip to main content',
    logo: 'Clipcy home',
    menu: 'Navigation menu',
    languageMenu: 'Language menu',
    externalLink: 'Opens in a new tab',
    loading: 'Loading...'
  }
};
