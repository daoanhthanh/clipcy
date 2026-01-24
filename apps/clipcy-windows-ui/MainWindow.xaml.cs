using Clipcy.Services;
using Clipcy.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Clipcy;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    
    private readonly WindowService _windowService;
    private readonly PreviewService _previewService;

    public MainWindow()
    {
        this.InitializeComponent();
        
        // Initialize services
        _windowService = new WindowService(this);
        _windowService.Initialize();
        
        // RootGrid is available after InitializeComponent
        _previewService = new PreviewService(this, RootGrid, _windowService);

        ViewModel = new MainViewModel();
        ((FrameworkElement)this.Content).DataContext = this;

        // Register for theme management
        if (RootGrid != null)
        {
            ThemeManager.RegisterElement(RootGrid);
            ThemeManager.SetTheme(ThemeManager.CurrentTheme); // Apply current global theme
        }

        this.Activated += MainWindow_Activated;
        ViewModel.RefreshHistory();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            // Only hide if the window was previously visible
            // This prevents hiding immediately after showing
            if (this.AppWindow.IsVisible)
            {
                this.HidePopup();
            }
        }
        else if (
            args.WindowActivationState == WindowActivationState.CodeActivated
            || args.WindowActivationState == WindowActivationState.PointerActivated
        )
        {
            // Use async version to avoid blocking UI thread
            _ = ViewModel.RefreshHistoryAsync();
            SearchBox.Text = string.Empty;

            // Don't set any focus - let user navigate with mouse or arrow keys
            // Force focus to RootGrid or ListView to prevent SearchBox from getting it
            HistoryListView.SelectedIndex = -1;
            RootGrid.Focus(FocusState.Programmatic);

            // Keep acrylic effect active
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
        // Hide popup first
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

        // Handle Enter to paste selected item
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (HistoryListView.SelectedItem is ClipboardItemViewModel item)
            {
                e.Handled = true;
                PasteSelectedItem(item);
                return;
            }
        }

        // Handle Up/Down arrow keys for navigation
        if (e.Key == Windows.System.VirtualKey.Down)
        {
            // If search box has focus, move to list
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
                // No selection, select first item
                if (ViewModel.Items.Count > 0)
                {
                    HistoryListView.SelectedIndex = 0;
                    HistoryListView.Focus(FocusState.Keyboard);
                }
            }
            else if (HistoryListView.SelectedIndex < ViewModel.Items.Count - 1)
            {
                // Move down
                HistoryListView.SelectedIndex++;
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            e.Handled = true;

            if (HistoryListView.SelectedIndex > 0)
            {
                // Move up
                HistoryListView.SelectedIndex--;
            }
            else if (HistoryListView.SelectedIndex == 0)
            {
                // At top, deselect - user can manually click search box if needed
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
        _previewService.HidePreview();
        this.AppWindow.Hide();
    }

    public bool IsVisible => this.AppWindow.IsVisible;

    private void HistoryListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is ListViewItem item)
        {
            // Remove old handlers first to avoid duplicates
            item.PointerEntered -= ListViewItem_PointerEntered;
            item.PointerExited -= ListViewItem_PointerExited;
            item.GotFocus -= ListViewItem_GotFocus;
            item.LostFocus -= ListViewItem_LostFocus;

            // Attach new handlers
            item.PointerEntered += ListViewItem_PointerEntered;
            item.PointerExited += ListViewItem_PointerExited;
            item.GotFocus += ListViewItem_GotFocus;
            item.LostFocus += ListViewItem_LostFocus;
        }
    }

    private void ListViewItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // If SearchBox has focus, clear it by focusing the item or list
        if (SearchBox != null && SearchBox.FocusState != FocusState.Unfocused)
        {
            // Focus the list view itself or the item to blur search box
            if (sender is ListViewItem item)
            {
                item.Focus(FocusState.Pointer);
            }
        }

        if (sender is ListViewItem li && li.Content is ClipboardItemViewModel viewModel)
        {
            // Schedule preview with delay (same as keyboard navigation)
            _previewService.ScheduleShowPreview(viewModel, li);
        }
    }

    private void ListViewItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Schedule hide with delay
        _previewService.ScheduleHidePreview();
    }

    private void ListViewItem_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewItem item && item.Content is ClipboardItemViewModel viewModel)
        {
            // Treat focus same as hover - schedule preview with delay
            _previewService.ScheduleShowPreview(viewModel, item);
        }
    }

    private void ListViewItem_LostFocus(object sender, RoutedEventArgs e)
    {
        // Cancel pending and schedule hide
        _previewService.ScheduleHidePreview();
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _previewService.HidePreview();
    }
}
