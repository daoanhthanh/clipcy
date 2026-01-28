using System.Collections.ObjectModel;
using Clipcy.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipcy.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();

#pragma warning disable MVVMTK0045
    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _searchQuery = string.Empty;
#pragma warning restore MVVMTK0045

    public async Task RefreshHistoryAsync()
    {
        try
        {
            var bridge = App.Bridge;
            if (bridge == null)
                return;

            // Run database call on background thread (uses max_history from settings)
            var items = await Task.Run(() => bridge.GetHistory());

            Items.Clear();

            // Sort pinned items to the top
            var sortedItems = items.OrderByDescending(x => x.Pinned);

            foreach (var item in sortedItems)
            {
                Items.Add(item);
            }

            StatusText = $"{Items.Count} items";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    public void RefreshHistory()
    {
        // Synchronous wrapper for compatibility
        _ = RefreshHistoryAsync();
    }

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
            if (bridge == null)
                return;

            var items = bridge.Search(query);
            Items.Clear();

            // Sort pinned items to the top
            var sortedItems = items.OrderByDescending(x => x.Pinned);

            foreach (var item in sortedItems)
            {
                Items.Add(item);
            }

            StatusText = $"{Items.Count} results";
        }
        catch (Exception ex)
        {
            StatusText = $"Search error: {ex.Message}";
        }
    }

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

    [RelayCommand]
    private void TogglePin(ClipboardItemViewModel item)
    {
        try
        {
            App.Bridge?.SetPinned(item.Id, !item.Pinned);
            item.Pinned = !item.Pinned;
        }
        catch (Exception ex)
        {
            StatusText = $"Pin error: {ex.Message}";
        }
    }

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
