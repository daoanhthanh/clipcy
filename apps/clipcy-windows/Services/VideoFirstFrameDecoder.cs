using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Win32.SafeHandles;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Clipcy.Services;

/// <summary>
/// Synchronous Media Foundation first-presentation-frame extraction on an MTA worker.
/// Native RGB32 is BGRX/X8R8G8B8; alpha forced opaque before BGRA8 PNG encode.
/// </summary>
internal static class VideoFirstFrameDecoder
{
    private const int MaxReadSampleCalls = 128;
    private static readonly TimeSpan MaxElapsed = TimeSpan.FromSeconds(5);
    private const long MaxSourcePixels = 16_000_000;
    private const uint MaxPreviewLongEdge = 1024;
    private static readonly Guid[] PreferredOutputSubtypes =
    [
        Native.MFVideoFormat_RGB32,
        Native.MFVideoFormat_NV12,
        Native.MFVideoFormat_YUY2,
    ];

    internal readonly record struct DecodeOutcome(
        FilePreviewStatus Status, string? Message, byte[]? PngBytes, object[]? MessageArguments = null);

    public static DecodeOutcome TryDecodeFirstFramePng(SafeFileHandle fileHandle, CancellationToken token)
    {
        if (fileHandle.IsInvalid || fileHandle.IsClosed)
            return new(FilePreviewStatus.Unavailable, "file_preview.invalid_handle", null);

        var hrCo = Native.CoInitializeEx(IntPtr.Zero, Native.COINIT_MULTITHREADED);
        var weInitializedCom = hrCo == Native.S_OK;
        if (hrCo < 0 && hrCo != Native.RPC_E_CHANGED_MODE && hrCo != Native.S_FALSE)
            return new(FilePreviewStatus.Unavailable, "file_preview.coinit_failed", null, [hrCo]);

        var mfStarted = false;
        IntPtr reader = IntPtr.Zero;
        IntPtr byteStream = IntPtr.Zero;
        FileStream? managedStream = null;
        SafeFileHandle? dup = null;
        object? istreamRoot = null;

        try
        {
            if (token.IsCancellationRequested)
                return new(FilePreviewStatus.Canceled, "status.canceled", null);

            var hr = Native.MFStartup(Native.MF_VERSION, Native.MFSTARTUP_FULL);
            if (hr < 0)
                return new(FilePreviewStatus.Unavailable, "file_preview.mfstartup_failed", null, [hr]);
            mfStarted = true;

            if (!Native.DuplicateHandle(
                    Native.GetCurrentProcess(),
                    fileHandle.DangerousGetHandle(),
                    Native.GetCurrentProcess(),
                    out var dupHandle,
                    0,
                    false,
                    Native.DUPLICATE_SAME_ACCESS
                ))
            {
                return new(FilePreviewStatus.Unavailable, "file_preview.dup_handle_failed", null, [Marshal.GetLastWin32Error()]);
            }

            dup = new SafeFileHandle(dupHandle, ownsHandle: true);

            // Handle-backed FileStream → COM IStream → IMFByteStream (no pathname reopen).
            managedStream = new FileStream(dup, FileAccess.Read, bufferSize: 64 * 1024, isAsync: false);
            dup = null; // ownership transferred to FileStream
            managedStream.Position = 0;

            var istream = new ComStreamOverStream(managedStream);
            istreamRoot = istream;
            var istreamPtr = Marshal.GetComInterfaceForObject(
                istream,
                typeof(System.Runtime.InteropServices.ComTypes.IStream)
            );
            try
            {
                hr = Native.MFCreateMFByteStreamOnStream(istreamPtr, out byteStream);
            }
            finally
            {
                Marshal.Release(istreamPtr);
            }

            if (hr < 0 || byteStream == IntPtr.Zero)
                return new(FilePreviewStatus.Unavailable, "file_preview.mf_bytestream_failed", null, [hr]);

            Native.IMFByteStream_SetCurrentPosition(byteStream, 0);

            // Enable color conversion / video processing so RGB32 output is available.
            hr = Native.MFCreateAttributes(out var readerAttrs, 2);
            if (hr < 0 || readerAttrs == IntPtr.Zero)
                return new(FilePreviewStatus.Unavailable, "file_preview.mf_attrs_failed", null, [hr]);
            try
            {
                Native.IMFAttributes_SetUINT32(readerAttrs, Native.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
                hr = Native.MFCreateSourceReaderFromByteStream(byteStream, readerAttrs, out reader);
            }
            finally
            {
                Marshal.Release(readerAttrs);
            }

            if (hr < 0 || reader == IntPtr.Zero)
                return new(FilePreviewStatus.Unavailable, "file_preview.source_reader_failed", null, [hr]);

            Native.IMFSourceReader_SetStreamSelection(reader, Native.MF_SOURCE_READER_ALL_STREAMS, false);

            var videoStream = FindFirstVideoStream(reader);
            if (videoStream < 0)
                return new(FilePreviewStatus.Unsupported, "file_preview.no_video_stream", null);

            Native.IMFSourceReader_SetStreamSelection(reader, videoStream, true);

            // Prefer RGB32, then NV12, then YUY2 for decoded output.
            Guid outputSubtype = default;
            var hrType = -1;
            foreach (var candidate in PreferredOutputSubtypes)
            {
                hrType = TrySetOutputSubtype(reader, videoStream, candidate);
                if (hrType >= 0)
                {
                    outputSubtype = candidate;
                    break;
                }
            }
            if (hrType < 0)
                return new(FilePreviewStatus.Unsupported, "file_preview.video_type_failed", null, [hrType]);

            uint width = 0, height = 0;
            int defaultStride = 0;
            hr = Native.IMFSourceReader_GetCurrentMediaType(reader, videoStream, out var currentType);
            try
            {
                if (hr >= 0 && currentType != IntPtr.Zero)
                {
                    Native.MFGetAttributeSize(currentType, Native.MF_MT_FRAME_SIZE, out width, out height);
                    Native.IMFAttributes_GetGUID(currentType, Native.MF_MT_SUBTYPE, out outputSubtype);
                    // MF_MT_DEFAULT_STRIDE
                    var strideKey = new Guid("644b4e48-1eeb-4faa-9aa1-6db5ad0c1a38");
                    if (Native.IMFAttributes_GetUINT32(currentType, strideKey, out var strideU) >= 0)
                        defaultStride = unchecked((int)strideU);
                }
            }
            finally
            {
                if (currentType != IntPtr.Zero)
                    Marshal.Release(currentType);
            }

            if (width == 0 || height == 0)
                return new(FilePreviewStatus.Unsupported, "file_preview.unknown_frame_size", null);
            if ((long)width * height > MaxSourcePixels)
                return new(FilePreviewStatus.ResourceLimited, "file_preview.video_pixel_limit", null);

            var started = Environment.TickCount64;
            for (var calls = 0; calls < MaxReadSampleCalls; calls++)
            {
                if (token.IsCancellationRequested)
                    return new(FilePreviewStatus.Canceled, "status.canceled", null);

                if (Environment.TickCount64 - started > MaxElapsed.TotalMilliseconds)
                    return new(FilePreviewStatus.ResourceLimited, "file_preview.video_time_budget", null);

                hr = Native.IMFSourceReader_ReadSample(
                    reader,
                    videoStream,
                    0,
                    out _,
                    out var flags,
                    out _,
                    out var sample
                );

                if (hr < 0)
                    return new(FilePreviewStatus.Unavailable, "file_preview.read_sample_failed", null, [hr]);

                if ((flags & Native.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    if (sample != IntPtr.Zero)
                        Marshal.Release(sample);
                    return new(FilePreviewStatus.Unsupported, "file_preview.no_video_sample", null);
                }

                if ((flags & Native.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                {
                    if (Native.IMFSourceReader_GetCurrentMediaType(reader, videoStream, out var mt) >= 0 && mt != IntPtr.Zero)
                    {
                        try
                        {
                            Native.MFGetAttributeSize(mt, Native.MF_MT_FRAME_SIZE, out width, out height);
                            if (width == 0 || height == 0 || (long)width * height > MaxSourcePixels)
                            {
                                if (sample != IntPtr.Zero)
                                    Marshal.Release(sample);
                                return new(FilePreviewStatus.ResourceLimited, "file_preview.frame_size_after_change", null);
                            }
                        }
                        finally
                        {
                            Marshal.Release(mt);
                        }
                    }
                }

                if (sample == IntPtr.Zero)
                    continue;

                try
                {
                    var png = SampleToPng(sample, width, height, defaultStride, outputSubtype, token);
                    if (png is null)
                        return new(FilePreviewStatus.Unavailable, "file_preview.encode_frame_failed", null);
                    return new(FilePreviewStatus.Ready, null, png);
                }
                finally
                {
                    Marshal.Release(sample);
                }
            }

            return new(FilePreviewStatus.ResourceLimited, "file_preview.read_sample_budget", null);
        }
        catch (OperationCanceledException)
        {
            return new(FilePreviewStatus.Canceled, "status.canceled", null);
        }
        catch (Exception ex)
        {
            return new(FilePreviewStatus.Unavailable, ex.Message, null);
        }
        finally
        {
            if (reader != IntPtr.Zero)
                Marshal.Release(reader);
            if (byteStream != IntPtr.Zero)
                Marshal.Release(byteStream);
            managedStream?.Dispose();
            dup?.Dispose();
            if (mfStarted)
                Native.MFShutdown();
            if (weInitializedCom)
                Native.CoUninitialize();
            GC.KeepAlive(istreamRoot);
        }
    }

    private static int FindFirstVideoStream(IntPtr reader)
    {
        for (int i = 0; i < 64; i++)
        {
            var hr = Native.IMFSourceReader_GetNativeMediaType(reader, i, 0, out var mt);
            if (unchecked((uint)hr) == 0xC00D36B7) // MF_E_INVALIDSTREAMNUMBER
                break;
            if (hr < 0 || mt == IntPtr.Zero)
                continue;
            try
            {
                if (Native.IMFAttributes_GetGUID(mt, Native.MF_MT_MAJOR_TYPE, out var major) >= 0
                    && major == Native.MFMediaType_Video)
                {
                    return i;
                }
            }
            finally
            {
                Marshal.Release(mt);
            }
        }

        // Prefer first-video constant as fallback.
        if (Native.IMFSourceReader_GetNativeMediaType(reader, Native.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, out var fmt) >= 0
            && fmt != IntPtr.Zero)
        {
            Marshal.Release(fmt);
            return Native.MF_SOURCE_READER_FIRST_VIDEO_STREAM;
        }

        return -1;
    }

    private static int TrySetOutputSubtype(IntPtr reader, int videoStream, Guid subtype)
    {
        var hr = Native.MFCreateMediaType(out var mediaType);
        if (hr < 0 || mediaType == IntPtr.Zero)
            return hr;
        try
        {
            Native.IMFAttributes_SetGUID(mediaType, Native.MF_MT_MAJOR_TYPE, Native.MFMediaType_Video);
            Native.IMFAttributes_SetGUID(mediaType, Native.MF_MT_SUBTYPE, subtype);
            return Native.IMFSourceReader_SetCurrentMediaType(reader, videoStream, IntPtr.Zero, mediaType);
        }
        finally
        {
            Marshal.Release(mediaType);
        }
    }

    private static byte[]? SampleToPng(IntPtr sample, uint width, uint height, int defaultStride, Guid subtype, CancellationToken token)
    {
        var hr = Native.IMFSample_ConvertToContiguousBuffer(sample, out var buffer);
        if (hr < 0 || buffer == IntPtr.Zero)
            return null;

        try
        {
            hr = Native.IMFMediaBuffer_Lock(buffer, out var scan0, out _, out var currentLength);
            if (hr < 0 || scan0 == IntPtr.Zero)
                return null;

            try
            {
                byte[]? bgra;
                int rows = (int)height;
                if (subtype == Native.MFVideoFormat_RGB32)
                {
                    var stride = checked((int)width * 4);
                    if (currentLength < stride)
                        return null;
                    rows = Math.Min((int)height, currentLength / stride);
                    if (rows <= 0)
                        return null;
                    bgra = new byte[checked((int)width * rows * 4)];
                    for (int y = 0; y < rows; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        var srcRow = IntPtr.Add(scan0, y * stride);
                        var dstOffset = y * stride;
                        Marshal.Copy(srcRow, bgra, dstOffset, stride);
                        for (int x = 0; x < (int)width; x++)
                            bgra[dstOffset + x * 4 + 3] = 0xFF;
                    }
                }
                else if (subtype == Native.MFVideoFormat_NV12)
                {
                    var yStride = defaultStride != 0 ? Math.Abs(defaultStride) : (((int)width + 15) & ~15);
                    var minLen = yStride * (int)height + yStride * (((int)height + 1) / 2);
                    if (currentLength < minLen)
                        return null;
                    bgra = ConvertNv12ToBgra(scan0, (int)width, (int)height, yStride);
                }
                else if (subtype == Native.MFVideoFormat_YUY2)
                {
                    var stride = defaultStride != 0 ? Math.Abs(defaultStride) : checked((int)width * 2);
                    if (currentLength < stride * (int)height)
                        return null;
                    bgra = ConvertYuy2ToBgra(scan0, (int)width, (int)height, stride);
                }
                else
                {
                    return null;
                }

                return EncodeBgraPng(bgra, (int)width, rows);
            }
            finally
            {
                Native.IMFMediaBuffer_Unlock(buffer);
            }
        }
        finally
        {
            Marshal.Release(buffer);
        }
    }

    private static byte[] ConvertNv12ToBgra(IntPtr src, int width, int height, int yStride)
    {
        var bgra = new byte[width * height * 4];
        var uvPtr = IntPtr.Add(src, yStride * height);
        for (int y = 0; y < height; y++)
        {
            var yRow = IntPtr.Add(src, y * yStride);
            var uvRow = IntPtr.Add(uvPtr, (y / 2) * yStride);
            for (int x = 0; x < width; x++)
            {
                int Y = Marshal.ReadByte(yRow, x);
                int U = Marshal.ReadByte(uvRow, x & ~1) - 128;
                int V = Marshal.ReadByte(uvRow, (x & ~1) + 1) - 128;
                WriteYuvPixel(bgra, y * width + x, Y, U, V);
            }
        }
        return bgra;
    }

    private static byte[] ConvertYuy2ToBgra(IntPtr src, int width, int height, int stride)
    {
        var bgra = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            var row = IntPtr.Add(src, y * stride);
            for (int x = 0; x < width; x += 2)
            {
                int y0 = Marshal.ReadByte(row, x * 2);
                int u = Marshal.ReadByte(row, x * 2 + 1) - 128;
                int y1 = Marshal.ReadByte(row, x * 2 + 2);
                int v = Marshal.ReadByte(row, x * 2 + 3) - 128;
                WriteYuvPixel(bgra, y * width + x, y0, u, v);
                if (x + 1 < width)
                    WriteYuvPixel(bgra, y * width + x + 1, y1, u, v);
            }
        }
        return bgra;
    }

    /// <summary>BT.601-ish YUV to opaque BGRA.</summary>
    private static void WriteYuvPixel(byte[] bgra, int pixelIndex, int Y, int U, int V)
    {
        int C = Y - 16;
        if (C < 0)
            C = 0;
        int r = (298 * C + 409 * V + 128) >> 8;
        int g = (298 * C - 100 * U - 208 * V + 128) >> 8;
        int b = (298 * C + 516 * U + 128) >> 8;
        var i = pixelIndex * 4;
        bgra[i] = (byte)Math.Clamp(b, 0, 255);
        bgra[i + 1] = (byte)Math.Clamp(g, 0, 255);
        bgra[i + 2] = (byte)Math.Clamp(r, 0, 255);
        bgra[i + 3] = 0xFF;
    }

    private static byte[]? EncodeBgraPng(byte[] bgra, int width, int height)
    {
        var longest = Math.Max(width, height);
        int outW = width;
        int outH = height;
        byte[] pixels = bgra;

        if (longest > MaxPreviewLongEdge)
        {
            var scale = (double)MaxPreviewLongEdge / longest;
            outW = Math.Max(1, (int)Math.Round(width * scale));
            outH = Math.Max(1, (int)Math.Round(height * scale));
            pixels = NearestNeighborScale(bgra, width, height, outW, outH);
        }

        try
        {
            using var outMs = new InMemoryRandomAccessStream();
            var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outMs).AsTask().GetAwaiter().GetResult();
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)outW,
                (uint)outH,
                96,
                96,
                pixels
            );
            encoder.FlushAsync().AsTask().GetAwaiter().GetResult();

            var size = (int)outMs.Size;
            if (size <= 0)
                return null;
            var bytes = new byte[size];
            outMs.Seek(0);
            outMs.ReadAsync(bytes.AsBuffer(), (uint)size, InputStreamOptions.None).AsTask().GetAwaiter().GetResult();
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] NearestNeighborScale(byte[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new byte[dw * dh * 4];
        for (int y = 0; y < dh; y++)
        {
            var sy = Math.Min(sh - 1, y * sh / dh);
            for (int x = 0; x < dw; x++)
            {
                var sx = Math.Min(sw - 1, x * sw / dw);
                var si = (sy * sw + sx) * 4;
                var di = (y * dw + x) * 4;
                dst[di] = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        return dst;
    }

    /// <summary>ComTypes.IStream wrapper over a seekable .NET stream (handle-backed FileStream).</summary>
    private sealed class ComStreamOverStream : System.Runtime.InteropServices.ComTypes.IStream
    {
        private readonly Stream _stream;

        public ComStreamOverStream(Stream stream) => _stream = stream;

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            var read = _stream.Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero)
                Marshal.WriteInt32(pcbRead, read);
        }

        public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw new NotSupportedException();

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            var origin = dwOrigin switch
            {
                0 => SeekOrigin.Begin,
                1 => SeekOrigin.Current,
                2 => SeekOrigin.End,
                _ => SeekOrigin.Begin,
            };
            var pos = _stream.Seek(dlibMove, origin);
            if (plibNewPosition != IntPtr.Zero)
                Marshal.WriteInt64(plibNewPosition, pos);
        }

        public void SetSize(long libNewSize) => throw new NotSupportedException();

        public void CopyTo(System.Runtime.InteropServices.ComTypes.IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) =>
            throw new NotSupportedException();

        public void Commit(int grfCommitFlags) { }

        public void Revert() { }

        public void LockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();

        public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();

        public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG
            {
                type = 2,
                cbSize = _stream.Length,
            };
        }

        public void Clone(out System.Runtime.InteropServices.ComTypes.IStream ppstm) => throw new NotSupportedException();
    }

