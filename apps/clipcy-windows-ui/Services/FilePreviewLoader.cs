using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Clipcy.Services;

public enum FilePreviewKind
{
    Image,
    Text,
    Video,
    Audio,
    Other,
    Folder,
}

public enum FilePreviewStatus
{
    Ready,
    Loading,
    Unavailable,
    Unsupported,
    Truncated,
    ResourceLimited,
    Canceled,
    RemoteOrCloud,
}

public sealed class FilePreviewResult
{
    public required string OriginalPath { get; init; }
    public required string DisplayName { get; init; }
    public required FilePreviewKind Kind { get; init; }
    public required FilePreviewStatus Status { get; init; }
    public string? TextContent { get; init; }
    public byte[]? PreviewPngBytes { get; init; }
    public bool IsTruncated { get; init; }
    public string? StatusMessage { get; init; }
}

/// <summary>
/// Bounded, local-only preview loader for a single clipboard file path.
/// </summary>
public static class FilePreviewLoader
{
    public const int MaxTextEncodedBytes = 65_536;
    public const int MaxImageEncodedBytes = 32 * 1024 * 1024;
    public const long MaxSourcePixels = 16_000_000;
    public const uint MaxPreviewLongEdge = 1024;

    private static readonly SemaphoreSlim CodecGate = new(1, 1);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff",
        ".ico",
        ".jxr",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".log",
        ".csv",
        ".tsv",
        ".json",
        ".xml",
        ".yaml",
        ".yml",
        ".ini",
        ".toml",
        ".html",
        ".htm",
        ".css",
        ".js",
        ".ts",
        ".cs",
        ".rs",
        ".py",
        ".ps1",
        ".sh",
        ".c",
        ".h",
        ".cpp",
        ".hpp",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".m4v",
        ".mov",
        ".avi",
        ".wmv",
        ".mkv",
        ".webm",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".wav",
        ".flac",
        ".m4a",
        ".aac",
        ".ogg",
        ".wma",
        ".aif",
        ".aiff",
        ".opus",
    };

    public static async Task<FilePreviewResult> LoadAsync(string path, CancellationToken token)
    {
        var displayName = GetDisplayName(path);
        var originalPath = path ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return Result(originalPath, displayName, FilePreviewKind.Other, FilePreviewStatus.Unavailable, "Missing path");

        token.ThrowIfCancellationRequested();

        var kind = Classify(path);

        // Audio: admit path only — no content I/O.
        if (kind == FilePreviewKind.Audio)
        {
            if (!TryAdmitLocalPath(path, out var admitStatus, out var admitMessage, out var audioHandle))
                return Result(originalPath, displayName, kind, admitStatus, admitMessage);

            audioHandle?.Dispose();
            return Result(originalPath, displayName, kind, FilePreviewStatus.Ready, null);
        }

        if (!TryAdmitLocalPath(path, out var status, out var message, out var safeHandle))
            return Result(originalPath, displayName, kind, status, message);

        try
        {
            token.ThrowIfCancellationRequested();

            if (kind is FilePreviewKind.Folder or FilePreviewKind.Other)
            {
                safeHandle?.Dispose();
                safeHandle = null;
                var reason = kind == FilePreviewKind.Folder
                    ? "Folder preview is not supported"
                    : "File type is not previewable";
                return Result(originalPath, displayName, kind, FilePreviewStatus.Unsupported, reason);
            }

            if (kind == FilePreviewKind.Video)
            {
                var videoHandle = safeHandle!;
                safeHandle = null;
                try
                {
                    return await LoadVideoAsync(originalPath, displayName, videoHandle, token);
                }
                finally
                {
                    videoHandle.Dispose();
                }
            }


            // Text/image: FileStream takes ownership. Handle was opened without OVERLAPPED.
            await using var stream = new FileStream(
                safeHandle!,
                FileAccess.Read,
                bufferSize: 64 * 1024,
                isAsync: false
            );
            safeHandle = null;

            return kind switch
            {
                FilePreviewKind.Text => await LoadTextAsync(originalPath, displayName, stream, token),
                FilePreviewKind.Image => await LoadImageAsync(originalPath, displayName, stream, token),
                _ => Result(originalPath, displayName, kind, FilePreviewStatus.Unsupported, "File type is not previewable"),
            };
        }
        catch (OperationCanceledException)
        {
            return Result(originalPath, displayName, kind, FilePreviewStatus.Canceled, "Canceled");
        }
        catch (Exception ex)
        {
            return Result(originalPath, displayName, kind, FilePreviewStatus.Unavailable, ex.Message);
        }
        finally
        {
            safeHandle?.Dispose();
        }
    }

    private static FilePreviewKind Classify(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return FilePreviewKind.Folder;
        }
        catch
        {
            // Fall through to extension classification.
        }

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            return FilePreviewKind.Other;
        if (ImageExtensions.Contains(ext))
            return FilePreviewKind.Image;
        if (TextExtensions.Contains(ext))
            return FilePreviewKind.Text;
        if (VideoExtensions.Contains(ext))
            return FilePreviewKind.Video;
        if (AudioExtensions.Contains(ext))
            return FilePreviewKind.Audio;
        return FilePreviewKind.Other;
    }

    private static string GetDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        try
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch
        {
            return path;
        }
    }

    private static FilePreviewResult Result(
        string path,
        string displayName,
        FilePreviewKind kind,
        FilePreviewStatus status,
        string? message,
        string? text = null,
        byte[]? png = null,
        bool truncated = false
    ) =>
        new()
        {
            OriginalPath = path,
            DisplayName = displayName,
            Kind = kind,
            Status = status,
            StatusMessage = message,
            TextContent = text,
            PreviewPngBytes = png,
            IsTruncated = truncated,
        };

    /// <summary>
    /// Admit only ordinary absolute local fixed/removable paths. Reject UNC, remote,
    /// reparse/junction/symlink/cloud placeholders. Opens a read-only shareable handle.
    /// </summary>
    private static bool TryAdmitLocalPath(
        string path,
        out FilePreviewStatus status,
        out string? message,
        out SafeFileHandle? handle
    )
    {
        status = FilePreviewStatus.Unavailable;
        message = null;
        handle = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            message = "Missing path";
            return false;
        }

        // Drive-letter paths use colon at index 1 ("C:\..."); reject ADS / other namespaces.
        if (path.IndexOf(':') is >= 0 and not 1)
        {
            status = FilePreviewStatus.RemoteOrCloud;
            message = "Unsupported path namespace";
            return false;
        }


        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            status = FilePreviewStatus.RemoteOrCloud;
            message = "Remote or UNC paths are not previewed";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }

        if (!Path.IsPathRooted(fullPath) || fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            status = FilePreviewStatus.RemoteOrCloud;
            message = "Only local absolute paths are previewed";
            return false;
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            status = FilePreviewStatus.RemoteOrCloud;
            message = "Only local volume roots are previewed";
            return false;
        }

        try
        {
            var driveLetter = root.TrimEnd('\\', '/');
            if (driveLetter.Length >= 2 && driveLetter[1] == ':')
            {
                var drive = new DriveInfo(driveLetter);
                if (!drive.IsReady)
                {
                    message = "Drive is not ready";
                    return false;
                }

                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    status = FilePreviewStatus.RemoteOrCloud;
                    message = $"Drive type {drive.DriveType} is not previewed";
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            status = FilePreviewStatus.RemoteOrCloud;
            message = ex.Message;
            return false;
        }

        const uint fileFlagOpenReparsePoint = 0x00200000;
        const uint fileFlagBackupSemantics = 0x02000000;
        const uint fileFlagOpenNoRecall = 0x00100000;


        var desiredAccess = unchecked((uint)0x80000000); // GENERIC_READ
        var share = (uint)(FileShare.Read | FileShare.Write | FileShare.Delete);
        var flags = fileFlagOpenNoRecall | fileFlagOpenReparsePoint | fileFlagBackupSemantics;

        var native = NativeMethods.CreateFileW(
            fullPath,
            desiredAccess,
            share,
            IntPtr.Zero,
            3 /* OPEN_EXISTING */,
            flags,
            IntPtr.Zero
        );

        if (native.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            // Retry without OPEN_REPARSE_POINT if the reparse-aware open failed.
            native = NativeMethods.CreateFileW(
                fullPath,
                desiredAccess,
                share,
                IntPtr.Zero,
                3,
                fileFlagOpenNoRecall,
                IntPtr.Zero
            );
            if (native.IsInvalid)
            {
                message = $"Cannot open file (Win32 {err}/{Marshal.GetLastWin32Error()})";
                return false;
            }
        }

        try
        {
            var attrs = NativeMethods.GetFileAttributesW(fullPath);
            if (attrs == NativeMethods.INVALID_FILE_ATTRIBUTES)
            {
                native.Dispose();
                message = "Cannot read attributes";
                return false;
            }

            const uint fileAttributeReparsePoint = 0x400;
            const uint fileAttributeOffline = 0x1000;
            const uint fileAttributeRecallOnOpen = 0x40000;
            const uint fileAttributeRecallOnDataAccess = 0x400000;

            if ((attrs & fileAttributeReparsePoint) != 0)
            {
                native.Dispose();
                status = FilePreviewStatus.RemoteOrCloud;
                message = "Reparse points, junctions, and symlinks are not previewed";
                return false;
            }

            if (
                (attrs & fileAttributeOffline) != 0
                || (attrs & fileAttributeRecallOnOpen) != 0
                || (attrs & fileAttributeRecallOnDataAccess) != 0
            )
            {
                native.Dispose();
                status = FilePreviewStatus.RemoteOrCloud;
                message = "Online-only or placeholder files are not previewed";
                return false;
            }

            // Caller owns disposal of the admitted handle.
            handle = native;
            status = FilePreviewStatus.Ready;
            return true;
        }
        catch
        {
            native.Dispose();
            throw;
        }
    }

    private static async Task<FilePreviewResult> LoadTextAsync(
        string path,
        string displayName,
        FileStream stream,
        CancellationToken token
    )
    {
        // Read prefix + small lookahead for truncation detection.
        const int lookahead = 8;
        var buffer = ArrayPool<byte>.Shared.Rent(MaxTextEncodedBytes + lookahead);
        try
        {
            var total = 0;
            while (total < MaxTextEncodedBytes + lookahead)
            {
                token.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), token);
                if (read == 0)
                    break;
                total += read;
            }

            var truncated = total > MaxTextEncodedBytes;
            var prefixLen = Math.Min(total, MaxTextEncodedBytes);
            if (prefixLen == 0)
            {
                return Result(path, displayName, FilePreviewKind.Text, FilePreviewStatus.Ready, null, text: string.Empty);
            }

            var span = buffer.AsSpan(0, prefixLen);
            if (!TryDecodeText(span, truncated, out var text, out var decodeError))
            {
                return Result(
                    path,
                    displayName,
                    FilePreviewKind.Text,
                    FilePreviewStatus.Unsupported,
                    decodeError ?? "Unsupported text encoding or binary content"
                );
            }

            var status = truncated ? FilePreviewStatus.Truncated : FilePreviewStatus.Ready;
            var message = truncated ? "Showing first 64 KiB" : null;
            return Result(path, displayName, FilePreviewKind.Text, status, message, text: text, truncated: truncated);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool TryDecodeText(ReadOnlySpan<byte> data, bool truncated, out string text, out string? error)
    {
        text = string.Empty;
        error = null;
        if (data.IsEmpty)
            return true;

        Encoding encoding;
        int bomLength = 0;

        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            bomLength = 3;
        }
        else if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            encoding = new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
            bomLength = 2;
        }
        else if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            encoding = new System.Text.UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
            bomLength = 2;
        }
        else
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }

        var payload = data[bomLength..];
        if (payload.IsEmpty)
        {
            text = string.Empty;
            return true;
        }

        // For truncated UTF-8/UTF-16, drop incomplete trailing sequence only.
        if (truncated)
        {
            payload = TrimIncompleteTrailingSequence(payload, encoding);
        }

        try
        {
            text = encoding.GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            error = "Invalid encoding or binary content";
            return false;
        }

        // Reject interior NUL / heavy binary control content (except common whitespace).
        var control = 0;
        foreach (var ch in text)
        {
            if (ch == '\0')
            {
                error = "Binary content is not previewed as text";
                return false;
            }

            if (char.IsControl(ch) && ch is not ('\t' or '\n' or '\r'))
                control++;
        }

        if (text.Length > 0 && control > Math.Max(8, text.Length / 20))
        {
            error = "Binary content is not previewed as text";
            return false;
        }

        return true;
    }

    private static ReadOnlySpan<byte> TrimIncompleteTrailingSequence(ReadOnlySpan<byte> payload, Encoding encoding)
    {
        if (encoding.CodePage == Encoding.UTF8.CodePage)
        {
            // Walk back over incomplete UTF-8 trailing bytes.
            var i = payload.Length;
            while (i > 0)
            {
                var b = payload[i - 1];
                if ((b & 0x80) == 0)
                    break; // ASCII
                if ((b & 0xC0) == 0xC0)
                {
                    // Lead byte — check if sequence is complete.
                    var need = b switch
                    {
                        >= 0xF0 => 4,
                        >= 0xE0 => 3,
                        >= 0xC0 => 2,
                        _ => 1,
                    };
                    var have = payload.Length - (i - 1);
                    if (have < need)
                        return payload[..(i - 1)];
                    break;
                }

                // Continuation byte
                i--;
                if (payload.Length - i > 3)
                    break;
            }

            return payload;
        }

        if (encoding.CodePage is 1200 or 1201)
        {
            // UTF-16: odd trailing byte is incomplete.
            if ((payload.Length & 1) == 1)
                return payload[..^1];

            // High surrogate without low surrogate at end.
            if (payload.Length >= 2)
            {
                int cu =
                    encoding.CodePage == 1200
                        ? payload[^2] | (payload[^1] << 8)
                        : payload[^1] | (payload[^2] << 8);
                if (cu is >= 0xD800 and <= 0xDBFF)
                    return payload[..^2];
            }
        }

        return payload;
    }

    private static async Task<FilePreviewResult> LoadImageAsync(
        string path,
        string displayName,
        FileStream stream,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();

        if (stream.Length > MaxImageEncodedBytes)
        {
            return Result(
                path,
                displayName,
                FilePreviewKind.Image,
                FilePreviewStatus.ResourceLimited,
                "Image exceeds 32 MiB encoded limit"
            );
        }

        var acquired = false;
        try
        {
            await CodecGate.WaitAsync(token);
            acquired = true;
            if (token.IsCancellationRequested)
                return Result(path, displayName, FilePreviewKind.Image, FilePreviewStatus.Canceled, "Canceled");

            stream.Position = 0;
            var ras = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras).AsTask(token);

            var width = decoder.PixelWidth;
            var height = decoder.PixelHeight;
            if (width == 0 || height == 0)
            {
                return Result(path, displayName, FilePreviewKind.Image, FilePreviewStatus.Unsupported, "Empty image");
            }

            if ((long)width * height > MaxSourcePixels)
            {
                return Result(
                    path,
                    displayName,
                    FilePreviewKind.Image,
                    FilePreviewStatus.ResourceLimited,
                    "Image exceeds 16M pixel limit"
                );
            }

            var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
            var longest = Math.Max(width, height);
            if (longest > MaxPreviewLongEdge)
            {
                var scale = (double)MaxPreviewLongEdge / longest;
                transform.ScaledWidth = (uint)Math.Max(1, Math.Round(width * scale));
                transform.ScaledHeight = (uint)Math.Max(1, Math.Round(height * scale));
            }
            else
            {
                transform.ScaledWidth = width;
                transform.ScaledHeight = height;
            }

            token.ThrowIfCancellationRequested();

            var soft = await decoder
                .GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb
                )
                .AsTask(token);

            using var outMs = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outMs).AsTask(token);
            encoder.SetSoftwareBitmap(SoftwareBitmap.Convert(soft, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied));
            await encoder.FlushAsync().AsTask(token);

            var size = (int)outMs.Size;
            var bytes = new byte[size];
            outMs.Seek(0);
            await outMs.ReadAsync(bytes.AsBuffer(), (uint)size, InputStreamOptions.None).AsTask(token);

            return Result(path, displayName, FilePreviewKind.Image, FilePreviewStatus.Ready, null, png: bytes);
        }
        catch (OperationCanceledException)
        {
            return Result(path, displayName, FilePreviewKind.Image, FilePreviewStatus.Canceled, "Canceled");
        }
        catch (Exception ex)
        {
            return Result(path, displayName, FilePreviewKind.Image, FilePreviewStatus.Unavailable, ex.Message);
        }
        finally
        {
            if (acquired)
                CodecGate.Release();
        }
    }

    private static async Task<FilePreviewResult> LoadVideoAsync(
        string path,
        string displayName,
        SafeFileHandle handle,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();

        var gateHeld = false;
        try
        {
            await CodecGate.WaitAsync(token);
            gateHeld = true;
            if (token.IsCancellationRequested)
                return Result(path, displayName, FilePreviewKind.Video, FilePreviewStatus.Canceled, "Canceled");

            var decode = await Task.Factory.StartNew(
                () => VideoFirstFrameDecoder.TryDecodeFirstFramePng(handle, token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );

            if (decode.Status == FilePreviewStatus.Canceled || token.IsCancellationRequested)
                return Result(path, displayName, FilePreviewKind.Video, FilePreviewStatus.Canceled, "Canceled");

            if (decode.PngBytes is { Length: > 0 } && decode.Status == FilePreviewStatus.Ready)
                return Result(path, displayName, FilePreviewKind.Video, FilePreviewStatus.Ready, null, png: decode.PngBytes);


            return Result(
                path,
                displayName,
                FilePreviewKind.Video,
                decode.Status,
                decode.Message ?? "Video frame unavailable"
            );
        }
        catch (OperationCanceledException)
        {
            return Result(path, displayName, FilePreviewKind.Video, FilePreviewStatus.Canceled, "Canceled");
        }
        catch (Exception ex)
        {
            return Result(path, displayName, FilePreviewKind.Video, FilePreviewStatus.Unavailable, ex.Message);
        }
        finally
        {
            if (gateHeld)
                CodecGate.Release();
        }
    }

    private static class NativeMethods
    {
        public const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFileAttributesW(string lpFileName);
    }
}
