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

    private DispatcherTimer? _previewDelayTimer;
    private DispatcherTimer? _hideTimer;
    private PreviewData? _pendingPreviewData;
    private FrameworkElement? _previewTarget;

    private Popup? _previewPopup;
    private PreviewControl? _previewControl;
    private PreviewData? _currentPreviewData;
    private bool _isPointerOverPreview = false;
    private bool _disposed;

    public PreviewService(Window window, Grid rootGrid, IWindowService windowService)
    {
        _window = window;
        _rootGrid = rootGrid;
        _windowService = windowService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _previewDelayTimer?.Stop();
        _hideTimer?.Stop();

        if (_previewPopup != null)
        {
            _previewPopup.IsOpen = false;
        }

        _disposed = true;
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
            RequestedTheme = _rootGrid?.ActualTheme ?? ElementTheme.Default, // Initial theme, though XAML handles updates usually
        };

        _previewControl = new PreviewControl();

        // Track pointer over the control to prevent auto-hide
        _previewControl.PointerEntered += (s, e) =>
        {
            _isPointerOverPreview = true;
            _hideTimer?.Stop();
        };

        _previewControl.PointerExited += (s, e) =>
        {
            _isPointerOverPreview = false;
            ScheduleHide();
        };

        popup.Child = _previewControl;

        // Animation logic
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

                // Simple manual translation animation
                // (Alternatively, could use Composition animations for smoother performance)
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                var elapsed = 0.0;
                timer.Tick += (s2, e2) =>
                {
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

        _dispatcherQueue.TryEnqueue(() =>
        {
            _hideTimer?.Stop();

            if (_currentPreviewData?.Id == data.Id && _previewPopup != null && _previewPopup.IsOpen)
                return;

            _pendingPreviewData = data;
            _previewTarget = target;

            if (_previewDelayTimer == null)
            {
                _previewDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _previewDelayTimer.Tick += PreviewDelayTimer_Tick;
            }

            _previewDelayTimer.Stop();
            _previewDelayTimer.Start();
        });
    }

    public void ScheduleHide()
    {
        if (_disposed)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_hideTimer == null)
            {
                _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _hideTimer.Tick += (s, e) =>
                {
                    _hideTimer?.Stop();
                    if (!_isPointerOverPreview)
                        Hide();
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
        _dispatcherQueue.TryEnqueue(() => HidePreviewInternal());
    }

    private void HidePreviewInternal()
    {
        _previewDelayTimer?.Stop();
        _hideTimer?.Stop();
        if (_previewPopup != null)
        {
            _previewPopup.IsOpen = false;
        }
        _currentPreviewData = null;
    }

    private void PreviewDelayTimer_Tick(object? sender, object e)
    {
        _previewDelayTimer?.Stop();
        if (_pendingPreviewData != null)
        {
            ShowPreviewInternal(_pendingPreviewData);
        }
    }

    private void ShowPreviewInternal(PreviewData data)
    {
        if (_currentPreviewData?.Id == data.Id && PreviewPopup.IsOpen)
            return;

        try
        {
            _previewDelayTimer?.Stop();
            _hideTimer?.Stop();

            var popup = PreviewPopup;
            if (_previewControl == null)
                return;

            _currentPreviewData = data;

            // Push data to the control
            _previewControl.PreviewData = data;

            // Handle Async full-data loading
            if (data.LoadFullData != null)
            {
                _ = LoadFullDataAsync(data);
            }

            // --- Positioning Logic ---
            if (_previewTarget != null)
            {
                popup.XamlRoot = _previewTarget.XamlRoot;

                // Ensure Layout Updated to get correct size
                _previewControl.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                var popupWidth = _previewControl.DesiredSize.Width;
                var popupHeight = _previewControl.DesiredSize.Height;
                if (popupWidth <= 0)
                    popupWidth = 360;
                if (popupHeight <= 0)
                    popupHeight = 260;

                var transform = _previewTarget.TransformToVisual(null);
                var targetPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                double dpiScale = _windowService.GetDpiScale();

                // Window/Monitor info (Physical)
                var appWindow = _windowService.AppWindow;
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    appWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Nearest
                );
                var workArea = displayArea.WorkArea;

                double winX = appWindow.Position.X;
                double winY = appWindow.Position.Y;

                double targetPhysicalX = winX + (targetPos.X * dpiScale);
                double targetPhysicalY = winY + (targetPos.Y * dpiScale);
                double targetPhysicalWidth = _previewTarget.ActualWidth * dpiScale;

                double popupPhysicalWidth = popupWidth * dpiScale;
                double popupPhysicalHeight = popupHeight * dpiScale;
                double spacingPhysical = 10.0 * dpiScale;

                // Default: Right of target
                double finalPhysicalX = targetPhysicalX + targetPhysicalWidth + spacingPhysical;
                double finalPhysicalY = targetPhysicalY;

                // Check Horizontal Fit
                if (finalPhysicalX + popupPhysicalWidth > workArea.X + workArea.Width)
                {
                    // Try Left
                    double leftTry = targetPhysicalX - popupPhysicalWidth - spacingPhysical;
                    finalPhysicalX =
                        (leftTry >= workArea.X)
                            ? leftTry
                            : (workArea.X + workArea.Width - popupPhysicalWidth - spacingPhysical);
                }

                // Check Vertical Fit
                if (finalPhysicalY + popupPhysicalHeight > workArea.Y + workArea.Height)
                {
                    finalPhysicalY = (workArea.Y + workArea.Height) - popupPhysicalHeight - spacingPhysical;
                }
                if (finalPhysicalY < workArea.Y)
                    finalPhysicalY = workArea.Y + spacingPhysical;

                popup.HorizontalOffset = (finalPhysicalX - winX) / dpiScale;
                popup.VerticalOffset = (finalPhysicalY - winY) / dpiScale;

                popup.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowPreview error: {ex.Message}");
            HidePreviewInternal();
        }
    }

    private async Task LoadFullDataAsync(PreviewData data)
    {
        try
        {
            var fullData = await data.LoadFullData!();
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_currentPreviewData?.Id != data.Id)
                    return;

                if (fullData != null && fullData.Length > 0)
                {
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
                        case "Html":
                        case "Files":
                            data.TextContent = Encoding.UTF8.GetString(fullData);
                            break;
                    }

                    if (_previewControl != null)
                        _previewControl.Refresh();
                }
            });
        }
        catch { }
    }
}