    private static class Native
    {
        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
        public const uint COINIT_MULTITHREADED = 0x0;
        public const uint MF_VERSION = 0x00020070;
        public const uint MFSTARTUP_FULL = 0;
        public const uint DUPLICATE_SAME_ACCESS = 0x00000002;

        public const int MF_SOURCE_READER_ALL_STREAMS = unchecked((int)0xFFFFFFFE);
        public const int MF_SOURCE_READER_FIRST_VIDEO_STREAM = unchecked((int)0xFFFFFFFC);
        public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x2;
        public const uint MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED = 0x20;

        public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_NV12 = new("3231564E-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_YUY2 = new("32595559-0000-0010-8000-00AA00389B71");
        public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d237dc");

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("mfplat.dll")]
        public static extern int MFStartup(uint version, uint dwFlags);

        [DllImport("mfplat.dll")]
        public static extern int MFShutdown();

        [DllImport("mfplat.dll")]
        public static extern int MFCreateMediaType(out IntPtr ppMFType);

        [DllImport("mfplat.dll")]
        public static extern int MFCreateAttributes(out IntPtr ppMFAttributes, uint cInitialSize);

        [DllImport("mfplat.dll")]
        public static extern int MFCreateMFByteStreamOnStream(IntPtr pStream, out IntPtr ppByteStream);

