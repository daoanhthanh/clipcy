namespace Clipcy.Services;

/// <summary>
/// Data needed to render a preview, decoupled from the source ViewModel.
/// </summary>
public class PreviewData
{
    public string Id { get; set; } = string.Empty;
    public string PreviewType { get; set; } = "Text"; // Text, Image, Files, RichText, Html

    public string? TextContent { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string? RtfContent { get; set; }

    public string RelativeTime { get; set; } = string.Empty;
    public ulong CreatedAtTimestamp { get; set; }
    public string? SourceApp { get; set; }
    public byte[]? SourceAppIcon { get; set; }
    public bool IsPinned { get; set; }

    /// <summary>Raw thumbnail used when full image bytes are not loaded yet.</summary>
    public byte[]? Thumbnail { get; set; }

    /// <summary>Optional async loader for full payload (e.g. high-res image or file paths).</summary>
    public System.Func<System.Threading.Tasks.Task<byte[]?>>? LoadFullData { get; set; }

    // --- Files preview (popup-local only; does not alter clipboard/paste payload) ---

    /// <summary>Original-order absolute paths from bridge full data (LF-separated UTF-8).</summary>
    public IReadOnlyList<string>? FilePaths { get; set; }

    /// <summary>Selected index into <see cref="FilePaths"/>; default 0.</summary>
    public int SelectedFileIndex { get; set; }

    /// <summary>Typed preview of the selected file; discarded on hide.</summary>
    public FilePreviewResult? FilePreview { get; set; }

    /// <summary>True while selected-file content is loading.</summary>
    public bool IsFilePreviewLoading { get; set; }
}
