/**
 * English website content catalog
 */

export default {
  meta: {
    title: 'Clipcy - Clipboard Manager for Windows',
    description: 'Find copied text, images, and files. Pin what you reuse. Paste with Alt+V.',
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
    title: 'Copy once. Use it again anytime.',
    subtitle: "Does one thing well: keeps what you copied close at hand. That's it!",
    demoPinHint: 'Press Ctrl+P to pin',
    demoUnpinHint: 'Press Ctrl+P to unpin',
    windowsSupport: 'Supports Windows 10+',
    freeOpenSource: 'Free and open source',
    ctaOr: 'or',
    ctaStore: 'Get in Microsoft Store',
    ctaDownload: 'Download now',
    ctaPrimary: 'Download for Windows',
    ctaSecondary: 'See build instructions',
    noReleaseNotice: 'No public release is currently available. See build instructions below.',
    imageAlt: 'Clipcy popup showing pinned items, search field, history list, and preview panel',
    imageCaption: 'Open with Alt+V. Search, choose, paste.'
  },

  problem: {
    title: 'Your copied text, images, and files—right at your fingertips.',
    body: 'Pull up earlier copies without switching windows or losing your place.'
  },

  features: {
    heading: 'Features',
    find: {
      title: 'Find the copy you need',
      body: 'Search through your clipboard history. Text, images, and files.',
      label: 'History & search'
    },
    pin: {
      title: 'Keep repeat pastes on top',
      body: 'Press Ctrl+P to pin items you use often.',
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
      title: '1. Copy in another app',
      body: 'Copy text, images, or files as usual.'
    },
    step2: {
      title: '2. Open Clipcy with Alt+V',
      body: 'Press Alt+V (configurable) and type to search.'
    },
    step3: {
      title: '3. Choose and paste',
      body: 'Use arrow keys to select. Press Enter to paste.'
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
    heading: 'Your history is stored on this PC.',
    storage: 'Clipboard history is saved locally in a SQLite database at %LOCALAPPDATA%\\clipcy\\clipcy.db.',
    warning: 'Clipboard history may contain sensitive content. Local storage does not imply encryption.',
    support: {
      windows: 'Windows 10 version 1809+ or Windows 11',
      languages: 'App UI: English, 简体中文, 日本語, Русский, 한국어, Tiếng Việt'
    },
    privacyLink: 'Read our privacy policy'
  },

  faq: {
    heading: 'Frequently asked questions',
    q1: {
      question: 'Which platforms are supported?',
      answer: 'Windows is implemented. macOS and Linux are planned but not yet available.'
    },
    q2: {
      question: 'How do I open Clipcy?',
      answer: 'Press Alt+V (default). You can change the hotkey in Settings.'
    },
    q3: {
      question: 'Where is clipboard history stored?',
      answer: 'Locally on your PC at %LOCALAPPDATA%\\clipcy\\clipcy.db. No cloud sync or remote storage.'
    },
    q4: {
      question: 'Are copied files archived?',
      answer: 'No. File previews read the current file path. If you move or delete a file, the preview will no longer work.'
    }
  },

  comparison: {
    heading: 'A better alternative to Win+V',
    body: 'Everything you need in a clipboard manager.',
    eyebrow: 'Compare',
    windows: 'Windows clipboard',
    rows: [
      {
        label: 'History',
        clipcy: 'Up to 1,000 items—ready whenever you need them.',
        windows: 'Up to 25 recent items; unpinned items disappear when you restart.',
        clipcyPro: true,
        windowsPro: false
      },
      {
        label: 'Content preview',
        clipcy: "Preview text, images, and files before pasting—so you know exactly what you're getting.",
        windows: 'None',
        clipcyPro: true,
        windowsPro: false
      },
      {
        label: 'Speed',
        clipcy: 'Native. Really, really fast.',
        windows: 'Good',
        clipcyPro: true,
        windowsPro: true
      }
    ],
    source: 'Microsoft clipboard documentation',
    benefits: [
      {
        title: 'No clutter',
        body: 'Does one thing well: manages what you copy. No extras cluttering things up.'
      },
      {
        title: 'Privacy first',
        body: 'Clipcy respects your privacy. If your password manager clears a password from the clipboard, Clipcy removes it from history too. Everything stays on your PC.'
      },
      {
        title: 'Keyboard first',
        body: 'Do everything with your keyboard. No need to reach for the mouse.'
      },
      {
        title: 'Open source',
        body: 'Clipcy is open source under the MIT license. Always free.'
      }
    ]
  },

  finalCta: {
    "heading": "Want to contribute a feature?",
    "body": "Share an idea, report a bug, or help build the next feature. Make Clipcy better with us.",
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
      body: 'A verified public package is not currently available. You can build Clipcy from source using the instructions below.'
    },
    requirements: {
      heading: 'Requirements',
      runtime: 'Windows 10 version 1809+ or Windows 11',
      runtimeNote: 'Windows App SDK Runtime (bundled in builds)'
    },
    buildInstructions: {
      heading: 'Build from source',
      prerequisites: {
        heading: 'Development prerequisites',
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
      heading: 'Usage',
      step1: 'Run Clipcy.exe',
      step2: 'Icon appears in system tray',
      step3: 'Press Alt+V to open clipboard history',
      step4: 'Click an item to paste',
      step5: 'Right-click tray icon for Settings, Clear History, or Exit'
    }
  },

  privacy: {
    heading: 'Privacy Policy',
    effectiveDate: 'Effective date',
    appData: {
      heading: 'Clipboard data',
      clipboard: 'Clipcy captures clipboard content (text, images, file paths) when you copy.',
      storage: 'History is stored locally in %LOCALAPPDATA%\\clipcy\\clipcy.db.',
      previews: 'File previews read the current file at its original path. Files are not archived or copied.',
      sensitive: 'Clipboard history may contain sensitive content. Clipcy does not encrypt the local database.',
      settings: 'Settings are stored locally in %LOCALAPPDATA%\\clipcy\\settings.json.'
    },
    websiteData: {
      heading: 'Website data',
      noAnalytics: 'This website does not use analytics, tracking cookies, or third-party scripts.',
      noFonts: 'No remote font requests.',
      noForms: 'No form submission or account creation.',
      language: 'Language preference is stored locally in your browser (localStorage) and is not sent to any server.',
      hosting: 'Standard web server access logs may record IP addresses and request paths. Logging policy is determined by the hosting provider.'
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
    externalLink: 'Opens in new tab',
    loading: 'Loading...'
  }
};