        [DllImport("mfreadwrite.dll")]
        public static extern int MFCreateSourceReaderFromByteStream(
            IntPtr pByteStream,
            IntPtr pAttributes,
            out IntPtr ppSourceReader
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwOptions
        );

        public static int IMFByteStream_SetCurrentPosition(IntPtr bs, ulong pos)
        {
            var fn = GetVtblFn<SetPos64>(bs, 7);
            return fn(bs, pos);
        }

        public static int IMFSourceReader_SetStreamSelection(IntPtr reader, int streamIndex, bool selected)
        {
            var fn = GetVtblFn<SetStreamSel>(reader, 4);
            return fn(reader, streamIndex, selected ? 1 : 0);
        }

        public static int IMFSourceReader_GetNativeMediaType(IntPtr reader, int streamIndex, int mediaTypeIndex, out IntPtr mediaType)
        {
            var fn = GetVtblFn<GetNativeMt>(reader, 5);
            return fn(reader, streamIndex, mediaTypeIndex, out mediaType);
        }

        public static int IMFSourceReader_GetCurrentMediaType(IntPtr reader, int streamIndex, out IntPtr mediaType)
        {
            var fn = GetVtblFn<GetCurrentMt>(reader, 6);
            return fn(reader, streamIndex, out mediaType);
        }

