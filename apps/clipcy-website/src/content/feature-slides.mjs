const copy = {
  en: [
    ['Search clipboard history for text, images, and files', 'Open Clipcy with Alt+V, then start typing to find an earlier copy. Your clipboard history narrows down as you type, so you can reuse a saved snippet or file path without copying it again from the original application.'],
    ['Use shortcuts to select faster', 'Move through your clipboard history with the arrow keys. Preview the selected text, image, or file before you paste it, then press Enter to insert it into the application you were using. Press Escape to close the popup.'],
    ['Pin clipboard items with Ctrl+P', 'Keep frequently used copies at the top of your clipboard history. Select an item and press Ctrl+P to pin it, making reusable text and file paths easier to find. Press Ctrl+P again when you no longer need that item pinned.'],
    ['Select an item with Ctrl + its number', 'Jump straight to an item by its position. Ctrl+3 selects the third item.'],
    ['Paste as plain text with Shift+Enter', 'Paste the selected text item without formatting by pressing Shift+Enter. Keep the words and leave the styles behind when moving text between documents, emails, or other applications. Use Enter for the regular paste action.']
  ],
  vi: [
    ['Gõ để tìm kiếm', 'Mở Clipcy bằng Alt+V rồi bắt đầu gõ. Lịch sử được lọc ngay khi bạn nhập.'],
    ['Dùng phím tắt để chọn nhanh hơn', 'Dùng phím mũi tên để di chuyển trong lịch sử. Nhấn Enter để dán mục đã chọn.'],
    ['Ghim mục bằng Ctrl+P', 'Giữ các nội dung thường dùng ở đầu danh sách. Chọn một mục và nhấn Ctrl+P để ghim.'],
    ['Chọn mục bằng Ctrl + số thứ tự', 'Chọn thẳng một mục theo vị trí. Ctrl+3 chọn mục thứ ba.'],
    ['Dán văn bản thuần bằng Shift+Enter', 'Dán mục đã chọn mà không giữ định dạng. Chỉ giữ nội dung văn bản.']
  ],
  'zh-CN': [
    ['直接输入即可搜索', '按 Alt+V 打开 Clipcy，然后输入。历史记录会随输入即时筛选。'],
    ['使用快捷键快速选择', '使用方向键浏览历史记录。按 Enter 粘贴选中的项目。'],
    ['按 Ctrl+P 固定项目', '将常用内容保留在顶部。选中项目并按 Ctrl+P 即可固定。'],
    ['按 Ctrl + 序号选择项目', '按位置直接选择项目。Ctrl+3 选择第三项。'],
    ['按 Shift+Enter 粘贴纯文本', '粘贴选中的项目，不保留格式。只留下文字。']
  ],
  ja: [
    ['入力するだけで検索', 'Alt+V で Clipcy を開いて入力すると、履歴がすぐに絞り込まれます。'],
    ['ショートカットで素早く選択', '矢印キーで履歴を移動。Enter で選択した項目を貼り付けます。'],
    ['Ctrl+P で項目をピン留め', 'よく使う内容を先頭に。項目を選び、Ctrl+P でピン留めします。'],
    ['Ctrl + 番号で項目を選択', '位置で直接選択。Ctrl+3 で3番目の項目を選びます。'],
    ['Shift+Enter でプレーンテキストを貼り付け', '書式を付けずに選択した項目を貼り付けます。文字だけを残します。']
  ],
  ko: [
    ['입력해서 검색하기', 'Alt+V로 Clipcy를 열고 입력하세요. 입력할 때마다 기록이 필터링됩니다.'],
    ['단축키로 빠르게 선택하기', '방향키로 기록을 탐색하세요. Enter로 선택한 항목을 붙여넣습니다.'],
    ['Ctrl+P로 항목 고정하기', '자주 쓰는 내용을 맨 위에 두세요. 항목을 선택하고 Ctrl+P를 눌러 고정합니다.'],
    ['Ctrl + 번호로 항목 선택하기', '위치 번호로 바로 선택하세요. Ctrl+3은 세 번째 항목을 선택합니다.'],
    ['Shift+Enter로 일반 텍스트 붙여넣기', '선택한 항목을 서식 없이 붙여넣으세요. 텍스트만 남깁니다.']
  ],
  ru: [
    ['Начните вводить для поиска', 'Откройте Clipcy с помощью Alt+V и начните вводить. История фильтруется по мере ввода.'],
    ['Выбирайте быстрее с клавиатуры', 'Перемещайтесь по истории стрелками. Нажмите Enter, чтобы вставить выбранный элемент.'],
    ['Закрепляйте элементы с Ctrl+P', 'Держите частые копии вверху. Выберите элемент и нажмите Ctrl+P, чтобы закрепить его.'],
    ['Выбирайте элемент с Ctrl + номер', 'Выбирайте сразу по позиции. Ctrl+3 выбирает третий элемент.'],
    ['Вставляйте обычный текст с Shift+Enter', 'Вставьте выбранный элемент без форматирования. Сохраните только текст.']
  ]
};

const keys = [['Alt', 'V'], ['↑', '↓', 'Enter'], ['Ctrl', 'P'], ['Ctrl', '3'], ['Shift', 'Enter']];
export function featureSlides(locale) {
  return (copy[locale] || copy.en).map(([title, body], index) => ({ title, body, keys: keys[index] }));
}

export function wallpaperAlt(locale) {
  return ({
    en: 'Windows 11 wallpaper with a folded blue ribbon',
    vi: 'Hình nền Windows 11 với dải lụa xanh uốn nếp',
    'zh-CN': 'Windows 11 壁纸，展示折叠的蓝色丝带',
    ja: '折り重なる青いリボンを描いた Windows 11 の壁紙',
    ko: '접힌 파란 리본 모양의 Windows 11 배경 화면',
    ru: 'Обои Windows 11 со складками синей ленты'
  })[locale] || 'Windows 11 wallpaper with a folded blue ribbon';
}
