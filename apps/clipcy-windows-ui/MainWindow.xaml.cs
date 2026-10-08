using Clipcy.Services;
using Clipcy.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Clipcy;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly IWindowService _windowService;
    private readonly IPreviewService _previewService;
    private enum SelectionOwner
    {
        None,
        Pointer,
        Keyboard,
    }

    private bool _syncingSelection;
    private bool _userSelecting;
    private bool _pinnedChromeQueued;
    private bool _returningFocus;
    private bool _hasPointer;
    private SelectionOwner _owner;
    private ListView? _activeList;
    private int _activeIndex = -1;
    private ClipboardItemViewModel? _focusedItem;
    private Windows.Foundation.Point _lastPointer;
    private string? _pinnedShortcutInput;
    private bool _quickPastePending;
    private int _popupGeneration;
    private MenuFlyout? _itemContextMenu;
    private ClipboardItemViewModel? _requestedPreviewItem;
    private bool _windowActive;

    public MainWindow()
    {
        this.InitializeComponent();
        RootGrid.AddHandler(UIElement.CharacterReceivedEvent,
            new Windows.Foundation.TypedEventHandler<UIElement, CharacterReceivedRoutedEventArgs>(
                RootGrid_CharacterReceived), handledEventsToo: true);

        // Manual Dependency Injection
        _windowService = new WindowService(this);
        _windowService.Initialize();

        _previewService = new PreviewService(this, RootGrid, _windowService);
        _previewService.ManualPreviewDismissed += OnManualPreviewDismissed;

        ViewModel = new MainViewModel();
        ((FrameworkElement)this.Content).DataContext = this;
        DisableListFocus(PinnedListView);
        DisableListFocus(HistoryListView);
        ViewModel.PinnedItems.CollectionChanged += (_, _) => QueuePinnedChrome();


        // Subscribe to theme changes and apply to Content
        ThemeManager.ThemeChanged += OnThemeChanged;
        ApplyTheme(ThemeManager.CurrentTheme);

        this.Activated += MainWindow_Activated;
        this.Closed += MainWindow_Closed;

        // Subscribe to Rust bridge events
        if (App.Bridge != null)
        {
            // Refresh when clipboard changes
            App.Bridge.OnHistoryUpdated += RefreshHistory;
            
            // Load initial data when Rust engine is ready
            App.Bridge.OnReady += RefreshHistory;
        }

    }

    private static void DisableListFocus(ListView list)
    {
        list.IsTabStop = false;
        list.AllowFocusOnInteraction = false;

    }

    public WindowService GetWindowService() => (WindowService)_windowService;

    private void RefreshHistory() => ViewModel.RefreshHistory();

    private void OnThemeChanged(AppTheme theme)
    {
        if (this.Content is FrameworkElement content)
        {
            content.DispatcherQueue.TryEnqueue(() => ApplyTheme(theme));
        }
    }

    private void ApplyTheme(AppTheme theme)
    {
        if (this.Content is FrameworkElement content)
        {
            var newTheme = ThemeManager.GetElementTheme(theme);
            if (content.RequestedTheme != newTheme)
            {
                content.RequestedTheme = newTheme;
            }
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        if (App.Bridge != null)
        {
            App.Bridge.OnHistoryUpdated -= RefreshHistory;
            App.Bridge.OnReady -= RefreshHistory;
        }
        ViewModel.Dispose();
        _previewService.ManualPreviewDismissed -= OnManualPreviewDismissed;
        _previewService.Dispose();
    }

    private void OnManualPreviewDismissed()
    {
        if (!_windowActive)
            HidePopup();
    }

    private void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (_previewService.IsManualPreviewOpen)
            return;
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (_windowService.IsVisible)
            {
                this.HidePopup();
            }
        }
        else if (
            args.WindowActivationState == WindowActivationState.CodeActivated
            || args.WindowActivationState == WindowActivationState.PointerActivated
        )
        {
            SearchBox.Text = string.Empty;

            // List is already populated in background - do nothing

            _owner = SelectionOwner.None;
            _focusedItem = null;
            _activeList = null;
            _activeIndex = -1;
            _hasPointer = false;
            _syncingSelection = true;
            PinnedListView.SelectedIndex = -1;
            HistoryListView.SelectedIndex = -1;
            _syncingSelection = false;
            RootGrid.Focus(FocusState.Programmatic);

            ResetListScroll(PinnedListView);
            ResetListScroll(HistoryListView);

            _windowService.EnsureBackdropActive();
        }
    }

    private static void ResetListScroll(ListView list)
    {
        list.UpdateLayout();
        FindScrollViewer(list)?.ChangeView(0, 0, null, disableAnimation: true);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.Search(SearchBox.Text);
    }

    private void HistoryListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ClipboardItemViewModel item)
        {
            PasteSelectedItem(item);
        }
    }

    private void PasteSelectedItem(ClipboardItemViewModel item)
    {
        HidePopup();

        try
        {
            App.Bridge?.PasteItem(item.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Paste error: {ex.Message}");
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow.ShowOrActivate();
        this.HidePopup();
    }

    private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_itemContextMenu != null)
            return;

        if (HandleQuickPaste(e))
            return;

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            this.HidePopup();
            return;
        }

        if (IsKeyDown(Windows.System.VirtualKey.Control) && e.Key == Windows.System.VirtualKey.P)
        {
            if (GetPinTarget() is ClipboardItemViewModel item)
            {
                e.Handled = true;
                await TogglePinAsync(item);
            }
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Delete)
        {
            if (SelectedClipboardItem() is ClipboardItemViewModel item)
            {
                e.Handled = true;
                try
                {
                    App.Bridge?.DeleteItem(item.Id);
                    ViewModel.RefreshHistory();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting item: {ex.Message}");
                }
            }
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (SelectedClipboardItem() is ClipboardItemViewModel item)
            {
                e.Handled = true;
                PasteSelectedItem(item);
                return;
            }
        }

        if (e.Key == Windows.System.VirtualKey.Down)
        {
            e.Handled = true;
            MoveSelection(1);
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            e.Handled = true;
            MoveSelection(-1);
        }
        else if (CanStartTypingSearch())
        {
            int key = (int)e.Key;
            // Letters, digits, numpad operators, space, OEM punctuation, and IME ProcessKey.
            // Focus before composition starts; Windows performs keyboard-layout translation.
            if (key is >= 0x30 and <= 0x5A or >= 0x60 and <= 0x6F
                or 0x20 or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2 or 0xE5)
            {
                if (SearchBox.Focus(FocusState.Keyboard))
                    SearchBox.Select(SearchBox.Text.Length, 0);
            }
        }
    }

    private void RootGrid_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (!CanStartTypingSearch() || char.IsControl(e.Character))
            return;

        if (!SearchBox.Focus(FocusState.Keyboard))
            return;

        SearchBox.Select(SearchBox.Text.Length, 0);
        SearchBox.SelectedText = e.Character.ToString();
        e.Handled = true;
    }

    private bool CanStartTypingSearch() =>
        _windowService.IsVisible && _itemContextMenu == null
        && !_quickPastePending && _pinnedShortcutInput == null
        && SearchBox.FocusState == FocusState.Unfocused
        && !IsKeyDown(Windows.System.VirtualKey.Control)
        && !IsKeyDown(Windows.System.VirtualKey.Menu)
        && !IsKeyDown(Windows.System.VirtualKey.LeftWindows)
        && !IsKeyDown(Windows.System.VirtualKey.RightWindows);

    private ClipboardItemViewModel? GetPinTarget() => _focusedItem;

    private bool HandleQuickPaste(KeyRoutedEventArgs e)
    {
        if (_quickPastePending)
        {
            e.Handled = true;
            if (e.Key == Windows.System.VirtualKey.Escape)
                HidePopup();
            return true;
        }
        var key = e.Key;
        int digit = key >= Windows.System.VirtualKey.Number0 && key <= Windows.System.VirtualKey.Number9
            ? (int)key - (int)Windows.System.VirtualKey.Number0
            : key >= Windows.System.VirtualKey.NumberPad0 && key <= Windows.System.VirtualKey.NumberPad9
                ? (int)key - (int)Windows.System.VirtualKey.NumberPad0
                : -1;
        bool control = IsKeyDown(Windows.System.VirtualKey.Control);
        bool alt = IsKeyDown(Windows.System.VirtualKey.Menu);
        bool shift = IsKeyDown(Windows.System.VirtualKey.Shift);
        char? character = digit >= 0 ? (char)('0' + digit)
            : key >= Windows.System.VirtualKey.A && key <= Windows.System.VirtualKey.Z
                ? (char)('A' + (int)key - (int)Windows.System.VirtualKey.A) : null;

        if (_pinnedShortcutInput != null)
        {
            e.Handled = true;
            if (key == Windows.System.VirtualKey.Escape)
            {
                _pinnedShortcutInput = null;
                ViewModel.ShowShortcutInput(null);
            }
            else if (key == Windows.System.VirtualKey.Back)
            {
                if (_pinnedShortcutInput.Length > 0)
                    _pinnedShortcutInput = _pinnedShortcutInput[..^1];
                ViewModel.ShowShortcutInput(_pinnedShortcutInput);
            }
            else if (key == Windows.System.VirtualKey.Enter)
            {
                if (!e.KeyStatus.WasKeyDown)
                    AppendPinnedShortcutCharacter('\n');
            }
            else if (character != null && !shift && !e.KeyStatus.WasKeyDown)
            {
                AppendPinnedShortcutCharacter(character.Value);
            }
            return true;
        }

        if (!control || shift || (alt ? character == null : digit < 0))
            return false;

        e.Handled = true;
        if (e.KeyStatus.WasKeyDown)
            return true;
        if (alt)
        {
            _pinnedShortcutInput = string.Empty;
            AppendPinnedShortcutCharacter(character!.Value);
        }
        else
        {
            int index = digit == 0 ? 9 : digit - 1;
            if (index < ViewModel.RegularItems.Count)
                QuickPasteItem(ViewModel.RegularItems[index]);
        }
        return true;
    }

    private void AppendPinnedShortcutCharacter(char character)
    {
        _pinnedShortcutInput += character;
        if (PinnedShortcutStore.TryReadSequence(_pinnedShortcutInput, out long code))
        {
            _pinnedShortcutInput = null;
            ViewModel.ShowShortcutInput(null);
            if (ViewModel.PinnedItems.FirstOrDefault(item => item.PinnedShortcutCode == code) is { } item)
                QuickPasteItem(item);
        }
        else if (!ViewModel.PinnedItems.Any(item => item.PinnedShortcutCode is long assigned
            && PinnedShortcutStore.Sequence(assigned).StartsWith(_pinnedShortcutInput, StringComparison.Ordinal)))
        {
            _pinnedShortcutInput = null;
            ViewModel.ShowShortcutInput(null);
        }
        else
        {
            ViewModel.ShowShortcutInput(string.Join(
                ", ", _pinnedShortcutInput.Select(value => value.ToString())));
        }
    }

    private async void QuickPasteItem(ClipboardItemViewModel item)
    {
        _quickPastePending = true;
        int generation = _popupGeneration;
        try
        {
            while (generation == _popupGeneration && _windowService.IsVisible
                && (IsKeyDown(Windows.System.VirtualKey.Control)
                || IsKeyDown(Windows.System.VirtualKey.Menu) || IsKeyDown(Windows.System.VirtualKey.Shift)))
                await Task.Delay(15);
            if (generation == _popupGeneration && _windowService.IsVisible && ViewModel.Items.Contains(item))
                PasteSelectedItem(item);
        }
        finally
        {
            _quickPastePending = false;
        }
    }

    private async Task TogglePinAsync(ClipboardItemViewModel item)
    {
        if (!ViewModel.TogglePinCommand.CanExecute(item))
            return;

        _previewService.Hide();
        await ViewModel.TogglePinCommand.ExecuteAsync(item);

        // Follow the same item after it moves between the pinned and regular regions.
        if (_windowService.IsVisible)
            RevealItem(item);
    }

    private ClipboardItemViewModel? SelectedClipboardItem() => _focusedItem;

    private ListView? SelectedList() =>
        _activeList != null && _activeIndex >= 0 && _activeList.SelectedIndex == _activeIndex
            ? _activeList
            : null;

    private bool TrySelectFirst()
    {
        if (ViewModel.PinnedItems.Count > 0)
            return SetFocusedItem(PinnedListView, 0, SelectionOwner.Keyboard);

        if (ViewModel.RegularItems.Count > 0)
            return SetFocusedItem(HistoryListView, 0, SelectionOwner.Keyboard);

        return false;
    }

    private void MoveSelection(int delta)
    {
        EnforceExclusiveSelection();
        var list = SelectedList();
        if (list == null)
        {
            if (delta > 0)
                TrySelectFirst();
            return;
        }

        int index = list.SelectedIndex + delta;
        if (index >= 0 && index < list.Items.Count)
        {
            SetFocusedItem(list, index, SelectionOwner.Keyboard);
            return;
        }

        if (delta > 0 && list == PinnedListView && HistoryListView.Items.Count > 0)
        {
            SetFocusedItem(HistoryListView, 0, SelectionOwner.Keyboard);
            return;
        }

        if (delta < 0 && list == HistoryListView && PinnedListView.Items.Count > 0)
        {
            SetFocusedItem(PinnedListView, PinnedListView.Items.Count - 1, SelectionOwner.Keyboard);
            return;
        }

        if (delta < 0)
            ClearFocusedItem();
    }

    private bool SetFocusedItem(ListView list, int index, SelectionOwner owner)
    {
        if (index < 0 || index >= list.Items.Count || list.Items[index] is not ClipboardItemViewModel item)
            return false;

        _owner = owner;
        _activeList = list;
        _activeIndex = index;
        _focusedItem = item;

        _syncingSelection = true;
        _userSelecting = true;
        if (!ReferenceEquals(list, PinnedListView))
            PinnedListView.SelectedIndex = -1;
        if (!ReferenceEquals(list, HistoryListView))
            HistoryListView.SelectedIndex = -1;
        list.SelectedIndex = index;
        _userSelecting = false;
        _syncingSelection = false;
        EnforceExclusiveSelection();

        list.ScrollIntoView(item);
        list.UpdateLayout();
        if (list.ContainerFromItem(item) is ListViewItem container)
        {
            _returningFocus = true;
            container.Focus(FocusState.Programmatic);
            _returningFocus = false;
            if (_itemContextMenu == null)
                _previewService.ScheduleShow(MapToPreviewData(item), container);
        }

        return true;
    }

    private void ClearFocusedItem()
    {
        _owner = SelectionOwner.None;
        _focusedItem = null;
        _activeList = null;
        _activeIndex = -1;
        _syncingSelection = true;
        PinnedListView.SelectedIndex = -1;
        HistoryListView.SelectedIndex = -1;
        _syncingSelection = false;
        RootGrid.Focus(FocusState.Programmatic);
        _previewService.Hide();
    }

    private void RevealItem(ClipboardItemViewModel item)
    {
        ListView? list = null;
        if (ViewModel.PinnedItems.Contains(item))
            list = PinnedListView;
        else if (ViewModel.RegularItems.Contains(item))
            list = HistoryListView;

        if (list == null)
            return;

        int index = list.Items.IndexOf(item);
        if (index < 0)
            return;

        SetFocusedItem(list, index, _owner == SelectionOwner.None ? SelectionOwner.Keyboard : _owner);
    }


    private ListView? ListOwning(ListViewItem item)
    {
        return ItemsControl.ItemsControlFromItemContainer(item) as ListView;
    }

    private void QueuePinnedChrome()
    {
        if (_pinnedChromeQueued)
            return;

        _pinnedChromeQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _pinnedChromeQueued = false;
            ApplyPinnedChrome();
        });
    }

    private void ApplyPinnedChrome()
    {
        int count = ViewModel.PinnedItems.Count;
        if (count == 0 && PinnedListView.SelectedIndex >= 0)
            PinnedListView.SelectedIndex = -1;

        int visible = Math.Min(count, WindowService.PinnedMaxVisibleRows);
        int pinnedHeight = visible * WindowService.PinnedRowHeightDip;

        PinnedListView.Height = count == 0
            ? double.NaN
            : pinnedHeight;
        ScrollViewer.SetVerticalScrollBarVisibility(
            PinnedListView,
            count > WindowService.PinnedMaxVisibleRows
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled);
        _windowService.SetExtraHeightDip(count == 0
            ? 0
            : WindowService.PinnedSectionChromeDip + pinnedHeight);
    }

    public void ShowAtCursor()
    {
        ApplyPinnedChrome();
        _windowService.ShowAtCursor();
    }

    public void HidePopup()
    {
        _requestedPreviewItem = null;
        _itemContextMenu?.Hide();
        _itemContextMenu = null;
        _popupGeneration++;
        _pinnedShortcutInput = null;
        ViewModel.ShowShortcutInput(null);
        _previewService.Hide();
        _windowService.Hide();
    }

    public bool IsVisible => _windowService.IsVisible;

    private void HistoryListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is ListViewItem item)
        {
            item.IsTabStop = false;
            item.AllowFocusOnInteraction = false;
            item.UseSystemFocusVisuals = false;
            item.PointerEntered -= ListViewItem_PointerEntered;
            item.PointerMoved -= ListViewItem_PointerMoved;
            item.PointerExited -= ListViewItem_PointerExited;
            item.GotFocus -= ListViewItem_GotFocus;
            item.RemoveHandler(UIElement.ContextRequestedEvent,
                new Windows.Foundation.TypedEventHandler<UIElement, ContextRequestedEventArgs>(
                    ClipboardItem_ContextRequested));

            item.PointerEntered += ListViewItem_PointerEntered;
            item.PointerMoved += ListViewItem_PointerMoved;
            item.PointerExited += ListViewItem_PointerExited;
            item.GotFocus += ListViewItem_GotFocus;
            // Include row padding and context requests handled by ListViewItem itself.
            item.AddHandler(UIElement.ContextRequestedEvent,
                new Windows.Foundation.TypedEventHandler<UIElement, ContextRequestedEventArgs>(
                    ClipboardItem_ContextRequested), handledEventsToo: true);
        }

        if (args.Item is ClipboardItemViewModel vm)
        {
            vm.EnsureThumbnailLoaded(this.DispatcherQueue);
        }
    }

    private void ListViewItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListViewItem item)
            TryPointerTakeover(item, e);
    }

    private void ListViewItem_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListViewItem item)
            TryPointerTakeover(item, e);
    }

    private void ListViewItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_owner == SelectionOwner.Pointer
            && sender is ListViewItem item
            && ReferenceEquals(item.Content, _focusedItem))
        {
            _previewService.ScheduleHide();
        }
    }

    private void TryPointerTakeover(ListViewItem item, PointerRoutedEventArgs e)
    {
        if (_itemContextMenu != null)
            return;

        if (!PointerMovedSince(e) && _owner == SelectionOwner.Keyboard)
            return;

        var list = ListOwning(item);
        if (list == null)
            return;

        int index = list.IndexFromContainer(item);
        if (index < 0)
            return;

        if (_owner == SelectionOwner.Pointer && ReferenceEquals(_focusedItem, item.Content))
        {
            _previewService.ScheduleShow(MapToPreviewData((ClipboardItemViewModel)item.Content), item);
            return;
        }

        SetFocusedItem(list, index, SelectionOwner.Pointer);
    }

    private bool PointerMovedSince(PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(RootGrid).Position;
        bool moved = !_hasPointer
            || Math.Abs(pos.X - _lastPointer.X) > 1
            || Math.Abs(pos.Y - _lastPointer.Y) > 1;
        _lastPointer = pos;
        _hasPointer = true;
        return moved;
    }

    private void ListViewItem_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_returningFocus || sender is not ListViewItem item)
            return;
        if (ReferenceEquals(item.Content, _focusedItem))
            return;

        _returningFocus = true;
        if (_activeList?.ContainerFromItem(_focusedItem) is ListViewItem owned)
            owned.Focus(FocusState.Programmatic);
        else
            RootGrid.Focus(FocusState.Programmatic);
        _returningFocus = false;
        EnforceExclusiveSelection();
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _previewService.Hide();
    }

    private void EnforceExclusiveSelection()
    {
        _syncingSelection = true;
        if (!ReferenceEquals(_activeList, PinnedListView) && PinnedListView.SelectedIndex >= 0)
            PinnedListView.SelectedIndex = -1;
        if (!ReferenceEquals(_activeList, HistoryListView) && HistoryListView.SelectedIndex >= 0)
            HistoryListView.SelectedIndex = -1;
        if (_activeList != null
            && _activeIndex >= 0
            && _activeIndex < _activeList.Items.Count
            && _activeList.SelectedIndex != _activeIndex)
        {
            _activeList.SelectedIndex = _activeIndex;
        }
        _syncingSelection = false;
    }

    private void ClipboardList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || sender is not ListView list || list.SelectedIndex < 0)
            return;

        if (!_userSelecting)
        {
            EnforceExclusiveSelection();
            return;
        }

        _activeList = list;
        _syncingSelection = true;
        if (!ReferenceEquals(list, PinnedListView))
            PinnedListView.SelectedIndex = -1;
        if (!ReferenceEquals(list, HistoryListView))
            HistoryListView.SelectedIndex = -1;
        _syncingSelection = false;
    }

    private static bool IsKeyDown(Windows.System.VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down)
        == Windows.UI.Core.CoreVirtualKeyStates.Down;

    private PreviewData MapToPreviewData(ClipboardItemViewModel vm)
    {
        var data = new PreviewData
        {
            Id = vm.Id,
            PreviewType = vm.ContentType,
            TextContent = vm.Preview, // Default to preview, will be replaced with full data below
            ImageBytes = null, // Will be populated by LoadFullData if available
            Thumbnail = vm.Thumbnail,
            RelativeTime = vm.RelativeTime,
            CreatedAtTimestamp = vm.CreatedAtTimestamp,
            SourceApp = vm.SourceApp,
            SourceAppIcon = vm.SourceAppIcon,
            IsPinned = vm.Pinned,
            LoadFullData = async () =>
            {
                if (App.Bridge != null && !string.IsNullOrEmpty(vm.Id))
                {
                    return await System.Threading.Tasks.Task.Run(() => App.Bridge.GetItemData(vm.Id));
                }
                return null;
            },
        };

        return data;
    }

    private static string ExtractPlainTextFromRtf(string rtf)
    {
        // Simple RTF to plain text extraction for preview fallback
        // This extracts text content while preserving newlines
        var result = new System.Text.StringBuilder();
        var i = 0;
        var braceDepth = 0;
        var skipGroup = false;
        var skipDepth = 0;

        while (i < rtf.Length)
        {
            var ch = rtf[i];

            if (ch == '{')
            {
                braceDepth++;

                // Check if this is a control group to skip
                if (i + 1 < rtf.Length && rtf[i + 1] == '\\')
                {
                    var controlStart = i + 2;
                    var controlEnd = controlStart;
                    while (controlEnd < rtf.Length && char.IsLetter(rtf[controlEnd]))
                    {
                        controlEnd++;
                    }
                    var control = rtf.Substring(controlStart, controlEnd - controlStart);

                    if (
                        control == "fonttbl"
                        || control == "colortbl"
                        || control == "stylesheet"
                        || control == "generator"
                        || control == "info"
                    )
                    {
                        skipGroup = true;
                        skipDepth = braceDepth;
                    }
                }
                i++;
            }
            else if (ch == '}')
            {
                braceDepth--;
                if (skipGroup && braceDepth < skipDepth)
                {
                    skipGroup = false;
                }
                i++;
            }
            else if (ch == '\\' && !skipGroup)
            {
                i++;
                if (i < rtf.Length)
                {
                    // Check for escaped characters
                    if (rtf[i] == '\\' || rtf[i] == '{' || rtf[i] == '}')
                    {
                        result.Append(rtf[i]);
                        i++;
                        continue;
                    }

                    // Read control word
                    var controlStart = i;
                    while (i < rtf.Length && char.IsLetter(rtf[i]))
                    {
                        i++;
                    }
                    var control = rtf.Substring(controlStart, i - controlStart);

                    // Skip numeric parameter
                    if (i < rtf.Length && (char.IsDigit(rtf[i]) || rtf[i] == '-'))
                    {
                        if (rtf[i] == '-')
                            i++;
                        while (i < rtf.Length && char.IsDigit(rtf[i]))
                        {
                            i++;
                        }
                    }

                    // Skip optional space
                    if (i < rtf.Length && rtf[i] == ' ')
                    {
                        i++;
                    }

                    // Handle special control words
                    if (control == "par" || control == "line")
                    {
                        result.AppendLine();
                    }
                    else if (control == "tab")
                    {
                        result.Append('\t');
                    }
                }
            }
            else if (!skipGroup)
            {
                result.Append(ch);
                i++;
            }
            else
            {
                i++;
            }
        }

        return result.ToString().Trim();
    }

    private void HistoryListView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject root)
            return;

        var scrollViewer = FindScrollViewer(root);
        if (scrollViewer != null)
        {
            scrollViewer.ViewChanging += (_, _) =>
            {
                if (!_previewService.IsManualPreviewOpen)
                    _previewService.Hide();
            };
        }
    }

    private void ClipboardItem_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListViewItem container || FindItemMenuRow(container) is not FrameworkElement row
            || Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.GetAttachedFlyout(row) is not MenuFlyout menu)
            return;

        e.Handled = true;
        if (ReferenceEquals(_itemContextMenu, menu) && menu.IsOpen)
            return;

        _requestedPreviewItem = null;
        _itemContextMenu?.Hide();
        _itemContextMenu = menu;
        foreach (var entry in menu.Items.OfType<MenuFlyoutItem>())
        {
            if (Equals(entry.Tag, "ManualPreview"))
                entry.Visibility = _previewService.AutomaticPreviewsEnabled
                    ? Visibility.Collapsed : Visibility.Visible;
        }
        _previewService.Hide();
        if (e.TryGetPosition(row, out var position))
            menu.ShowAt(row, position);
        else
            menu.ShowAt(row);
    }

    private static FrameworkElement? FindItemMenuRow(ListViewItem container)
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(container);
        while (pending.Count > 0)
        {
            var element = pending.Dequeue();
            if (element is FrameworkElement row
                && Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.GetAttachedFlyout(row) is MenuFlyout)
                return row;

            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
                pending.Enqueue(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i));
        }
        return null;
    }

    private void ClipboardItemMenu_Opened(object sender, object e)
    {
        // WinUI popup opening can leave a stale busy/resize cursor until pointer movement.
        // Restore it after popup creation, rather than changing cursor state during hover.
        if (ReferenceEquals(_itemContextMenu, sender))
            WindowService.RestoreArrowCursor();
    }

    private void ClipboardItemMenu_Closed(object sender, object e)
    {
        if (!ReferenceEquals(_itemContextMenu, sender))
            return;

        _itemContextMenu = null;
        var requestedItem = _requestedPreviewItem;
        _requestedPreviewItem = null;
        if (requestedItem == null)
            return;

        // Wait for menu dismissal and focus restoration before showing the requested preview.
        int generation = _popupGeneration;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _popupGeneration || !_windowService.IsVisible || _itemContextMenu != null)
                return;

            var list = ViewModel.PinnedItems.Contains(requestedItem) ? PinnedListView : HistoryListView;
            int index = list.Items.IndexOf(requestedItem);
            if (index < 0)
                return;

            SetFocusedItem(list, index, SelectionOwner.Keyboard);
            if (list.ContainerFromItem(requestedItem) is ListViewItem target)
                _previewService.ShowManual(MapToPreviewData(requestedItem), target);
        });
    }

    private void PreviewMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is ClipboardItemViewModel vm)
        {
            _requestedPreviewItem = vm;
            _itemContextMenu?.Hide();
        }
    }

    private async void PinUnpinMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is ClipboardItemViewModel vm)
        {
            await TogglePinAsync(vm);
        }
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is ClipboardItemViewModel vm)
        {
            try
            {
                App.Bridge?.DeleteItem(vm.Id);
                ViewModel.RefreshHistory();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting item: {ex.Message}");
            }
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        var queue = new System.Collections.Generic.Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is ScrollViewer sv)
                return sv;

            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                queue.Enqueue(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(current, i));
            }
        }
        return null;
    }
}
