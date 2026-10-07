using System.Numerics;
using System.Text;
using Clipcy.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;

namespace Clipcy.Services;

public class PreviewService : IPreviewService
{
    private readonly Window _window;
    private readonly Grid _rootGrid;
    private readonly IWindowService _windowService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly RustBridgeService? _bridge;
    private bool _showPreviewPanel;

    private DispatcherTimer? _previewDelayTimer;
    private DispatcherTimer? _hideTimer;
    private PreviewData? _pendingPreviewData;
    private FrameworkElement? _previewTarget;
    private long _pendingShowGeneration;
    private long _scheduledShowGeneration;
    private long _hideGeneration;

    private Popup? _previewPopup;
    private PreviewControl? _previewControl;
    private PreviewData? _currentPreviewData;
    private bool _isPointerOverPreview = false;
    private bool _disposed;

    private long _popupRequestId;
    private long _fileRequestId;
    private CancellationTokenSource? _fileLoadCts;
    private bool _positioningPreview;

    public PreviewService(Window window, Grid rootGrid, IWindowService windowService)
    {
        _window = window;
        _rootGrid = rootGrid;
        _windowService = windowService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _bridge = App.Bridge;
        _showPreviewPanel = _bridge?.GetSettings().ShowPreviewPanel ?? true;
        if (_bridge != null)
            _bridge.OnPreviewPanelChanged += OnPreviewPanelChanged;
        _rootGrid.ActualThemeChanged += OnRootActualThemeChanged;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _previewDelayTimer?.Stop();
        _hideTimer?.Stop();
        _pendingPreviewData = null;
        _pendingShowGeneration++;
        CancelFileLoad();
        _rootGrid.ActualThemeChanged -= OnRootActualThemeChanged;
        if (_bridge != null)
            _bridge.OnPreviewPanelChanged -= OnPreviewPanelChanged;
        if (_previewPopup != null)
        {
            _previewPopup.IsOpen = false;
        }

        _currentPreviewData = null;
    }

    private Popup PreviewPopup
    {
        get
        {
            if (_previewPopup == null)
            {
                _previewPopup = CreatePreviewPopup();
            }
            return _previewPopup;
        }
    }

    private Popup CreatePreviewPopup()
    {
        var popup = new Popup
        {
            IsLightDismissEnabled = false,
            ShouldConstrainToRootBounds = false,
            XamlRoot = _rootGrid?.XamlRoot,
            SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
        };
        SynchronizePreviewTheme(popup);

        _previewControl = new PreviewControl();

        _previewControl.PointerEntered += (s, e) =>
        {
            _isPointerOverPreview = true;
            _hideTimer?.Stop();
            // Entering popup cancels obsolete pending replacements.
            _pendingPreviewData = null;
            _pendingShowGeneration++;
            _previewDelayTimer?.Stop();
        };

        _previewControl.PointerExited += (s, e) =>
        {
            _isPointerOverPreview = false;
            ScheduleHide();
        };

        _previewControl.SelectedFileIndexChanged += OnSelectedFileIndexChanged;
        _previewControl.SizeChanged += (_, _) =>
        {
            if (_disposed || _previewPopup is not { IsOpen: true })
                return;
            PositionPreviewPopup();
        };

        popup.Child = _previewControl;

        popup.Opened += (s, e) =>
        {
            if (_previewControl.Content is FrameworkElement rootBorder)
            {
                rootBorder.Opacity = 0;
                rootBorder.Translation = new Vector3(-10, 0, 32);

                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };

                var storyboard = new Storyboard();
                Storyboard.SetTarget(fadeIn, rootBorder);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                storyboard.Children.Add(fadeIn);
                storyboard.Begin();

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                var elapsed = 0.0;
                timer.Tick += (s2, e2) =>
                {
                    if (_disposed)
                    {
                        timer.Stop();
                        return;
                    }

                    elapsed += 16;
                    var progress = Math.Min(1.0, elapsed / 150.0);
                    var eased = 1 - Math.Pow(1 - progress, 2);
                    rootBorder.Translation = new Vector3((float)(-10 * (1 - eased)), 0, 32);

                    if (progress >= 1.0)
                        timer.Stop();
                };
                timer.Start();
            }
        };

