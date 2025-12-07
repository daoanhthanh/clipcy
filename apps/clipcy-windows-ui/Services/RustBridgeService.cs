using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Clipcy.Services;

/// <summary>
/// Wrapper service around the UniFFI-generated Rust bindings.
/// This is a placeholder that will be replaced with actual UniFFI-generated code.
/// </summary>
public class RustBridgeService : IDisposable
{
  private bool _disposed;
  private DispatcherQueue? _dispatcherQueue;

  // Event handlers for Rust callbacks
  public event Action? OnHistoryUpdated;
  public event Action? OnHotkeyTriggered;
  public event Action<bool>? OnThemeChanged;

  public RustBridgeService()
  {
    _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
  }

  /// <summary>
  /// Initialize the Rust clipboard engine
  /// </summary>
  public void Initialize()
  {
    // TODO: Replace with actual UniFFI-generated code
    // var dbPath = Path.Combine(
    //     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    //     "clipcy", "clipcy.db");
    //
    // _engine = ClipboardEngine.New(dbPath);
    // _engine.Start(new BridgeCallbackHandler(this));

    System.Diagnostics.Debug.WriteLine("RustBridgeService initialized (placeholder)");
  }

  /// <summary>
  /// Get clipboard history items
  /// </summary>
  public List<ClipboardItemDto> GetHistory(uint limit)
  {
    // TODO: Replace with actual UniFFI call
    // return _engine?.GetHistory(limit).ToList() ?? new List<ClipboardItemDto>();

    // Return mock data for now
    return new List<ClipboardItemDto>
        {
            new() { Id = "1", ContentType = "Text", Preview = "Hello World", Pinned = false, CreatedAtTimestamp = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds() },
            new() { Id = "2", ContentType = "Text", Preview = "Sample text copied from somewhere", Pinned = true, CreatedAtTimestamp = (ulong)DateTimeOffset.Now.AddMinutes(-5).ToUnixTimeSeconds() },
            new() { Id = "3", ContentType = "Image", Preview = "[Image]", Pinned = false, CreatedAtTimestamp = (ulong)DateTimeOffset.Now.AddHours(-1).ToUnixTimeSeconds() },
        };
  }

  /// <summary>
  /// Search clipboard history
  /// </summary>
  public List<ClipboardItemDto> Search(string query)
  {
    // TODO: Replace with actual UniFFI call
    // return _engine?.Search(query).ToList() ?? new List<ClipboardItemDto>();

    return GetHistory(50).Where(i =>
        i.Preview.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
  }

  /// <summary>
  /// Paste an item by ID
  /// </summary>
  public void PasteItem(string id)
  {
    // TODO: Replace with actual UniFFI call
    // _engine?.PasteItem(id);

    System.Diagnostics.Debug.WriteLine($"PasteItem: {id}");
  }

  /// <summary>
  /// Delete an item by ID
  /// </summary>
  public void DeleteItem(string id)
  {
    // TODO: Replace with actual UniFFI call
    // _engine?.DeleteItem(id);

    System.Diagnostics.Debug.WriteLine($"DeleteItem: {id}");
  }

  /// <summary>
  /// Set pinned status for an item
  /// </summary>
  public void SetPinned(string id, bool pinned)
  {
    // TODO: Replace with actual UniFFI call
    // _engine?.SetPinned(id, pinned);

    System.Diagnostics.Debug.WriteLine($"SetPinned: {id} = {pinned}");
  }

  /// <summary>
  /// Clear all non-pinned history
  /// </summary>
  public void ClearHistory()
  {
    // TODO: Replace with actual UniFFI call
    // _engine?.ClearHistory();

    System.Diagnostics.Debug.WriteLine("ClearHistory");
  }

  /// <summary>
  /// Get current settings
  /// </summary>
  public SettingsDto GetSettings()
  {
    // TODO: Replace with actual UniFFI call
    // return _engine?.GetSettings() ?? new SettingsDto();

    return new SettingsDto
    {
      MaxHistoryItems = 100,
      Hotkey = "Ctrl+Shift+V",
      StartOnBoot = true,
      ShowPreviewPanel = true
    };
  }

  /// <summary>
  /// Save settings
  /// </summary>
  public void SaveSettings(SettingsDto settings)
  {
    // TODO: Replace with actual UniFFI call
    // _engine?.SaveSettings(settings);

    System.Diagnostics.Debug.WriteLine($"SaveSettings: {settings.Hotkey}");
  }

  /// <summary>
  /// Check if current theme is dark
  /// </summary>
  public bool IsDarkTheme()
  {
    // TODO: Replace with actual UniFFI call
    // return _engine?.IsDarkTheme() ?? false;

    return true;
  }

  // Internal method to dispatch events to UI thread
  internal void DispatchEvent(Action action)
  {
    _dispatcherQueue?.TryEnqueue(() => action());
  }

  internal void RaiseHistoryUpdated() => DispatchEvent(() => OnHistoryUpdated?.Invoke());
  internal void RaiseHotkeyTriggered() => DispatchEvent(() => OnHotkeyTriggered?.Invoke());
  internal void RaiseThemeChanged(bool isDark) => DispatchEvent(() => OnThemeChanged?.Invoke(isDark));

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    // TODO: Stop the Rust engine
    // _engine?.Stop();

    GC.SuppressFinalize(this);
  }
}

/// <summary>
/// DTO for clipboard items - mirrors Rust ClipboardItemDto
/// </summary>
public class ClipboardItemDto
{
  public string Id { get; init; } = string.Empty;
  public string ContentType { get; init; } = "Text";
  public string Preview { get; init; } = string.Empty;
  public byte[]? Thumbnail { get; init; }
  public string? SourceApp { get; init; }
  public ulong CreatedAtTimestamp { get; init; }
  public bool Pinned { get; init; }
}

/// <summary>
/// DTO for settings - mirrors Rust SettingsDto
/// </summary>
public class SettingsDto
{
  public uint MaxHistoryItems { get; set; } = 100;
  public string Hotkey { get; set; } = "Ctrl+Shift+V";
  public string Theme { get; set; } = "System";
  public bool StartOnBoot { get; set; } = true;
  public bool ShowNotifications { get; set; }
  public bool ShowPreviewPanel { get; set; } = true;
  public List<string> ExcludedApps { get; set; } = new();
}
