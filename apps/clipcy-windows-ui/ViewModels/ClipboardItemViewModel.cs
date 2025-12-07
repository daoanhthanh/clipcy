using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipcy.ViewModels;

/// <summary>
/// ViewModel for a single clipboard history item
/// </summary>
public partial class ClipboardItemViewModel : ObservableObject
{
  /// <summary>
  /// Unique identifier for the item
  /// </summary>
  public string Id { get; init; } = string.Empty;

  /// <summary>
  /// Content type (Text, Image, Files, etc.)
  /// </summary>
  public string ContentType { get; init; } = "Text";

  /// <summary>
  /// Preview text to display
  /// </summary>
  public string Preview { get; init; } = string.Empty;

  /// <summary>
  /// Whether this item is pinned
  /// </summary>
  [ObservableProperty]
  private bool _isPinned;

  /// <summary>
  /// Timestamp when the item was created
  /// </summary>
  public ulong CreatedAtTimestamp { get; init; }

  /// <summary>
  /// Gets the icon glyph based on content type
  /// </summary>
  public string ContentTypeIcon => ContentType switch
  {
    "Image" => "\uE8B9",      // Picture icon
    "Files" => "\uE8A5",       // Folder icon
    "Html" => "\uE8A1",        // Globe icon (web content)
    "RichText" => "\uE8A4",    // Document icon
    _ => "\uE8C8",             // Copy icon (text)
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
        < 1 => "just now",
        < 60 => $"{(int)diff.TotalMinutes}m ago",
        < 1440 => $"{(int)diff.TotalHours}h ago",
        _ => $"{(int)diff.TotalDays}d ago"
      };
    }
  }
}
