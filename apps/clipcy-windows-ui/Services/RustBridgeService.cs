using System.ComponentModel;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using uniffi.clipcy_core;
using Windows.Storage.Streams;

namespace Clipcy.Services;

/// <summary>
/// Wrapper service around the UniFFI-generated Rust bindings.
/// Connects the C# UI to the Rust ClipboardEngine via UniFFI.
/// </summary>
internal class RustBridgeService : IDisposable, UiCallback
{
    private bool _disposed;
    private DispatcherQueue? _dispatcherQueue;
    private ClipboardEngine? _engine;

    // Event handlers for Rust callbacks
    public event Action? OnHistoryUpdated;
    public event Action? OnHotkeyTriggered;
    public event Action<bool>? OnThemeChanged;
    public event Action<ClipboardItemViewModel>? OnClipboardChanged;
    public event Action<string>? OnNotification;
    public event Action? OnReady;

    public RustBridgeService()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>
    /// Initialize the Rust clipboard engine
    /// </summary>
    public void Initialize()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "clipcy",
            "clipcy.db"
        );

        // Ensure directory exists
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _engine = new ClipboardEngine(dbPath);
        _engine.Start(this);

        System.Diagnostics.Debug.WriteLine($"RustBridgeService initialized with db: {dbPath}");
    }

    /// <summary>
    /// UiCallback implementation - receives events from Rust
    /// </summary>
    public void OnEvent(BridgeEvent @event)
    {
        switch (@event)
        {
            case BridgeEvent.HistoryUpdated:
                DispatchEvent(() => OnHistoryUpdated?.Invoke());
                break;

            case BridgeEvent.HotkeyTriggered:
                DispatchEvent(() => OnHotkeyTriggered?.Invoke());
                break;

            case BridgeEvent.ThemeChanged themeEvent:
                System.Diagnostics.Debug.WriteLine(
                    $"[RustBridge] ThemeChanged event received from Rust. isDark: {themeEvent.isDark}"
                );
                DispatchEvent(() =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RustBridge] Dispatching OnThemeChanged to {OnThemeChanged?.GetInvocationList().Length ?? 0} subscribers"
                    );
                    OnThemeChanged?.Invoke(themeEvent.isDark);
                });
                break;

            case BridgeEvent.ClipboardChanged clipboardEvent:
                var viewModel = ToViewModel(clipboardEvent.item);
                DispatchEvent(() => OnClipboardChanged?.Invoke(viewModel));
                DispatchEvent(() => OnHistoryUpdated?.Invoke());
                break;

            case BridgeEvent.Error errorEvent:
                System.Diagnostics.Debug.WriteLine($"Rust error: {errorEvent.message}");
                break;

            case BridgeEvent.Notification notificationEvent:
                DispatchEvent(() => OnNotification?.Invoke(notificationEvent.message));
                break;

            case BridgeEvent.Ready:
                DispatchEvent(() => OnReady?.Invoke());
                break;
        }
    }

    /// <summary>
    /// Get clipboard history items (uses max_history from settings)
    /// </summary>
    public List<ClipboardItemViewModel> GetHistory()
    {
        if (_engine == null)
            return new List<ClipboardItemViewModel>();

        try
        {
            var items = _engine.GetHistory();
            return items.Select(ToViewModel).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetHistory error: {ex.Message}");
            return new List<ClipboardItemViewModel>();
        }
    }

    /// <summary>
    /// Search clipboard history
    /// </summary>
    public List<ClipboardItemViewModel> Search(string query)
    {
        if (_engine == null)
            return new List<ClipboardItemViewModel>();

        try
        {
            var items = _engine.Search(query);
            return items.Select(ToViewModel).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search error: {ex.Message}");
            return new List<ClipboardItemViewModel>();
        }
    }

    /// <summary>
    /// Paste an item by ID
    /// </summary>
    public void PasteItem(string id)
    {
        try
        {
            _engine?.PasteItem(id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PasteItem error: {ex.Message}");
        }
    }

    /// <summary>
    /// Save current foreground window for paste target
    /// </summary>
    public void SaveForegroundWindow()
    {
        try
        {
            _engine?.SaveForegroundWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveForegroundWindow error: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete an item by ID
    /// </summary>
    public void DeleteItem(string id)
    {
        try
        {
            _engine?.DeleteItem(id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DeleteItem error: {ex.Message}");
        }
    }

    /// <summary>
    /// Set pinned status for an item
    /// </summary>
    public void SetPinned(string id, bool pinned)
    {
        try
        {
            _engine?.SetPinned(id, pinned);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetPinned error: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear all non-pinned history
    /// </summary>
    public void ClearHistory()
    {
        try
        {
            _engine?.ClearHistory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ClearHistory error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get full item data (for preview)
    /// </summary>
    public byte[]? GetItemData(string id)
    {
        try
        {
            return _engine?.GetItemData(id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetItemData error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get current settings
    /// </summary>
    public SettingsViewModel GetSettings()
    {
        try
        {
            var settings = _engine?.GetSettings();
            if (settings != null)
            {
                return new SettingsViewModel
                {
                    MaxHistoryItems = settings.maxHistoryItems,
                    Hotkey = settings.hotkey,
                    Theme = settings.theme.ToString(),
                    StartOnBoot = settings.startOnBoot,
                    ShowNotifications = settings.showNotifications,
                    ShowPreviewPanel = settings.showPreviewPanel,
                    FirstRun = settings.firstRun,
                    ExcludedApps = settings.excludedApps.ToList(),
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetSettings error: {ex.Message}");
        }

        return new SettingsViewModel();
    }

    /// <summary>
    /// Save settings
    /// </summary>
    public void SaveSettings(SettingsViewModel settings)
    {
        try
        {
            var theme = settings.Theme switch
            {
                "Light" => ThemeModeDto.Light,
                "Dark" => ThemeModeDto.Dark,
                _ => ThemeModeDto.System,
            };

            var dto = new SettingsDto(
                maxHistoryItems: settings.MaxHistoryItems,
                hotkey: settings.Hotkey,
                theme: theme,
                startOnBoot: settings.StartOnBoot,
                showNotifications: settings.ShowNotifications,
                showPreviewPanel: settings.ShowPreviewPanel,
                excludedApps: settings.ExcludedApps.ToArray(),
                firstRun: settings.FirstRun
            );

            _engine?.SaveSettings(dto);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveSettings error: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if current theme is dark
    /// </summary>
    public bool IsDarkTheme()
    {
        try
        {
            return _engine?.IsDarkTheme() ?? false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"IsDarkTheme error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get app version from Rust backend
    /// </summary>
    public string GetAppVersion()
    {
        try
        {
            return _engine?.GetAppVersion() ?? string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetAppVersion error: {ex.Message}");
            return string.Empty;
        }
    }

    // Internal method to dispatch events to UI thread
    internal void DispatchEvent(Action action)
    {
        if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }

    private static ClipboardItemViewModel ToViewModel(uniffi.clipcy_core.ClipboardItemDto dto)
    {
        return new ClipboardItemViewModel
        {
            Id = dto.id,
            ContentType = dto.contentType.ToString(),
            Preview = dto.preview,
            Thumbnail = dto.thumbnail,
            SourceApp = dto.sourceApp,
            SourceAppIcon = dto.sourceAppIcon,
            CreatedAtTimestamp = dto.createdAtTimestamp,
            Pinned = dto.pinned,
            HasMultipleLines = dto.hasNewline,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _engine?.Stop();
            _engine?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dispose error: {ex.Message}");
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ViewModel for clipboard items - used by UI layer
/// </summary>
public class ClipboardItemViewModel : INotifyPropertyChanged
{
    private static readonly LruCache<string, BitmapImage> ThumbnailCache = new(200);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; init; } = string.Empty;
    public string ContentType { get; init; } = "Text";
    public string Preview { get; init; } = string.Empty;
    public byte[]? Thumbnail { get; init; }
    public string? SourceApp { get; init; }
    public byte[]? SourceAppIcon { get; init; }
    public ulong CreatedAtTimestamp { get; init; }
    private bool _pinned;
    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (_pinned == value)
                return;
            _pinned = value;
            RaisePropertyChanged(nameof(Pinned));
            RaisePropertyChanged(nameof(PinUnpinText));
            RaisePropertyChanged(nameof(PinnedVisibility));
        }
    }
    public bool HasMultipleLines { get; init; }

    public Visibility MultiLineIndicatorVisibility => HasMultipleLines ? Visibility.Visible : Visibility.Collapsed;

    public string DisplayPreview
    {
        get
        {
            if (ContentType == "Image" && Preview == "[Image]")
                return LocalizationService.Get("content.image_placeholder");
            if (ContentType == "RichText" && Preview == "[RTF Content]")
                return LocalizationService.Get("content.rtf_placeholder");
            if (ContentType == "Html" && Preview == "Table")
                return LocalizationService.Get("content.table");
            if (ContentType == "Files")
                return LocalizeFileSummary(Preview);

            if (string.IsNullOrWhiteSpace(Preview))
            {
                return ContentType switch
                {
                    "Html" => LocalizationService.Get("content.table"),
                    "RichText" => LocalizationService.Get("content.formatted"),
                    _ => string.Empty,
                };
            }

            var firstLine = GetFirstLine(Preview);
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return ContentType == "Html" ? LocalizationService.Get("content.table") : string.Empty;
            }

            if (firstLine.Contains('\t'))
            {
                var parts = firstLine.Split('\t').Select(p => p.Trim());
                var display = string.Join(" | ", parts);
                return string.IsNullOrWhiteSpace(display) ? LocalizationService.Get("content.table") : display;
            }

            return firstLine;
        }
    }

    public bool IsTablePreview =>
        (ContentType == "Html" && (Preview == "Table" || Preview.Contains('\t') || Preview.Contains('\n')))
        || (ContentType == "Text" && Preview.Contains('\t'));

    private static string LocalizeFileSummary(string preview)
    {
        if (preview.Length == 0 || preview[0] is < '0' or > '9')
            return preview;

        // These summaries are generated by Rust; absolute file paths and names remain untouched.
        var parts = preview.Split(" | ", StringSplitOptions.None);
        var localized = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "1 file")
            {
                localized[i] = LocalizationService.Get("preview.one_file");
                continue;
            }
            if (parts[i] == "1 folder")
            {
                localized[i] = LocalizationService.Get("content.one_folder");
                continue;
            }

            var separator = parts[i].IndexOf(' ');
            if (separator <= 0
                || !uint.TryParse(parts[i].AsSpan(0, separator),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var count))
                return preview;

            var key = parts[i][(separator + 1)..] switch
            {
                "files" => "preview.n_files",
                "folders" => "content.n_folders",
                "items" => "main.n_items",
                _ => null,
            };
            if (key == null)
                return preview;
            localized[i] = LocalizationService.Format(key, count);
        }
        return string.Join(" | ", localized);
    }

    public void RefreshLocalizedText()
    {
        RaisePropertyChanged(nameof(DisplayPreview));
        RaisePropertyChanged(nameof(PinUnpinText));
        RaisePropertyChanged(nameof(RelativeTime));
    }

    private static string GetFirstLine(string text)
    {
        var parts = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
                return part;
        }
        return text;
    }

    public bool IsImage => ContentType == "Image";

    public Visibility ImageVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;

    public string PinUnpinText => LocalizationService.Get(Pinned ? "common.unpin" : "common.pin");

    public Visibility PinnedVisibility => Pinned ? Visibility.Visible : Visibility.Collapsed;

    private BitmapImage? _thumbnailImage;
    private Task? _thumbnailLoadTask;
    public BitmapImage? ThumbnailImage
    {
        get { return _thumbnailImage; }
    }

    public void EnsureThumbnailLoaded(DispatcherQueue dispatcher)
    {
        if (_thumbnailImage != null)
            return;
        if (!IsImage || Thumbnail == null || Thumbnail.Length == 0)
            return;
        if (_thumbnailLoadTask != null)
            return;

        if (ThumbnailCache.TryGet(Id, out var cached))
        {
            _thumbnailImage = cached;
            RaisePropertyChanged(nameof(ThumbnailImage));
            return;
        }

        _thumbnailLoadTask = LoadThumbnailAsync(dispatcher);
    }

    private async Task LoadThumbnailAsync(DispatcherQueue dispatcher)
    {
        try
        {
            await RunOnDispatcherAsync(
                dispatcher,
                async () =>
                {
                    if (_thumbnailImage != null || Thumbnail == null || Thumbnail.Length == 0)
                        return;

                    var bitmap = new BitmapImage { DecodePixelHeight = 64 };
                    using var stream = new MemoryStream(Thumbnail);
                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

                    _thumbnailImage = bitmap;
                    ThumbnailCache.Add(Id, bitmap);
                    RaisePropertyChanged(nameof(ThumbnailImage));
                }
            );
        }
        catch { }
        finally
        {
            _thumbnailLoadTask = null;
        }
    }

    private static Task RunOnDispatcherAsync(DispatcherQueue dispatcher, Func<Task> action)
    {
        var tcs = new TaskCompletionSource<bool>();
        if (
            !dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            })
        )
        {
            tcs.TrySetResult(true);
        }
        return tcs.Task;
    }

    private void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Gets the icon glyph based on content type
    /// </summary>
    public string ContentTypeIcon =>
        IsTablePreview
            ? "\uE80A" // Table icon
            : ContentType switch
            {
                "Image" => "\uE8B9", // Picture icon
                "Files" => "\uE8A5", // Folder icon
                "Html" => "\uE8A1", // Globe icon (web content)
                "RichText" => "\uE8A4", // Document icon
                _ => "\uE8C8", // Copy icon (text)
            };

    /// <summary>
    /// Gets a formatted relative time string
    /// </summary>
    public string RelativeTime
    {
        get
        {
            var created = DateTimeOffset.FromUnixTimeSeconds((long)CreatedAtTimestamp);
            var diff = DateTimeOffset.Now - created;

            return diff.TotalMinutes switch
            {
                < 1 => LocalizationService.Get("time.just_now"),
                < 60 => LocalizationService.Format("time.m_ago", (int)diff.TotalMinutes),
                < 1440 => LocalizationService.Format("time.h_ago", (int)diff.TotalHours),
                _ => LocalizationService.Format("time.d_ago", (int)diff.TotalDays),
            };
        }
    }
}

/// <summary>
/// ViewModel for settings - used by UI layer
/// </summary>
public class SettingsViewModel
{
    public uint MaxHistoryItems { get; set; } = 210;
    public string Hotkey { get; set; } = "Alt+V";
    public string Theme { get; set; } = "System";
    public bool StartOnBoot { get; set; } = true;
    public bool ShowNotifications { get; set; }
    public bool ShowPreviewPanel { get; set; } = true;
    public bool FirstRun { get; set; }
    public List<string> ExcludedApps { get; set; } = new();
}
