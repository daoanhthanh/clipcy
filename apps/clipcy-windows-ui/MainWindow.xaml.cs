using Clipcy.Services;
using Clipcy.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Clipcy;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    
    private readonly IWindowService _windowService;
    private readonly IPreviewService _previewService;

    public MainWindow()
    {
        this.InitializeComponent();
        
        // Manual Dependency Injection
        _windowService = new WindowService(this);
        _windowService.Initialize();
        
        _previewService = new PreviewService(this, RootGrid, _windowService);

        ViewModel = new MainViewModel();
        ((FrameworkElement)this.Content).DataContext = this;

        // Register for theme management
        if (RootGrid != null)
        {
            ThemeManager.RegisterElement(RootGrid);
            ThemeManager.SetTheme(ThemeManager.CurrentTheme);
        }

        this.Activated += MainWindow_Activated;
        this.Closed += MainWindow_Closed;
        
        ViewModel.RefreshHistory();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _previewService.Dispose();
        // WindowService doesn't implement IDisposable but if it did, call it here
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
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
            _ = ViewModel.RefreshHistoryAsync();
            SearchBox.Text = string.Empty;

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
        var settingsWindow = new SettingsWindow();
        settingsWindow.Activate();
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
            item.PointerExited -= ListViewItem_PointerExited;
            item.GotFocus -= ListViewItem_GotFocus;
            item.LostFocus -= ListViewItem_LostFocus;

            item.PointerEntered += ListViewItem_PointerEntered;
            item.PointerExited += ListViewItem_PointerExited;
            item.GotFocus += ListViewItem_GotFocus;
            item.LostFocus += ListViewItem_LostFocus;
        }
    }

    private void ListViewItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (SearchBox != null && SearchBox.FocusState != FocusState.Unfocused)
        {
            if (sender is ListViewItem item)
            {
                item.Focus(FocusState.Pointer);
            }
        }

        if (sender is ListViewItem li && li.Content is ClipboardItemViewModel viewModel)
        {
            var data = MapToPreviewData(viewModel);
            _previewService.ScheduleShow(data, li);
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
    
    private PreviewData MapToPreviewData(ClipboardItemViewModel vm)
    {
        return new PreviewData
        {
            Id = vm.Id,
            PreviewType = vm.ContentType,
            TextContent = vm.Preview,
            // For now using Thumbnail for image preview to avoid complex async fetching logic in View.
            // If full resolution is needed, a dedicated data provider service should be introduced.
            ImageBytes = null, 
            Thumbnail = vm.Thumbnail,
            RelativeTime = vm.RelativeTime,
            SourceApp = vm.SourceApp,
            IsPinned = vm.Pinned
        };
    }
}
