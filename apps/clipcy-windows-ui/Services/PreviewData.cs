namespace Clipcy.Services;

/// <summary>
/// Represents data needed to render a preview, decoupled from the source ViewModel.
/// </summary>
public class PreviewData
{
    public string Id { get; set; } = string.Empty;
    public string PreviewType { get; set; } = "Text"; // Text, Image, Files, RichText

    // Content payload
    public string? TextContent { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string? RtfContent { get; set; }

    // Metadata for display
    public string RelativeTime { get; set; } = string.Empty;
    public ulong CreatedAtTimestamp { get; set; }
    public string? SourceApp { get; set; }
    public byte[]? SourceAppIcon { get; set; }
    public bool IsPinned { get; set; }

    // Raw thumbnail data (used if full image not loaded yet)
    public byte[]? Thumbnail { get; set; }

    /// <summary>
    /// Optional delegate to asynchronously load full data (e.g. high-res image).
    /// </summary>
    public System.Func<System.Threading.Tasks.Task<byte[]?>>? LoadFullData { get; set; }
}
