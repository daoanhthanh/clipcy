using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

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
    private Microsoft.UI.Xaml.Controls.Primitives.Popup? _previewPopup;
    private FrameworkElement? _previewPopupRoot;
    private TextBlock? _previewContentType;
    private TextBlock? _previewText;
    private RichEditBox? _previewRichText;
    private Image? _previewImage;
    private Border? _previewImageBorder;
    private TextBlock? _previewMetadata;
    private bool _isPointerOverPreview = false;
    private PreviewData? _currentPreviewData;
    private bool _disposed;

    public PreviewService(Window window, Grid rootGrid, IWindowService windowService)
    {
        _window = window;
        _rootGrid = rootGrid;
        _windowService = windowService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ThemeManager.ThemeChanged -= OnThemeChanged;

        _previewDelayTimer?.Stop();
        _hideTimer?.Stop();

        if (_previewPopup != null)
        {
            _previewPopup.IsOpen = false;
        }

        _disposed = true;
    }

    private void OnThemeChanged(AppTheme theme)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                // Update theme on existing popup - faster than recreating
                // XAML will propagate theme to child elements automatically
                if (_previewPopup != null)
                {
                    var elementTheme = ThemeManager.GetElementTheme(theme);
                    _previewPopup.RequestedTheme = elementTheme;
                    
                    if (_previewPopupRoot is FrameworkElement root)
                    {
                        root.RequestedTheme = elementTheme;
                    }
                    
                    // For hardcoded colors, we need to recreate the popup
                    // But only if it's currently closed to avoid visual glitches
                    if (!_previewPopup.IsOpen)
                    {
                        _previewPopup = null;
                        _previewPopupRoot = null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnThemeChanged error: {ex.Message}");
            }
        });
    }

    private Microsoft.UI.Xaml.Controls.Primitives.Popup PreviewPopup
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


    private Microsoft.UI.Xaml.Controls.Primitives.Popup CreatePreviewPopup()
    {
        var elementTheme = ThemeManager.CurrentElementTheme;
        var isDarkTheme = ThemeManager.IsDarkTheme();

        var popup = new Microsoft.UI.Xaml.Controls.Primitives.Popup
        {
            IsLightDismissEnabled = false,
            ShouldConstrainToRootBounds = false,
            XamlRoot = _rootGrid?.XamlRoot,
            RequestedTheme = elementTheme,
        };

        // Helper to create brush with correct theme colors
        // These colors match the WinUI 3 system theme colors
        Brush GetThemedBrush(string key)
        {
            return key switch
            {
                "LayerFillColorDefaultBrush" => new SolidColorBrush(
                    isDarkTheme 
                        ? Windows.UI.Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)  // Dark: semi-transparent white
                        : Windows.UI.Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)), // Light: semi-transparent white
                "SurfaceStrokeColorDefaultBrush" => new SolidColorBrush(
                    isDarkTheme 
                        ? Windows.UI.Color.FromArgb(0x66, 0x75, 0x75, 0x75)  // Dark: gray stroke
                        : Windows.UI.Color.FromArgb(0x66, 0x75, 0x75, 0x75)), // Light: gray stroke
                "TextFillColorPrimaryBrush" => new SolidColorBrush(
                    isDarkTheme 
                        ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)  // Dark: white text
                        : Windows.UI.Color.FromArgb(0xFF, 0x00, 0x00, 0x00)), // Light: black text
                "TextFillColorSecondaryBrush" => new SolidColorBrush(
                    isDarkTheme 
                        ? Windows.UI.Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF)  // Dark: slightly transparent white
                        : Windows.UI.Color.FromArgb(0xC5, 0x00, 0x00, 0x00)), // Light: slightly transparent black
                _ => new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };
        }

        // Create the outer border with theme-aware styling
        var outerBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Width = 360,
            MaxHeight = 400,
            RequestedTheme = elementTheme,
            Background = GetThemedBrush("LayerFillColorDefaultBrush"),
            BorderBrush = GetThemedBrush("SurfaceStrokeColorDefaultBrush"),
        };

        // Add shadow effect
        outerBorder.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
        outerBorder.Translation = new System.Numerics.Vector3(0, 0, 32);

        var grid = new Grid { Padding = new Thickness(12, 10, 12, 12), RowSpacing = 10 };

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Track pointer over preview panel
        grid.PointerEntered += (s, e) =>
        {
            _isPointerOverPreview = true;
            _hideTimer?.Stop();
        };

        grid.PointerExited += (s, e) =>
        {
            _isPointerOverPreview = false;
            ScheduleHide();
        };

        // Header
        _previewContentType = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = GetThemedBrush("TextFillColorPrimaryBrush"),
        };
        Grid.SetRow(_previewContentType, 0);
        grid.Children.Add(_previewContentType);

        // Content
        var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 280 };

        var contentStack = new StackPanel { Spacing = 10 };

        _previewText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Visibility = Visibility.Collapsed,
            LineHeight = 20,
            Foreground = GetThemedBrush("TextFillColorPrimaryBrush"),
        };
        contentStack.Children.Add(_previewText);

        _previewRichText = new RichEditBox
        {
            IsReadOnly = true,
            Visibility = Visibility.Collapsed,
            FontSize = 13,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
        };
        contentStack.Children.Add(_previewRichText);

        _previewImage = new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            MaxWidth = 336,
            MaxHeight = 260,
            Visibility = Visibility.Collapsed,
        };

        // Add rounded corners to image
        _previewImageBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Child = _previewImage,
            Visibility = Visibility.Collapsed,
        };
        contentStack.Children.Add(_previewImageBorder);

        scrollViewer.Content = contentStack;
        Grid.SetRow(scrollViewer, 1);
        grid.Children.Add(scrollViewer);

        // Metadata
        var metadataStack = new StackPanel { Spacing = 4 };
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 6),
            Background = GetThemedBrush("SurfaceStrokeColorDefaultBrush"),
        };
        metadataStack.Children.Add(divider);

        _previewMetadata = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 16,
            Foreground = GetThemedBrush("TextFillColorSecondaryBrush"),
        };
        metadataStack.Children.Add(_previewMetadata);

        Grid.SetRow(metadataStack, 2);
        grid.Children.Add(metadataStack);

        outerBorder.Child = grid;
        popup.Child = outerBorder;
        _previewPopupRoot = outerBorder;

        // Store the outerBorder reference for animations
        popup.Opened += (s, e) =>
        {
            outerBorder.Opacity = 0;
            outerBorder.Translation = new System.Numerics.Vector3(-10, 0, 32);

            var fadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuadraticEase
                {
                    EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
                },
            };

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, outerBorder);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");
            storyboard.Children.Add(fadeIn);

            storyboard.Begin();

            // Manually animate translation
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            var elapsed = 0.0;
            timer.Tick += (s2, e2) =>
            {
                elapsed += 16;
                var progress = Math.Min(1.0, elapsed / 150.0);
                var eased = 1 - Math.Pow(1 - progress, 2); // Quadratic ease out
                outerBorder.Translation = new System.Numerics.Vector3((float)(-10 * (1 - eased)), 0, 32);

                if (progress >= 1.0)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        };

        return popup;
    }

    public void ScheduleShow(PreviewData data, FrameworkElement target)
    {
        if (_disposed)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            // Stop any pending hide timer immediately
            _hideTimer?.Stop();

            // If already showing the same item (by ID), do nothing
            // Note: We compare ID instead of reference since DTOs might be recreated
            if (_currentPreviewData?.Id == data.Id && _previewPopup != null && _previewPopup.IsOpen == true)
                return;

            _pendingPreviewData = data;
            _previewTarget = target;

            // Delay preview by 300ms
            if (_previewDelayTimer == null)
            {
                _previewDelayTimer = new DispatcherTimer();
                _previewDelayTimer.Interval = TimeSpan.FromMilliseconds(300);
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
            // Create hide timer if needed
            if (_hideTimer == null)
            {
                _hideTimer = new DispatcherTimer();
                _hideTimer.Interval = TimeSpan.FromMilliseconds(150);
                _hideTimer.Tick += (s, e) =>
                {
                    _hideTimer?.Stop();
                    if (!_isPointerOverPreview)
                    {
                        Hide();
                    }
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

        _dispatcherQueue.TryEnqueue(() =>
        {
            HidePreviewInternal();
        });
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
        // Prevent showing if already visible for the same item
        if (_currentPreviewData?.Id == data.Id && PreviewPopup.IsOpen == true)
            return;

        try
        {
            // Stop all timers
            _previewDelayTimer?.Stop();
            _hideTimer?.Stop();

            // Ensure popup is created (this initializes all UI elements)
            var popup = PreviewPopup;

            if (
                _previewContentType == null
                || _previewText == null
                || _previewRichText == null
                || _previewImage == null
                || _previewImageBorder == null
                || _previewMetadata == null
            )
                return;

            // Set current preview item
            _currentPreviewData = data;

            // Set content type header
            _previewContentType.Text = data.PreviewType;

            // Reset visibility
            _previewText.Visibility = Visibility.Collapsed;
            _previewRichText.Visibility = Visibility.Collapsed;
            _previewImageBorder.Visibility = Visibility.Collapsed;

            // Show preview based on content type
            switch (data.PreviewType)
            {
                case "Text":
                    _previewText.Text = data.TextContent ?? string.Empty;
                    _previewText.Visibility = Visibility.Visible;
                    break;

                case "RichText":
                    // Show rich text content in RichEditBox
                    if (!string.IsNullOrEmpty(data.RtfContent))
                    {
                        try
                        {
                            _previewRichText.Document.SetText(
                                Microsoft.UI.Text.TextSetOptions.FormatRtf,
                                data.RtfContent
                            );
                            _previewRichText.Visibility = Visibility.Visible;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error rendering RTF: {ex.Message}");
                            // Fallback to plain text
                            _previewText.Text = data.TextContent ?? "[RTF Content]";
                            _previewText.Visibility = Visibility.Visible;
                        }
                    }
                    else
                    {
                        // If no RTF data, show preview text
                        _previewText.Text = data.TextContent ?? "[RTF Content]";
                        _previewText.Visibility = Visibility.Visible;
                    }
                    break;

                case "Html":
                    _previewText.Text = data.TextContent ?? string.Empty;
                    _previewText.Visibility = Visibility.Visible;
                    break;

                case "Image":
                    byte[]? imageData = data.ImageBytes ?? data.Thumbnail;

                    if (imageData != null && imageData.Length > 0)
                    {
                        using (var stream = new MemoryStream(imageData))
                        {
                            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                            bitmap.SetSource(stream.AsRandomAccessStream());
                            _previewImage.Source = bitmap;
                            _previewImage.Visibility = Visibility.Visible;
                            _previewImageBorder.Visibility = Visibility.Visible;
                        }
                    }
                    else
                    {
                        // If we have no image data yet but know it's an image, hide for now
                        _previewImage.Visibility = Visibility.Collapsed;
                        _previewImageBorder.Visibility = Visibility.Collapsed;
                    }

                    // Trigger async full load if available
                    if (data.LoadFullData != null)
                    {
                        _ = LoadFullImageAsync(data);
                    }
                    break;

                case "Files":
                    _previewText.Text = data.TextContent ?? string.Empty;
                    _previewText.Visibility = Visibility.Visible;
                    break;
            }

            UpdateMetadata(data);

            // Position and show the popup
            if (_previewTarget != null)
            {
                popup.XamlRoot = _previewTarget.XamlRoot;

                // Get target position in Window-Relative DIPs
                var transform = _previewTarget.TransformToVisual(null);
                var targetPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                if (_previewPopupRoot != null)
                {
                    _previewPopupRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                }
                var popupWidth = _previewPopupRoot?.DesiredSize.Width ?? 360.0;
                var popupHeight = _previewPopupRoot?.DesiredSize.Height ?? 260.0;
                var spacing = 10.0;

                // Get Monitor/Window info in Physical Pixels
                var appWindow = _windowService.AppWindow;
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    appWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Nearest
                );
                var workArea = displayArea.WorkArea; // Physical

                double dpiScale = _windowService.GetDpiScale();
                
                // Convert Window Position to Physical (AppWindow.Position is Physical)
                double winX = appWindow.Position.X;
                double winY = appWindow.Position.Y;
                
                // Calculate Target Position in Physical Screen Coordinates
                double targetPhysicalX = winX + (targetPos.X * dpiScale);
                double targetPhysicalY = winY + (targetPos.Y * dpiScale);
                double targetPhysicalHeight = _previewTarget.ActualHeight * dpiScale;

                // --- VISIBILITY CHECK ---
                // If the target is effectively off-screen (e.g. scrolled out), don't show
                // Check if target is efficiently within the window bounds
                // Note: We use Window coordinates for this check
                var windowRect = new Windows.Foundation.Rect(0, 0, _windowService.AppWindow.Size.Width / dpiScale, _windowService.AppWindow.Size.Height / dpiScale);
                var targetRect = new Windows.Foundation.Rect(targetPos.X, targetPos.Y, _previewTarget.ActualWidth, _previewTarget.ActualHeight);
                
                // Simple intersection check: Target must be partially within window
                // Better check: Target center must be within window or specific Y range?
                // For ListView, items can exist but be clipped.
                
                // Try using TransformToVisual on Root
                var root = _previewTarget.XamlRoot?.Content as UIElement;
                if (root != null) 
                {
                   var transformToRoot = _previewTarget.TransformToVisual(root);
                   var posInRoot = transformToRoot.TransformPoint(new Windows.Foundation.Point(0,0));
                   
                   // Check Y bounds
                   if (posInRoot.Y < 0 && posInRoot.Y + _previewTarget.ActualHeight < 0) 
                   {
                       // Completely above top
                       return;
                   }
                   if (posInRoot.Y > ((FrameworkElement)root).ActualHeight) 
                   {
                       // Completely below bottom
                       return;
                   }
                }

                double targetPhysicalWidth = _previewTarget.ActualWidth * dpiScale;
                
                double popupPhysicalWidth = popupWidth * dpiScale;
                double popupPhysicalHeight = popupHeight * dpiScale;
                double spacingPhysical = spacing * dpiScale;

                // --- Positioning Logic (Physical Domain) ---

                // Default: Right of target, Top aligned
                double finalPhysicalX = targetPhysicalX + targetPhysicalWidth + spacingPhysical;
                double finalPhysicalY = targetPhysicalY;

                // 1. Check Horizontal Fit (Right)
                if (finalPhysicalX + popupPhysicalWidth > workArea.X + workArea.Width)
                {
                    // No fit right, try Left: TargetX - PopupWidth - Spacing
                    double leftTry = targetPhysicalX - popupPhysicalWidth - spacingPhysical;
                    if (leftTry >= workArea.X)
                    {
                        finalPhysicalX = leftTry;
                    }
                    else
                    {
                        // Fits nowhere? Align with edge or keep right depending on preference?
                        // Let's force it inside from the right edge
                        finalPhysicalX = (workArea.X + workArea.Width) - popupPhysicalWidth - spacingPhysical;
                    }
                }

                // 2. Check Vertical Fit (Bottom)
                if (finalPhysicalY + popupPhysicalHeight > workArea.Y + workArea.Height)
                {
                    // Shift Up to fit
                    // Position = BottomEdge - Height - Padding
                    finalPhysicalY = (workArea.Y + workArea.Height) - popupPhysicalHeight - spacingPhysical;
                }

                // Ensure Top is not cut off
                if (finalPhysicalY < workArea.Y)
                {
                    finalPhysicalY = workArea.Y + spacingPhysical;
                }

                // --- Convert back to Window-Relative DIPs ---
                // Offset = (ScreenPos - WindowPos) / Scale

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

    private async Task LoadFullImageAsync(PreviewData data)
    {
        try
        {
            var fullData = await data.LoadFullData!();

            _dispatcherQueue.TryEnqueue(() =>
            {
                // Verify we are still showing the same item and UI is valid
                if (
                    _currentPreviewData?.Id != data.Id
                    || _previewPopup?.IsOpen != true
                    || _previewImage == null
                    || _previewImageBorder == null
                )
                    return;

                if (fullData != null && fullData.Length > 0)
                {
                    // Update Data object
                    data.ImageBytes = fullData;

                    // Update Image UI
                    using (var stream = new MemoryStream(fullData))
                    {
                        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                        bitmap.SetSource(stream.AsRandomAccessStream());
                        _previewImage.Source = bitmap;
                        _previewImage.Visibility = Visibility.Visible;
                        _previewImageBorder.Visibility = Visibility.Visible;
                    }

                    // Refresh Metadata (Size, Dimensions)
                    UpdateMetadata(data);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadFullImageAsync error: {ex.Message}");
        }
    }

    private void UpdateMetadata(PreviewData data)
    {
        if (_previewMetadata == null)
            return;

        // Build metadata string
        var metadata = new System.Text.StringBuilder();

        // Created time
        metadata.AppendLine($"Copied: {data.RelativeTime}");

        // Content-specific metadata
        switch (data.PreviewType)
        {
            case "Text":
                if (!string.IsNullOrEmpty(data.TextContent))
                {
                    var charCount = data.TextContent.Length;
                    var wordCount = data
                        .TextContent.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Length;
                    var lineCount = data.TextContent.Split('\n').Length;
                    metadata.AppendLine($"Characters: {charCount:N0}");
                    metadata.AppendLine($"Words: {wordCount:N0}");
                    if (lineCount > 1)
                        metadata.AppendLine($"Lines: {lineCount:N0}");
                }
                break;

            case "RichText":
                // For RichText, show statistics about the plain text content
                if (!string.IsNullOrEmpty(data.TextContent))
                {
                    var charCount = data.TextContent.Length;
                    var wordCount = data
                        .TextContent.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Length;
                    metadata.AppendLine($"Characters: {charCount:N0}");
                    metadata.AppendLine($"Words: {wordCount:N0}");
                }
                // Show RTF size if available
                if (!string.IsNullOrEmpty(data.RtfContent))
                {
                    var rtfSize = System.Text.Encoding.UTF8.GetByteCount(data.RtfContent);
                    metadata.AppendLine($"RTF Size: {rtfSize / 1024.0:N1} KB");
                }
                break;

            case "Html":
                if (!string.IsNullOrEmpty(data.TextContent))
                {
                    var charCount = data.TextContent.Length;
                    var wordCount = data
                        .TextContent.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Length;
                    metadata.AppendLine($"Characters: {charCount:N0}");
                    metadata.AppendLine($"Words: {wordCount:N0}");
                }
                break;

            case "Image":
                if (_previewImage?.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bmp)
                {
                    // Note: PixelWidth/Height might not be available immediately if lazy loaded,
                    // but updating metadata after load ensures it catches it.
                    if (bmp.PixelWidth > 0)
                    {
                        metadata.AppendLine($"Dimensions: {bmp.PixelWidth} x {bmp.PixelHeight}");
                    }
                }

                int sizeBytes = (data.ImageBytes?.Length ?? 0);
                // Only fall back to thumbnail size if full data isn't there
                if (sizeBytes == 0)
                    sizeBytes = (data.Thumbnail?.Length ?? 0);

                if (sizeBytes > 0)
                {
                    var sizeKB = sizeBytes / 1024.0;
                    metadata.AppendLine($"Size: {sizeKB:N1} KB");
                }
                break;

            case "Files":
                if (!string.IsNullOrEmpty(data.TextContent))
                {
                    var files = data.TextContent.Split('\n').Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
                    metadata.AppendLine($"Files: {files.Length}");
                }
                break;
        }

        if (!string.IsNullOrEmpty(data.SourceApp))
        {
            metadata.AppendLine($"Source: {data.SourceApp}");
        }

        if (data.IsPinned)
        {
            metadata.AppendLine("Pinned");
        }

        _previewMetadata.Text = metadata.ToString().TrimEnd();
    }
}
