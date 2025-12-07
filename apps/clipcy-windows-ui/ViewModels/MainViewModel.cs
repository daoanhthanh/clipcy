using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipcy.ViewModels;

/// <summary>
/// ViewModel for the main popup window
/// </summary>
public partial class MainViewModel : ObservableObject
{
  /// <summary>
  /// Observable collection of clipboard items
  /// </summary>
  public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();

  /// <summary>
  /// Status bar text
  /// </summary>
  [ObservableProperty]
  private string _statusText = "Ready";

  /// <summary>
  /// Current search query
  /// </summary>
  [ObservableProperty]
  private string _searchQuery = string.Empty;

  /// <summary>
  /// Refresh the history list from the Rust backend
  /// </summary>
  public void RefreshHistory()
  {
    try
    {
      var bridge = App.Bridge;
      if (bridge == null) return;

      var items = bridge.GetHistory(50);
      Items.Clear();

      foreach (var item in items)
      {
        Items.Add(new ClipboardItemViewModel
        {
          Id = item.Id,
          ContentType = item.ContentType,
          Preview = item.Preview,
          IsPinned = item.Pinned,
          CreatedAtTimestamp = item.CreatedAtTimestamp
        });
      }

      StatusText = $"{Items.Count} items";
    }
    catch (Exception ex)
    {
      StatusText = $"Error: {ex.Message}";
    }
  }

  /// <summary>
  /// Search clipboard history
  /// </summary>
  public void Search(string query)
  {
    SearchQuery = query;

    if (string.IsNullOrWhiteSpace(query))
    {
      RefreshHistory();
      return;
    }

    try
    {
      var bridge = App.Bridge;
      if (bridge == null) return;

      var items = bridge.Search(query);
      Items.Clear();

      foreach (var item in items)
      {
        Items.Add(new ClipboardItemViewModel
        {
          Id = item.Id,
          ContentType = item.ContentType,
          Preview = item.Preview,
          IsPinned = item.Pinned,
          CreatedAtTimestamp = item.CreatedAtTimestamp
        });
      }

      StatusText = $"{Items.Count} results";
    }
    catch (Exception ex)
    {
      StatusText = $"Search error: {ex.Message}";
    }
  }

  /// <summary>
  /// Paste an item by ID
  /// </summary>
  public void PasteItem(string id)
  {
    try
    {
      App.Bridge?.PasteItem(id);
    }
    catch (Exception ex)
    {
      StatusText = $"Paste error: {ex.Message}";
    }
  }

  /// <summary>
  /// Toggle pinned status for an item
  /// </summary>
  [RelayCommand]
  private void TogglePin(ClipboardItemViewModel item)
  {
    try
    {
      App.Bridge?.SetPinned(item.Id, !item.IsPinned);
      item.IsPinned = !item.IsPinned;
    }
    catch (Exception ex)
    {
      StatusText = $"Pin error: {ex.Message}";
    }
  }

  /// <summary>
  /// Delete an item
  /// </summary>
  [RelayCommand]
  private void DeleteItem(ClipboardItemViewModel item)
  {
    try
    {
      App.Bridge?.DeleteItem(item.Id);
      Items.Remove(item);
      StatusText = $"{Items.Count} items";
    }
    catch (Exception ex)
    {
      StatusText = $"Delete error: {ex.Message}";
    }
  }

  /// <summary>
  /// Clear all history
  /// </summary>
  [RelayCommand]
  private void ClearHistory()
  {
    try
    {
      App.Bridge?.ClearHistory();
      RefreshHistory();
    }
    catch (Exception ex)
    {
      StatusText = $"Clear error: {ex.Message}";
    }
  }
}