        public static int IMFSourceReader_SetCurrentMediaType(IntPtr reader, int streamIndex, IntPtr reserved, IntPtr mediaType)
        {
            var fn = GetVtblFn<SetCurrentMt>(reader, 7);
            return fn(reader, streamIndex, reserved, mediaType);
        }

        public static int IMFSourceReader_ReadSample(
            IntPtr reader,
            int streamIndex,
            uint controlFlags,
            out int actualStreamIndex,
            out uint streamFlags,
            out ulong timestamp,
            out IntPtr sample
        )
        {
            var fn = GetVtblFn<ReadSampleFn>(reader, 9);
            return fn(reader, streamIndex, controlFlags, out actualStreamIndex, out streamFlags, out timestamp, out sample);
        }

        public static int IMFAttributes_SetGUID(IntPtr attrs, Guid key, Guid value)
        {
            var k = key;
            var v = value;
            var fn = GetVtblFn<SetGuidFn>(attrs, 24);
            return fn(attrs, ref k, ref v);
        }

        public static int IMFAttributes_SetUINT32(IntPtr attrs, Guid key, uint value)
        {
            var k = key;
            var fn = GetVtblFn<SetUInt32Fn>(attrs, 21);
            return fn(attrs, ref k, value);
        }

        public static int IMFAttributes_GetUINT32(IntPtr attrs, Guid key, out uint value)
        {
            var k = key;
            var fn = GetVtblFn<GetUInt32Fn>(attrs, 7);
            return fn(attrs, ref k, out value);
        }

