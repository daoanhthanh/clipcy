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
    private bool _keyboardNavigationActive;
    private bool _isFirstActivation = true;

    public MainWindow()
    {
        this.InitializeComponent();

        // Manual Dependency Injection
        _windowService = new WindowService(this);
        _windowService.Initialize();

        _previewService = new PreviewService(this, RootGrid, _windowService);

        ViewModel = new MainViewModel();
        ((FrameworkElement)this.Content).DataContext = this;


        // Subscribe to theme changes and apply to Content
        ThemeManager.ThemeChanged += OnThemeChanged;
        ApplyTheme(ThemeManager.CurrentTheme);

        this.Activated += MainWindow_Activated;
        this.Closed += MainWindow_Closed;

        // Subscribe to Rust bridge events
        if (App.Bridge != null)
        {
            // Refresh when clipboard changes
            App.Bridge.OnHistoryUpdated += () => _ = ViewModel.RefreshHistoryAsync();
            
            // Load initial data when Rust engine is ready
            App.Bridge.OnReady += () => _ = ViewModel.RefreshHistoryAsync();
        }

    }

    public WindowService GetWindowService() => (WindowService)_windowService;

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

            // Load data only on first show
            if (_isFirstActivation)
            {
                _ = ViewModel.RefreshHistoryAsync();
                _isFirstActivation = false;
            }

            HistoryListView.SelectedIndex = -1;
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

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            this.HidePopup();
            return;
        }

        if (IsKeyDown(Windows.System.VirtualKey.Control) && e.Key == Windows.System.VirtualKey.P)
        {
            if (HistoryListView.SelectedItem is ClipboardItemViewModel item)
            {
                e.Handled = true;
                try
                {
                    App.Bridge?.SetPinned(item.Id, !item.Pinned);
                    ViewModel.RefreshHistory();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error toggling pin: {ex.Message}");
                }
            }
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Delete)
        {
            if (HistoryListView.SelectedItem is ClipboardItemViewModel item)
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
            if (HistoryListView.SelectedItem is ClipboardItemViewModel item)
            {
                e.Handled = true;
                PasteSelectedItem(item);
                return;
            }
        }

        if (e.Key == Windows.System.VirtualKey.Down)
        {
            _keyboardNavigationActive = true;

            if (SearchBox.FocusState != FocusState.Unfocused)
            {
                e.Handled = true;
                HistoryListView.Focus(FocusState.Programmatic);
                if (HistoryListView.Items.Count > 0)
                {
                    HistoryListView.SelectedIndex = 0;
                }
                return;
            }

            e.Handled = true;

            if (HistoryListView.SelectedIndex < 0)
            {
                if (ViewModel.Items.Count > 0)
                {
                    HistoryListView.SelectedIndex = 0;
                    HistoryListView.Focus(FocusState.Keyboard);
                }
            }
            else if (HistoryListView.SelectedIndex < ViewModel.Items.Count - 1)
            {
                HistoryListView.SelectedIndex++;
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            _keyboardNavigationActive = true;
            e.Handled = true;

            if (HistoryListView.SelectedIndex > 0)
            {
                HistoryListView.SelectedIndex--;
            }
            else if (HistoryListView.SelectedIndex == 0)
            {
                HistoryListView.SelectedIndex = -1;
                RootGrid.Focus(FocusState.Keyboard);
            }
        }
    }

    public void ShowAtCursor()
    {
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
            item.PointerEntered -= ListViewItem_PointerEntered;
            item.PointerMoved -= ListViewItem_PointerMoved;
            item.PointerExited -= ListViewItem_PointerExited;
            item.GotFocus -= ListViewItem_GotFocus;
            item.LostFocus -= ListViewItem_LostFocus;

            item.PointerEntered += ListViewItem_PointerEntered;
            item.PointerMoved += ListViewItem_PointerMoved;
            item.PointerExited += ListViewItem_PointerExited;
            item.GotFocus += ListViewItem_GotFocus;
            item.LostFocus += ListViewItem_LostFocus;
        }

        if (args.Item is ClipboardItemViewModel vm)
        {
            vm.EnsureThumbnailLoaded(this.DispatcherQueue);
        }
    }

    private void ListViewItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListViewItem item)
        {
            _keyboardNavigationActive = false;
            ApplyPointerSelection(item);
        }

        if (sender is ListViewItem li && li.Content is ClipboardItemViewModel viewModel)
        {
            var data = MapToPreviewData(viewModel);
            _previewService.ScheduleShow(data, li);
        }
    }

    private void ListViewItem_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_keyboardNavigationActive && sender is ListViewItem item)
        {
            _keyboardNavigationActive = false;
            ApplyPointerSelection(item);
        }
    }

    private void ListViewItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _previewService.ScheduleHide();
    }

    private void ListViewItem_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewItem item && item.Content is ClipboardItemViewModel viewModel)
        {
            var data = MapToPreviewData(viewModel);
            _previewService.ScheduleShow(data, item);
        }
    }

    private void ListViewItem_LostFocus(object sender, RoutedEventArgs e)
    {
        _previewService.ScheduleHide();
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _previewService.Hide();
    }

    private void ApplyPointerSelection(ListViewItem item)
    {
        var index = HistoryListView.IndexFromContainer(item);
        if (index >= 0 && HistoryListView.SelectedIndex != index)
        {
            HistoryListView.SelectedIndex = index;
        }

        item.Focus(FocusState.Pointer);
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
        var scrollViewer = FindScrollViewer(HistoryListView);
        if (scrollViewer != null)
        {
            scrollViewer.ViewChanging += (s, args) =>
            {
                _previewService?.Hide();
            };
        }
    }

    private void PinUnpinMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is ClipboardItemViewModel vm)
        {
            try
            {
                App.Bridge?.SetPinned(vm.Id, !vm.Pinned);
                ViewModel.RefreshHistory();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling pin: {ex.Message}");
            }
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