        return popup;
    }

    public void ScheduleShow(PreviewData data, FrameworkElement target)
    {
        if (_disposed)
            return;

        long hideGeneration = _hideGeneration;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || hideGeneration != _hideGeneration)
                return;

            if (!_showPreviewPanel)
            {
                HidePreviewInternal();
                return;
            }

            _hideTimer?.Stop();

            // Same item already open: keep it; do not restart delay.
            if (_currentPreviewData?.Id == data.Id && _previewPopup != null && _previewPopup.IsOpen)
                return;

            // Invalidate any pending different-item show before scheduling this one.
            _pendingShowGeneration++;
            var generation = _pendingShowGeneration;

            _pendingPreviewData = data;
            _previewTarget = target;

            if (_previewDelayTimer == null)
            {
                _previewDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _previewDelayTimer.Tick += PreviewDelayTimer_Tick;
            }

            _previewDelayTimer.Stop();
            _scheduledShowGeneration = generation;
            _previewDelayTimer.Start();
        });
    }

    public void ScheduleHide()
    {
        if (_disposed)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
                return;

            if (_hideTimer == null)
            {
                _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _hideTimer.Tick += (s, e) =>
                {
                    _hideTimer?.Stop();
                    if (_disposed)
                        return;
                    if (!_isPointerOverPreview)
                        HidePreviewInternal();
                };
            }

            _hideTimer.Stop();
            _hideTimer.Start();
        });
    }

    public void Hide()
    {
        if (_disposed)
            return;
        if (_dispatcherQueue.HasThreadAccess)
        {
            HidePreviewInternal();
            return;
        }
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
                return;
            HidePreviewInternal();
        });
    }

    private void HidePreviewInternal()
    {
        _hideGeneration++;
        _previewDelayTimer?.Stop();
        _hideTimer?.Stop();
        _pendingPreviewData = null;
        _pendingShowGeneration++;
        CancelFileLoad();
        if (_previewPopup != null)
        {
            _previewPopup.IsOpen = false;
        }
        _currentPreviewData = null;
    }

    private void PreviewDelayTimer_Tick(object? sender, object e)
    {
        _previewDelayTimer?.Stop();
        if (_disposed)
            return;

        if (_scheduledShowGeneration != _pendingShowGeneration)
            return;

        if (_pendingPreviewData != null)
        {
            var data = _pendingPreviewData;
            _pendingPreviewData = null;
            ShowPreviewInternal(data);
        }
    }

    private void ShowPreviewInternal(PreviewData data)
    {
        if (_disposed)
            return;

        if (!_showPreviewPanel)
        {
            HidePreviewInternal();
            return;
        }

        if (_currentPreviewData?.Id == data.Id && PreviewPopup.IsOpen && ReferenceEquals(_currentPreviewData, data))
            return;

        try
        {
            _previewDelayTimer?.Stop();
            _hideTimer?.Stop();
            CancelFileLoad();

            var popup = PreviewPopup;
            SynchronizePreviewTheme(popup);
            if (_previewControl == null)
                return;

            _popupRequestId++;
            var popupId = _popupRequestId;
            _currentPreviewData = data;

            // Clear previous file preview state on the model when rebinding.
            if (data.PreviewType == "Files")
            {
                data.FilePreview = null;
                data.IsFilePreviewLoading = data.LoadFullData != null;
                data.SelectedFileIndex = 0;
            }

            _previewControl.PreviewData = data;

            if (data.LoadFullData != null)
            {
                _ = LoadFullDataAsync(data, popupId);
            }

            if (_previewTarget != null)
            {
                PositionPreviewPopup();
                popup.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowPreview error: {ex.Message}");
            HidePreviewInternal();
        }
    }

    // Matches PreviewControl RootBorder.MaxHeight. Reserve this while async content
    // (file/image decode) can still grow the pane downward.
    private const double PreviewMaxHeight = 400;

    private void PositionPreviewPopup()
    {
        if (_positioningPreview || _disposed)
            return;

        var popup = _previewPopup;
        var target = _previewTarget;
        var control = _previewControl;
        if (popup == null || target == null || control == null || target.XamlRoot == null)
            return;

        _positioningPreview = true;
        try
        {
            popup.XamlRoot = target.XamlRoot;

            control.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var popupWidth = control.DesiredSize.Width;
            var popupHeight = Math.Max(control.DesiredSize.Height, control.ActualHeight);
            if (popupWidth <= 0)
                popupWidth = 360;
            if (popupHeight <= 0)
                popupHeight = 260;
            if (PreviewMayGrow(_currentPreviewData))
                popupHeight = Math.Max(popupHeight, PreviewMaxHeight);

            var transform = target.TransformToVisual(null);
            var targetPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            double dpiScale = _windowService.GetDpiScale();
            if (dpiScale <= 0)
                dpiScale = 1;

            var appWindow = _windowService.AppWindow;
            double winX = appWindow.Position.X;
            double winY = appWindow.Position.Y;

            double targetPhysicalX = winX + (targetPos.X * dpiScale);
            double targetPhysicalY = winY + (targetPos.Y * dpiScale);
            double targetPhysicalWidth = target.ActualWidth * dpiScale;
            double popupPhysicalWidth = popupWidth * dpiScale;
            double popupPhysicalHeight = popupHeight * dpiScale;
            double spacingPhysical = 10.0 * dpiScale;

            if (
                !_windowService.TryGetWorkArea((int)targetPhysicalX, (int)targetPhysicalY, out var workArea)
            )
            {
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    appWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Nearest
                );
                workArea = displayArea.WorkArea;
            }

            double finalPhysicalX = targetPhysicalX + targetPhysicalWidth + spacingPhysical;
            double finalPhysicalY = targetPhysicalY;
            double workRight = workArea.X + workArea.Width;
            double workBottom = workArea.Y + workArea.Height;

            if (finalPhysicalX + popupPhysicalWidth > workRight)
            {
                double leftTry = targetPhysicalX - popupPhysicalWidth - spacingPhysical;
                finalPhysicalX = leftTry >= workArea.X ? leftTry : workRight - popupPhysicalWidth - spacingPhysical;
            }
            if (finalPhysicalX < workArea.X)
                finalPhysicalX = workArea.X + spacingPhysical;

            if (finalPhysicalY + popupPhysicalHeight > workBottom)
                finalPhysicalY = workBottom - popupPhysicalHeight - spacingPhysical;
            if (finalPhysicalY < workArea.Y)
                finalPhysicalY = workArea.Y + spacingPhysical;

            popup.HorizontalOffset = (finalPhysicalX - winX) / dpiScale;
            popup.VerticalOffset = (finalPhysicalY - winY) / dpiScale;
        }
        finally
        {
            _positioningPreview = false;
        }
    }

    private static bool PreviewMayGrow(PreviewData? data)
    {
        if (data == null)
            return false;
        if (data.PreviewType == "Files")
            return data.IsFilePreviewLoading || data.FilePreview == null;
        if (data.PreviewType == "Image")
            return data.ImageBytes is not { Length: > 0 } && data.Thumbnail is not { Length: > 0 };
        return false;
    }

    private void OnSelectedFileIndexChanged(object? sender, int index)
    {
        if (_disposed || _currentPreviewData is null)
            return;
        if (_currentPreviewData.PreviewType != "Files")
            return;
        if (_currentPreviewData.FilePaths is null || index < 0 || index >= _currentPreviewData.FilePaths.Count)
            return;
        if (_currentPreviewData.SelectedFileIndex == index && _currentPreviewData.FilePreview is not null)
            return;

        var data = _currentPreviewData;
        var popupId = _popupRequestId;
        data.SelectedFileIndex = index;
        data.FilePreview = null;
        data.IsFilePreviewLoading = true;
        _previewControl?.Refresh();

        _ = LoadSelectedFileAsync(data, popupId, index);
    }

    private void CancelFileLoad()
    {
        _fileRequestId++;
        var cts = _fileLoadCts;
        _fileLoadCts = null;
        if (cts is null)
            return;

        // Request cancellation without running I/O cancellation callbacks on the UI thread.
        _ = CancelAndDisposeAsync(cts);
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource cts)
    {
        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // Cleanup must still release the source if a cancellation callback fails.
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task LoadFullDataAsync(PreviewData data, long popupId)
    {
        try
        {
            byte[]? fullData = null;
            try
            {
                fullData = await data.LoadFullData!();
            }
            catch
            {
                fullData = null;
            }

            if (_disposed || popupId != _popupRequestId || !ReferenceEquals(_currentPreviewData, data))
                return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_disposed || popupId != _popupRequestId || !ReferenceEquals(_currentPreviewData, data))
                    return;

                if (fullData is null || fullData.Length == 0)
                {
                    if (data.PreviewType == "Files")
                    {
                        data.IsFilePreviewLoading = false;
                        data.FilePaths = Array.Empty<string>();
                        data.FilePreview = UnavailableFileResult("File paths unavailable");
                        _previewControl?.Refresh();
                    }
                    return;
                }


                switch (data.PreviewType)
                {
                    case "Image":
                        data.ImageBytes = fullData;
                        break;
                    case "RichText":
                        var rtf = Encoding.UTF8.GetString(fullData);
                        data.RtfContent = rtf;
                        data.TextContent = rtf;
                        break;
                    case "Text":
                        data.TextContent = Encoding.UTF8.GetString(fullData);
                        break;
                    case "Html":
                        var html = Encoding.UTF8.GetString(fullData);
                        data.TextContent = ExtractHtmlFragment(fullData) ?? html;
                        break;
                    case "Files":
                        ApplyFilesPayload(data, fullData, popupId);
                        return; // ApplyFilesPayload refreshes and starts file load
                }

                if (_previewControl != null)
                    _previewControl.Refresh();
            });
        }
        catch
        {
            // discard
        }
    }

    private void ApplyFilesPayload(PreviewData data, byte[] fullData, long popupId)
    {
        var paths = ParseFilePaths(fullData);
        data.TextContent = Encoding.UTF8.GetString(fullData);

        if (paths.Count == 0)
        {
            data.IsFilePreviewLoading = false;
            data.FilePaths = Array.Empty<string>();
            data.FilePreview = UnavailableFileResult("file_preview.empty_path_list");
            _previewControl?.Refresh();
            return;
        }

        data.FilePaths = paths;
        data.SelectedFileIndex = 0;
        data.IsFilePreviewLoading = true;
        data.FilePreview = null;
        _previewControl?.Refresh();

        _ = LoadSelectedFileAsync(data, popupId, 0);
    }

    /// <summary>
    /// Decode UTF-8 path list: split LF, strip optional CR, drop empty entries, preserve order/spelling.
    /// </summary>
    internal static IReadOnlyList<string> ParseFilePaths(byte[] fullData)
    {
        var text = Encoding.UTF8.GetString(fullData);
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        var lines = text.Split('\n');
        var list = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var path = line.EndsWith('\r') ? line[..^1] : line;
            if (path.Length == 0)
                continue;
            list.Add(path);
        }

        return list;
    }

    private async Task LoadSelectedFileAsync(PreviewData data, long popupId, int selectedIndex)
    {
        CancelFileLoad();
        var fileReq = _fileRequestId;
        var cts = new CancellationTokenSource();
        _fileLoadCts = cts;
        var token = cts.Token;

        try
        {
            if (data.FilePaths is null || selectedIndex < 0 || selectedIndex >= data.FilePaths.Count)
            {
                PublishFileResult(
                    data,
                    popupId,
                    fileReq,
                    selectedIndex,
                    UnavailableFileResult("file_preview.invalid_selection")
                );
                return;
            }


            var path = data.FilePaths[selectedIndex];
            FilePreviewResult result;
            try
            {
                // Path admission and codec setup perform synchronous I/O before their first await.
                result = await Task.Run(() => FilePreviewLoader.LoadAsync(path, token), token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                result = new FilePreviewResult
                {
                    OriginalPath = path,
                    DisplayName = System.IO.Path.GetFileName(path),
                    Kind = FilePreviewKind.Other,
                    Status = FilePreviewStatus.Unavailable,
                    StatusMessage = ex.Message,
                };
            }


            if (cts.IsCancellationRequested || fileReq != _fileRequestId)
                return;

            PublishFileResult(data, popupId, fileReq, selectedIndex, result);
        }
        finally
        {
            if (ReferenceEquals(_fileLoadCts, cts))
            {
                _fileLoadCts = null;
                cts.Dispose();
            }
        }
    }

    private void PublishFileResult(
        PreviewData data,
        long popupId,
        long fileReq,
        int selectedIndex,
        FilePreviewResult result
    )
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed
                || popupId != _popupRequestId
                || fileReq != _fileRequestId
                || !ReferenceEquals(_currentPreviewData, data)
                || data.SelectedFileIndex != selectedIndex)
            {
                return;
            }

            data.FilePreview = result;
            data.IsFilePreviewLoading = false;
            _previewControl?.Refresh();
        });
    }

    private static FilePreviewResult UnavailableFileResult(string message) =>
        new()
        {
            OriginalPath = string.Empty,
            DisplayName = string.Empty,
            Kind = FilePreviewKind.Other,
            Status = FilePreviewStatus.Unavailable,
            StatusMessage = message,
        };


    private void OnPreviewPanelChanged(bool showPreviewPanel)
    {
        if (_disposed)
            return;

        _showPreviewPanel = showPreviewPanel;
        if (!showPreviewPanel)
            HidePreviewInternal();
    }

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_disposed || _previewPopup == null)
            return;

        SynchronizePreviewTheme(_previewPopup);
    }

    private void SynchronizePreviewTheme(Popup popup)
    {
        var theme = _rootGrid.ActualTheme;
        if (popup.RequestedTheme != theme)
            popup.RequestedTheme = theme;
    }

    private static string? ExtractHtmlFragment(byte[] data)
    {
        if (data.Length == 0)
            return null;

        var text = Encoding.UTF8.GetString(data);
        if (!IsHtmlClipboardFormat(text))
            return null;

        var fragment = TryExtractHtmlSpan(text, data, "StartFragment:", "EndFragment:");
        if (fragment != null)
            return fragment;

        return TryExtractHtmlSpan(text, data, "StartHTML:", "EndHTML:");
    }

    private static string? TryExtractHtmlSpan(string text, byte[] data, string startKey, string endKey)
    {
        var start = ParseHtmlOffset(text, startKey);
        var end = ParseHtmlOffset(text, endKey);
        if (start == null || end == null)
            return null;
        if (start >= end || end > data.Length)
            return null;

        return Encoding.UTF8.GetString(data, start.Value, end.Value - start.Value);
    }

    private static int? ParseHtmlOffset(string text, string key)
    {
        var index = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        index += key.Length;
        var end = index;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        if (end == index)
            return null;

        return int.TryParse(text.Substring(index, end - index), out var value) ? value : null;
    }

    private static bool IsHtmlClipboardFormat(string text) =>
        text.StartsWith("Version:", StringComparison.OrdinalIgnoreCase)
        && text.Contains("StartHTML:", StringComparison.OrdinalIgnoreCase)
        && text.Contains("EndHTML:", StringComparison.OrdinalIgnoreCase);
}