        public static int IMFAttributes_GetGUID(IntPtr attrs, Guid key, out Guid value)
        {
            var k = key;
            var fn = GetVtblFn<GetGuidFn>(attrs, 10);
            return fn(attrs, ref k, out value);
        }

        public static int IMFAttributes_GetUINT64(IntPtr attrs, Guid key, out ulong value)
        {
            var k = key;
            var fn = GetVtblFn<GetUInt64Fn>(attrs, 8);
            return fn(attrs, ref k, out value);
        }

        public static void MFGetAttributeSize(IntPtr mt, Guid key, out uint width, out uint height)
        {
            IMFAttributes_GetUINT64(mt, key, out var packed);
            width = (uint)(packed >> 32);
            height = (uint)(packed & 0xffffffff);
        }

        public static int IMFSample_ConvertToContiguousBuffer(IntPtr sample, out IntPtr buffer)
        {
            var fn = GetVtblFn<ConvertBufFn>(sample, 41);
            return fn(sample, out buffer);
        }

        public static int IMFMediaBuffer_Lock(IntPtr buffer, out IntPtr data, out int maxLen, out int curLen)
        {
            var fn = GetVtblFn<LockFn>(buffer, 3);
            return fn(buffer, out data, out maxLen, out curLen);
        }

        public static int IMFMediaBuffer_Unlock(IntPtr buffer)
        {
            var fn = GetVtblFn<UnlockFn>(buffer, 4);
            return fn(buffer);
        }

