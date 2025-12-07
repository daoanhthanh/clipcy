using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Clipcy.ViewModels;
using Windows.Foundation;

namespace Clipcy;

/// <summary>
/// Main popup window that displays clipboard history
/// </summary>
public sealed partial class MainWindow : Window
{
  public MainViewModel ViewModel { get; }

  public MainWindow()
  {
    this.InitializeComponent();

    // Apply Mica backdrop for Windows 11 look
    SetupMicaBackdrop();

    ViewModel = new MainViewModel();
    HistoryListView.DataContext = ViewModel;

    // Handle window activation/deactivation
    this.Activated += MainWindow_Activated;

    // Load initial data
    ViewModel.RefreshHistory();
  }

  private void SetupMicaBackdrop()
  {
    // Try to apply Mica backdrop (Windows 11)
    try
    {
      if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
      {
        var micaBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        this.SystemBackdrop = micaBackdrop;
      }
    }
    catch
    {
      // Fallback for older Windows versions - just use default background
    }
  }

  private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
  {
    if (args.WindowActivationState == WindowActivationState.Deactivated)
    {
      // Hide window when it loses focus
      this.Hide();
    }
    else
    {
      // Refresh history when window is shown
      ViewModel.RefreshHistory();
      SearchBox.Focus(FocusState.Programmatic);
    }
  }

  private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
  {
    var query = SearchBox.Text;
    ViewModel.Search(query);
  }

  private void HistoryListView_ItemClick(object sender, ItemClickEventArgs e)
  {
    if (e.ClickedItem is ClipboardItemViewModel item)
    {
      // Paste the selected item
      ViewModel.PasteItem(item.Id);
      this.Hide();
    }
  }

  private void SettingsButton_Click(object sender, RoutedEventArgs e)
  {
    // Open settings window
    var settingsWindow = new SettingsWindow();
    settingsWindow.Activate();
  }

  /// <summary>
  /// Show and position the window near the cursor
  /// </summary>
  public void ShowAtCursor()
  {
    // Get cursor position and position window there
    // For now, just center on screen
    this.Activate();
  }

  /// <summary>
  /// Hide the popup window
  /// </summary>
  public void Hide()
  {
    // Minimize to effectively hide (WinUI 3 doesn't have true Hide)
    var presenter = this.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
    presenter?.Minimize();
  }

  /// <summary>
  /// Check if the window is currently visible
  /// </summary>
  public bool IsVisible
  {
    get
    {
      var presenter = this.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
      return presenter?.State != Microsoft.UI.Windowing.OverlappedPresenterState.Minimized;
    }
  }
}
