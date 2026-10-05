/**
 * Japanese website content catalog
 */

export default {
  meta: {
    title: 'Clipcy - Windows用クリップボードマネージャー',
    description: 'コピーしたテキスト、画像、ファイルを検索。よく使うものをピン留め。Alt+Vで貼り付け。',
    ogTitle: 'あなたのクリップボード、次の貼り付けの準備ができています。',
    ogDescription: 'Windows用クリップボードマネージャー。履歴を検索、アイテムをピン留め、貼り付け前にプレビュー。'
  },
  
  nav: {
    features: '機能',
    howItWorks: '使い方',
    download: 'ダウンロード',
    privacy: 'プライバシー',
    chooseLanguage: '言語を選択',
    useBrowserLanguage: 'ブラウザの言語を使用'
  },
  
  hero: {
    eyebrow: 'Windows用クリップボードマネージャー',
    title: 'あなたのクリップボード、次の貼り付けの準備ができています。',
    subtitle: '軽量で、速さを重視した設計。Windows ネイティブの UI と光速のレスポンス',
    ctaPrimary: 'Windows版をダウンロード',
    ctaSecondary: 'ビルド手順を見る',
    noReleaseNotice: '現在、公開リリースはありません。以下のビルド手順を参照してください。',
    imageAlt: 'ピン留めアイテム、検索フィールド、履歴リスト、プレビューパネルを表示するClipcyポップアップ',
    imageCaption: 'Alt+Vで開く。検索、選択、貼り付け。'
  },
  
  problem: {
    title: '便利なものをコピーしましたか？すぐに使えるように保管。',
    body: 'ウィンドウを切り替えたり、作業位置を失ったりすることなく、以前のコピーを取得します。'
  },
  
  features: {
    heading: '機能',
    find: {
      title: '必要なコピーを検索',
      body: 'クリップボード履歴を検索。テキスト、画像、ファイル。',
      label: '履歴と検索'
    },
    pin: {
      title: 'よく使う貼り付けを上部に固定',
      body: 'Ctrl+Pを押して、よく使うアイテムをピン留めします。',
      label: 'ピン留め'
    },
    preview: {
      title: '貼り付け前にプレビュー',
      body: '貼り付ける前にテキスト、画像、ファイルの詳細を確認します。',
      label: 'テキスト、画像、ファイル'
    }
  },
  
  workflow: {
    heading: 'コピー。検索。貼り付け。',
    step1: {
      title: '1. 他のアプリでコピー',
      body: 'いつものようにテキスト、画像、ファイルをコピーします。'
    },
    step2: {
      title: '2. Alt+VでClipcyを開く',
      body: 'Alt+V（設定可能）を押して、入力して検索します。'
    },
    step3: {
      title: '3. 選択して貼り付け',
      body: '矢印キーで選択。Enterを押して貼り付け。'
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
    heading: '履歴はこのPCに保存されます。',
    storage: 'クリップボード履歴は%LOCALAPPDATA%\\clipcy\\clipcy.dbのSQLiteデータベースにローカルで保存されます。',
    warning: 'クリップボード履歴には機密コンテンツが含まれる場合があります。ローカルストレージは暗号化を意味しません。',
    support: {
      windows: 'Windows 10 バージョン 1809+ または Windows 11',
      languages: 'アプリUI：English、简体中文、日本語、Русский、한국어、Tiếng Việt'
    },
    privacyLink: 'プライバシーポリシーを読む'
  },
  
  faq: {
    heading: 'よくある質問',
    q1: {
      question: 'どのプラットフォームがサポートされていますか？',
      answer: 'Windowsが実装されています。macOSとLinuxは計画されていますが、まだ利用できません。'
    },
    q2: {
      question: 'Clipcyを開くにはどうすればよいですか？',
      answer: 'Alt+V（デフォルト）を押します。設定でホットキーを変更できます。'
    },
    q3: {
      question: 'クリップボード履歴はどこに保存されますか？',
      answer: '%LOCALAPPDATA%\\clipcy\\clipcy.dbのPCにローカルで保存されます。クラウド同期やリモートストレージはありません。'
    },
    q4: {
      question: 'コピーされたファイルはアーカイブされますか？',
      answer: 'いいえ。ファイルプレビューは現在のファイルパスを読み取ります。ファイルを移動または削除すると、プレビューは機能しなくなります。'
    }
  },
  
  finalCta: {
    heading: 'クリップボード履歴を保持する準備はできましたか？',
    ctaPrimary: 'Windows版をダウンロード',
    ctaSecondary: 'ビルド手順を見る',
    planned: 'macOSとLinuxのサポートが計画されています。'
  },
  
  footer: {
    version: 'バージョン',
    repository: 'リポジトリ',
    privacy: 'プライバシー',
    license: 'MITライセンス'
  },
  
  download: {
    heading: 'Windows版Clipcyを入手',
    noRelease: {
      title: '公開リリースはありません',
      body: '検証済みの公開パッケージは現在利用できません。以下の手順を使用してソースからClipcyをビルドできます。'
    },
    requirements: {
      heading: '要件',
      runtime: 'Windows 10 バージョン 1809+ または Windows 11',
      runtimeNote: 'Windows App SDK Runtime（ビルドにバンドル）'
    },
    buildInstructions: {
      heading: 'ソースからビルド',
      prerequisites: {
        heading: '開発の前提条件',
        items: [
          'Windows 10 バージョン 1809+（ビルド 17763）',
          'Rust 1.75+',
          '.NET 10 SDK',
          'Visual Studio 2022 with "Windows App SDK" workload'
        ]
      },
      steps: {
        heading: 'ビルド手順',
        step1: 'リポジトリをクローン',
        step2: 'Rustブリッジをビルド',
        step3: 'C#バインディングを生成（uniffi-bindgen-csが必要）',
        step4: 'WinUIアプリをビルド',
        runNote: 'ビルド出力ディレクトリからClipcy.exeを実行します。'
      }
    },
    usage: {
      heading: '使用方法',
      step1: 'Clipcy.exeを実行',
      step2: 'システムトレイにアイコンが表示されます',
      step3: 'Alt+Vを押してクリップボード履歴を開きます',
      step4: 'アイテムをクリックして貼り付けます',
      step5: 'トレイアイコンを右クリックして設定、履歴のクリア、または終了'
    }
  },
  
  privacy: {
    heading: 'プライバシーポリシー',
    effectiveDate: '発効日',
    appData: {
      heading: 'クリップボードデータ',
      clipboard: 'Clipcyはコピー時にクリップボードコンテンツ（テキスト、画像、ファイルパス）をキャプチャします。',
      storage: '履歴は%LOCALAPPDATA%\\clipcy\\clipcy.dbにローカルで保存されます。',
      previews: 'ファイルプレビューは元のパスで現在のファイルを読み取ります。ファイルはアーカイブまたはコピーされません。',
      sensitive: 'クリップボード履歴には機密コンテンツが含まれる場合があります。Clipcyはローカルデータベースを暗号化しません。',
      settings: '設定は%LOCALAPPDATA%\\clipcy\\settings.jsonにローカルで保存されます。'
    },
    websiteData: {
      heading: 'ウェブサイトデータ',
      noAnalytics: 'このウェブサイトは、分析、トラッキングCookie、サードパーティスクリプトを使用しません。',
      noFonts: 'リモートフォントリクエストはありません。',
      noForms: 'フォーム送信またはアカウント作成はありません。',
      language: '言語設定はブラウザにローカルで保存され（localStorage）、サーバーに送信されません。',
      hosting: '標準のWebサーバーアクセスログは、IPアドレスとリクエストパスを記録する場合があります。ログポリシーはホスティングプロバイダーによって決定されます。'
    },
    changes: {
      heading: 'このポリシーの変更',
      body: 'プライバシーポリシーの更新は、新しい発効日とともにこのページに投稿されます。'
    }
  },
  
  actions: {
    copy: 'コピー',
    copied: 'コピーしました！',
    copyFailed: 'コピーに失敗しました',
    copyPage: 'ページをコピー',
    copyUrl: 'URLをコピー',
    viewMarkdown: 'Markdownとして表示',
    share: '共有',
    shareVia: '共有方法',
    email: 'メール',
    aiActions: 'AIアクション',
    askChatGPT: 'ChatGPTに聞く',
    askClaude: 'Claudeに聞く',
    askPerplexity: 'Perplexityに聞く',
    askGemini: 'Geminiに聞く',
    permissionDenied: 'クリップボード権限が拒否されました',
    fallbackInstructions: '以下のテキストを選択してコピーしてください：',
    close: '閉じる'
  },
  
  a11y: {
    skipToMain: 'メインコンテンツへスキップ',
    logo: 'Clipcyホーム',
    menu: 'ナビゲーションメニュー',
    languageMenu: '言語メニュー',
    externalLink: '新しいタブで開きます',
    loading: '読み込み中...'
  }
};