        private static T GetVtblFn<T>(IntPtr obj, int slot) where T : Delegate
        {
            var vtable = Marshal.ReadIntPtr(obj);
            var fnPtr = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(fnPtr);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetPos64(IntPtr bs, ulong pos);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetStreamSel(IntPtr r, int idx, int sel);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetNativeMt(IntPtr r, int stream, int mtIndex, out IntPtr mt);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetCurrentMt(IntPtr r, int stream, out IntPtr mt);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetCurrentMt(IntPtr r, int stream, IntPtr reserved, IntPtr mt);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReadSampleFn(
            IntPtr r,
            int stream,
            uint flags,
            out int actual,
            out uint streamFlags,
            out ulong ts,
            out IntPtr sample
        );

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetGuidFn(IntPtr a, ref Guid key, ref Guid value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetUInt32Fn(IntPtr a, ref Guid key, uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetGuidFn(IntPtr a, ref Guid key, out Guid value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetUInt32Fn(IntPtr a, ref Guid key, out uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetUInt64Fn(IntPtr a, ref Guid key, out ulong value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ConvertBufFn(IntPtr sample, out IntPtr buffer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LockFn(IntPtr buffer, out IntPtr data, out int max, out int cur);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int UnlockFn(IntPtr buffer);
    }
}
