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

    public MainWindow()
    {
        this.InitializeComponent();

        // Manual Dependency Injection
        _windowService = new WindowService(this);
        _windowService.Initialize();

        _previewService = new PreviewService(this, RootGrid, _windowService);

        ViewModel = new MainViewModel();
        ((FrameworkElement)this.Content).DataContext = this;
        PinnedListView.MaxHeight = WindowService.PinnedRowHeightDip * WindowService.PinnedMaxVisibleRows;
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
        _previewService.Dispose();
    }

    private void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
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

            _windowService.EnsureBackdropActive();
        }
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
    }

    private ClipboardItemViewModel? GetPinTarget() => _focusedItem;

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
        PinnedListView.Height = count == 0
            ? double.NaN
            : visible * WindowService.PinnedRowHeightDip;
        ScrollViewer.SetVerticalScrollBarVisibility(
            PinnedListView,
            count > WindowService.PinnedMaxVisibleRows
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled);
        _windowService.SetExtraHeightDip(WindowService.ExtraHeightForPinnedCount(count));
    }

    public void ShowAtCursor()
    {
        ApplyPinnedChrome();
        _windowService.ShowAtCursor();
    }

    public void HidePopup()
    {
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

            item.PointerEntered += ListViewItem_PointerEntered;
            item.PointerMoved += ListViewItem_PointerMoved;
            item.PointerExited += ListViewItem_PointerExited;
            item.GotFocus += ListViewItem_GotFocus;
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
                _previewService?.Hide();
            };
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
