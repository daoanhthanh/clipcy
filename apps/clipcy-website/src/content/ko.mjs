/**
 * Korean website content catalog
 */

export default {
  meta: {
    title: 'Clipcy - Windows용 클립보드 관리자',
    description: '복사한 텍스트, 이미지, 파일을 찾으세요. 자주 사용하는 항목을 고정하세요. Alt+V로 붙여넣기.',
    ogTitle: '클립보드, 다음 붙여넣기 준비 완료.',
    ogDescription: 'Windows용 클립보드 관리자. 기록 검색, 항목 고정, 붙여넣기 전 미리보기.'
  },
  
  nav: {
    features: '기능',
    howItWorks: '작동 방식',
    download: '다운로드',
    privacy: '개인정보 보호',
    chooseLanguage: '언어 선택',
    useBrowserLanguage: '브라우저 언어 사용'
  },
  
  hero: {
    eyebrow: 'Windows용 무료 클립보드 관리자',
    title: '클립보드, 다음 붙여넣기 준비 완료.',
    subtitle: '가볍고 빠른 동작을 위해 설계된 Windows 네이티브 인터페이스와 빛처럼 빠른 응답',
    windowsSupport: 'Windows 10 이상 지원',
    freeOpenSource: '무료 오픈 소스',
    ctaPrimary: 'Windows용 다운로드',
    ctaSecondary: '빌드 지침 보기',
    noReleaseNotice: '현재 공개 릴리스를 사용할 수 없습니다. 아래 빌드 지침을 참조하세요.',
    imageAlt: '고정된 항목, 검색 필드, 기록 목록, 미리보기 패널을 표시하는 Clipcy 팝업',
    imageCaption: 'Alt+V로 열기. 검색, 선택, 붙여넣기.'
  },
  
  problem: {
    title: '유용한 것을 복사했나요? 가까이 두세요.',
    body: '창을 전환하거나 작업 위치를 잃지 않고 이전 복사본을 검색합니다.'
  },
  
  features: {
    heading: '기능',
    find: {
      title: '필요한 복사본 찾기',
      body: '클립보드 기록을 검색합니다. 텍스트, 이미지, 파일.',
      label: '기록 및 검색'
    },
    pin: {
      title: '자주 사용하는 붙여넣기를 상단에 고정',
      body: 'Ctrl+P를 눌러 자주 사용하는 항목을 고정합니다.',
      label: '고정'
    },
    preview: {
      title: '붙여넣기 전 미리보기',
      body: '붙여넣기 전에 텍스트, 이미지, 파일 세부 정보를 확인합니다.',
      label: '텍스트, 이미지, 파일'
    }
  },
  
  workflow: {
    heading: '복사. 찾기. 붙여넣기.',
    step1: {
      title: '1. 다른 앱에서 복사',
      body: '평소처럼 텍스트, 이미지 또는 파일을 복사합니다.'
    },
    step2: {
      title: '2. Alt+V로 Clipcy 열기',
      body: 'Alt+V(구성 가능)를 누르고 입력하여 검색합니다.'
    },
    step3: {
      title: '3. 선택 및 붙여넣기',
      body: '화살표 키를 사용하여 선택합니다. Enter를 눌러 붙여넣습니다.'
    },
    shortcuts: {
      open: 'Alt+V',
      navigate: '위/아래',
      paste: 'Enter',
      close: 'Esc',
      pin: 'Ctrl+P'
    }
  },
  
  trust: {
    heading: '기록은 이 PC에 저장됩니다.',
    storage: '클립보드 기록은 %LOCALAPPDATA%\\clipcy\\clipcy.db의 SQLite 데이터베이스에 로컬로 저장됩니다.',
    warning: '클립보드 기록에는 민감한 콘텐츠가 포함될 수 있습니다. 로컬 저장소는 암호화를 의미하지 않습니다.',
    support: {
      windows: 'Windows 10 버전 1809+ 또는 Windows 11',
      languages: '앱 UI: English, 简体中文, 日本語, Русский, 한국어, Tiếng Việt'
    },
    privacyLink: '개인정보 보호정책 읽기'
  },
  
  faq: {
    heading: '자주 묻는 질문',
    q1: {
      question: '어떤 플랫폼이 지원되나요?',
      answer: 'Windows가 구현되었습니다. macOS 및 Linux는 계획되어 있지만 아직 사용할 수 없습니다.'
    },
    q2: {
      question: 'Clipcy를 어떻게 여나요?',
      answer: 'Alt+V(기본값)를 누릅니다. 설정에서 단축키를 변경할 수 있습니다.'
    },
    q3: {
      question: '클립보드 기록은 어디에 저장되나요?',
      answer: 'PC의 %LOCALAPPDATA%\\clipcy\\clipcy.db에 로컬로 저장됩니다. 클라우드 동기화 또는 원격 저장소가 없습니다.'
    },
    q4: {
      question: '복사된 파일이 보관되나요?',
      answer: '아니요. 파일 미리보기는 현재 파일 경로를 읽습니다. 파일을 이동하거나 삭제하면 미리보기가 더 이상 작동하지 않습니다.'
    }
  },
  
  comparison: {
  "heading": "완벽한 대안: Win+V",
  "body": "Windows 클립보드 기록은 기본 기능을 제공합니다. Clipcy는 검색과 전용 미리보기를 더하고 데이터를 PC에 저장합니다.",
  "eyebrow": "비교",
  "windows": "Windows 클립보드",
  "rows": [
    {
      "label": "기록",
      "clipcy": "최대 1,000개. 필요할 때 언제든 사용할 수 있습니다.",
      "windows": "최근 항목 최대 25개; 고정하지 않은 항목은 재시작 시 삭제됩니다.",
      "clipcyPro": true,
      "windowsPro": false
    },
    {
      "label": "전용 미리보기",
      "clipcy": "텍스트, 이미지, 파일 전용 미리보기",
      "windows": "없음",
      "clipcyPro": true,
      "windowsPro": false
    },
    {
      "label": "속도",
      "clipcy": "Windows 기본 Win+V만큼 빠르게",
      "windows": "빠름",
      "clipcyPro": true,
      "windowsPro": true
    }
  ],
  "source": "Microsoft 클립보드 문서",
  "benefits": [
    {
      "title": "클립보드에 집중",
      "body": "추가 단계 없이 유용한 복사 내용을 가까이 두세요."
    },
    {
      "title": "가볍고 빠르게",
      "body": "Rust 기반 코어로 기록에 편리하게 접근하세요."
    },
    {
      "title": "키보드 중심",
      "body": "Alt+V로 열고, 입력하여 검색하고, Enter로 붙여넣으세요."
    },
    {
      "title": "Windows 네이티브 UI",
      "body": "익숙한 WinUI 컨트롤과 아크릴 효과가 데스크톱에 어울립니다."
    }
  ]
},

  finalCta: {
  "heading": "기능 개발에 참여하고 싶으신가요?",
  "body": "아이디어를 공유하고, 버그를 신고하거나 새 기능을 함께 개발하세요. Clipcy를 함께 개선해요.",
  "contribute": "GitHub에서 기여하기"
},
  
  footer: {
    version: '버전',
    repository: '저장소',
    privacy: '개인정보 보호',
    license: 'MIT 라이선스'
  },
  
  download: {
    heading: 'Windows용 Clipcy 받기',
    noRelease: {
      title: '공개 릴리스 없음',
      body: '검증된 공개 패키지는 현재 사용할 수 없습니다. 아래 지침을 사용하여 소스에서 Clipcy를 빌드할 수 있습니다.'
    },
    requirements: {
      heading: '요구 사항',
      runtime: 'Windows 10 버전 1809+ 또는 Windows 11',
      runtimeNote: 'Windows App SDK Runtime(빌드에 번들)'
    },
    buildInstructions: {
      heading: '소스에서 빌드',
      prerequisites: {
        heading: '개발 전제 조건',
        items: [
          'Windows 10 버전 1809+(빌드 17763)',
          'Rust 1.75+',
          '.NET 10 SDK',
          'Visual Studio 2022 with "Windows App SDK" workload'
        ]
      },
      steps: {
        heading: '빌드 단계',
        step1: '저장소 복제',
        step2: 'Rust 브리지 빌드',
        step3: 'C# 바인딩 생성(uniffi-bindgen-cs 필요)',
        step4: 'WinUI 앱 빌드',
        runNote: '빌드 출력 디렉터리에서 Clipcy.exe를 실행합니다.'
      }
    },
    usage: {
      heading: '사용법',
      step1: 'Clipcy.exe 실행',
      step2: '시스템 트레이에 아이콘이 나타남',
      step3: 'Alt+V를 눌러 클립보드 기록 열기',
      step4: '항목을 클릭하여 붙여넣기',
      step5: '트레이 아이콘을 마우스 오른쪽 버튼으로 클릭하여 설정, 기록 지우기 또는 종료'
    }
  },
  
  privacy: {
    heading: '개인정보 보호정책',
    effectiveDate: '시행일',
    appData: {
      heading: '클립보드 데이터',
      clipboard: 'Clipcy는 복사할 때 클립보드 콘텐츠(텍스트, 이미지, 파일 경로)를 캡처합니다.',
      storage: '기록은 %LOCALAPPDATA%\\clipcy\\clipcy.db에 로컬로 저장됩니다.',
      previews: '파일 미리보기는 원래 경로에서 현재 파일을 읽습니다. 파일은 보관되거나 복사되지 않습니다.',
      sensitive: '클립보드 기록에는 민감한 콘텐츠가 포함될 수 있습니다. Clipcy는 로컬 데이터베이스를 암호화하지 않습니다.',
      settings: '설정은 %LOCALAPPDATA%\\clipcy\\settings.json에 로컬로 저장됩니다.'
    },
    websiteData: {
      heading: '웹사이트 데이터',
      noAnalytics: '이 웹사이트는 분석, 추적 쿠키 또는 타사 스크립트를 사용하지 않습니다.',
      noFonts: '원격 글꼴 요청이 없습니다.',
      noForms: '양식 제출 또는 계정 생성이 없습니다.',
      language: '언어 기본 설정은 브라우저에 로컬로 저장되며(localStorage) 서버로 전송되지 않습니다.',
      hosting: '표준 웹 서버 액세스 로그는 IP 주소 및 요청 경로를 기록할 수 있습니다. 로깅 정책은 호스팅 제공업체에서 결정합니다.'
    },
    changes: {
      heading: '이 정책의 변경 사항',
      body: '개인정보 보호정책 업데이트는 새로운 시행일과 함께 이 페이지에 게시됩니다.'
    }
  },
  
  actions: {
    copy: '복사',
    copied: '복사됨!',
    copyFailed: '복사 실패',
    copyPage: '페이지 복사',
    copyUrl: 'URL 복사',
    viewMarkdown: 'Markdown으로 보기',
    share: '공유',
    shareVia: '공유 방법',
    email: '이메일',
    aiActions: 'AI 작업',
    askChatGPT: 'ChatGPT에게 묻기',
    askClaude: 'Claude에게 묻기',
    askPerplexity: 'Perplexity에게 묻기',
    askGemini: 'Gemini에게 묻기',
    permissionDenied: '클립보드 권한 거부됨',
    fallbackInstructions: '아래 텍스트를 선택하고 복사하세요:',
    close: '닫기'
  },
  
  a11y: {
    skipToMain: '주요 콘텐츠로 건너뛰기',
    logo: 'Clipcy 홈',
    menu: '탐색 메뉴',
    languageMenu: '언어 메뉴',
    externalLink: '새 탭에서 열림',
    loading: '로딩 중...'
  }
};
