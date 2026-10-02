using System.Collections.ObjectModel;
using Clipcy.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipcy.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();

#pragma warning disable MVVMTK0045
    [ObservableProperty]
    private string _statusText = LocalizationService.Get("main.ready");

    [ObservableProperty]
    private string _searchQuery = string.Empty;
#pragma warning restore MVVMTK0045

    private string _statusKey = "main.ready";
    private object[] _statusArguments = Array.Empty<object>();

    public MainViewModel()
    {
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    private void SetStatus(string key, params object[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        StatusText = LocalizationService.Format(key, arguments);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        StatusText = LocalizationService.Format(_statusKey, _statusArguments);
        foreach (var item in Items)
        {
            item.RefreshLocalizedText();
        }
    }

    public void Dispose()
    {
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }


    public async Task RefreshHistoryAsync()
    {
        try
        {
            var bridge = App.Bridge;
            if (bridge == null)
                return;

            // Run database call on background thread
            var items = await Task.Run(() => bridge.GetHistory());

            // Differential update - only change what's different
            // Remove items no longer in the new list
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (!items.Any(x => x.Id == Items[i].Id))
                {
                    Items.RemoveAt(i);
                }
            }

            // Add or update items
            for (int i = 0; i < items.Count; i++)
            {
                var newItem = items[i];
                
                // Check if item exists at correct position
                if (i < Items.Count && Items[i].Id == newItem.Id)
                {
                    // Item is already in correct position, update properties if needed
                    if (Items[i].Pinned != newItem.Pinned)
                    {
                        Items[i].Pinned = newItem.Pinned;
                    }
                }
                else
                {
                    // Find if item exists elsewhere
                    int existingIndex = -1;
                    for (int j = i + 1; j < Items.Count; j++)
                    {
                        if (Items[j].Id == newItem.Id)
                        {
                            existingIndex = j;
                            break;
                        }
                    }

                    if (existingIndex >= 0)
                    {
                        // Move to correct position
                        Items.Move(existingIndex, i);
                    }
                    else
                    {
                        // Insert new item
                        Items.Insert(i, newItem);
                    }
                }
            }

            SetStatus("main.n_items", Items.Count);
        }
        catch (Exception ex)
        {
            SetStatus("main.error", ex.Message);
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

            foreach (var item in items)
            {
                Items.Add(item);
            }

            SetStatus("main.n_results", Items.Count);
        }
        catch (Exception ex)
        {
            SetStatus("main.search_error", ex.Message);
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
            SetStatus("main.paste_error", ex.Message);
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
            SetStatus("main.pin_error", ex.Message);
        }
    }

    [RelayCommand]
    private void DeleteItem(ClipboardItemViewModel item)
    {
        try
        {
            App.Bridge?.DeleteItem(item.Id);
            Items.Remove(item);
            SetStatus("main.n_items", Items.Count);
        }
        catch (Exception ex)
        {
            SetStatus("main.delete_error", ex.Message);
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
            SetStatus("main.clear_error", ex.Message);
        }
    }
}
