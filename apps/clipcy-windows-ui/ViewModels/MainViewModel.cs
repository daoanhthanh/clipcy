using System.Collections.ObjectModel;
using Clipcy.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipcy.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();
    public ObservableCollection<ClipboardItemViewModel> PinnedItems { get; } = new();
    public ObservableCollection<ClipboardItemViewModel> RegularItems { get; } = new();

#pragma warning disable MVVMTK0045
    [ObservableProperty]
    private string _statusText = LocalizationService.Get("main.ready");

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _hasPinnedItems;
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

            var query = SearchQuery;
            var items = await Task.Run(() => string.IsNullOrWhiteSpace(query)
                ? bridge.GetHistory()
                : bridge.Search(query));
            if (query != SearchQuery)
                return;

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
                
                if (i >= Items.Count || Items[i].Id != newItem.Id)
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

                // Moves retain the existing view model, including its old pin state.
                Items[i].Pinned = newItem.Pinned;
            }

            SyncSections();
            SetStatus(string.IsNullOrWhiteSpace(query) ? "main.n_items" : "main.n_results", Items.Count);
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

            SyncSections();
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
    private async Task TogglePinAsync(ClipboardItemViewModel item)
    {
        try
        {
            var bridge = App.Bridge;
            if (bridge == null)
                return;

            bridge.SetPinned(item.Id, !item.Pinned);
            item.Pinned = !item.Pinned;
            await RefreshHistoryAsync();
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
            SyncSections();
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

    private void SyncSections()
    {
        var pinned = new List<ClipboardItemViewModel>();
        var regular = new List<ClipboardItemViewModel>();
        foreach (var item in Items)
        {
            if (item.Pinned)
                pinned.Add(item);
            else
                regular.Add(item);
        }

        // Drop cross-membership before inserts so a row is never in both lists.
        RemoveMissing(PinnedItems, pinned);
        RemoveMissing(RegularItems, regular);
        Align(PinnedItems, pinned);
        Align(RegularItems, regular);
        HasPinnedItems = PinnedItems.Count > 0;
        UpdateShortcutLabels();
    }

    private void UpdateShortcutLabels()
    {
        foreach (var item in Items)
            item.ShortcutLabel = string.Empty;
        for (int i = 0; i < Math.Min(10, RegularItems.Count); i++)
            RegularItems[i].ShortcutLabel = $"Ctrl+{(i + 1) % 10}";
    }

    private static void RemoveMissing(
        ObservableCollection<ClipboardItemViewModel> target,
        List<ClipboardItemViewModel> desired)
    {
        var keep = new HashSet<ClipboardItemViewModel>(desired);
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(target[i]))
                target.RemoveAt(i);
        }
    }

    private static void Align(
        ObservableCollection<ClipboardItemViewModel> target,
        List<ClipboardItemViewModel> desired)
    {
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i]))
                continue;

            int existing = -1;
            for (int j = i; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], desired[i]))
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
                target.Move(existing, i);
            else if (i >= target.Count)
                target.Add(desired[i]);
            else
                target.Insert(i, desired[i]);
        }

        while (target.Count > desired.Count)
            target.RemoveAt(target.Count - 1);
    }
}
