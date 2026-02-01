using System;
using System.IO;
using System.Linq;
using Clipcy.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Clipcy.Controls;

public sealed partial class PreviewControl : UserControl
{
    public static readonly DependencyProperty PreviewDataProperty = DependencyProperty.Register(
        nameof(PreviewData),
        typeof(PreviewData),
        typeof(PreviewControl),
        new PropertyMetadata(null, OnPreviewDataChanged)
    );

    public PreviewData? PreviewData
    {
        get => (PreviewData?)GetValue(PreviewDataProperty);
        set => SetValue(PreviewDataProperty, value);
    }

    /// <summary>
    /// Forces a refresh of the UI from the current PreviewData.
    /// Useful when the data object changes internally (e.g. image loaded async).
    /// </summary>
    public void Refresh()
    {
        if (PreviewData != null)
        {
            UpdateContent(PreviewData);
        }
    }

    public PreviewControl()
    {
        this.InitializeComponent();

        // Ensure Shadow/Translation if XAML fails (though we put it back in XAML for now,
        // if this fails again we will move it back to C#).
        // Since we are rewriting, let's keep XAML clean and verify.
        // Wait, I put Translation/Shadow back in XAML in the step above.
        // If it fails I'll move it here.
    }

    private static void OnPreviewDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PreviewControl control && e.NewValue is PreviewData data)
        {
            control.UpdateContent(data);
        }
    }

    private void UpdateContent(PreviewData data)
    {
        if (data == null)
            return;

        // Header
        ContentTypeText.Text = data.PreviewType;

        // Reset Visibility
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewRichText.Visibility = Visibility.Collapsed;
        PreviewImageBorder.Visibility = Visibility.Collapsed;

        // Update Content based on type
        switch (data.PreviewType)
        {
            case "Text":
            case "Html":
            case "Files":
                PreviewText.Text = data.TextContent ?? string.Empty;
                PreviewText.Visibility = Visibility.Visible;
                break;

            case "RichText":
                if (!string.IsNullOrEmpty(data.RtfContent))
                {
                    try
                    {
                        PreviewRichText.Document.SetText(Microsoft.UI.Text.TextSetOptions.FormatRtf, data.RtfContent);
                        PreviewRichText.Visibility = Visibility.Visible;
                    }
                    catch
                    {
                        PreviewText.Text = data.TextContent ?? "[RTF Content]";
                        PreviewText.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    PreviewText.Text = data.TextContent ?? "[RTF Content]";
                    PreviewText.Visibility = Visibility.Visible;
                }
                break;

            case "Image":
                UpdateImage(data);
                break;
        }

        // Update Metadata
        UpdateMetadata(data);
    }

    private void UpdateImage(PreviewData data)
    {
        byte[]? imageData = data.ImageBytes ?? data.Thumbnail;

        if (imageData != null && imageData.Length > 0)
        {
            using var stream = new MemoryStream(imageData);
            var bitmap = new BitmapImage();
            bitmap.SetSource(stream.AsRandomAccessStream());
            PreviewImage.Source = bitmap;
            PreviewImageBorder.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewImage.Source = null;
            PreviewImageBorder.Visibility = Visibility.Visible; // Show empty or hide?
            // If waiting for load, maybe show placeholder?
            // Logic in PreviewService handles async loading which triggers UpdateContent again
        }
    }

    private void UpdateMetadata(PreviewData data)
    {
        var metadata = new System.Text.StringBuilder();

        // Created time
        if (!string.IsNullOrEmpty(data.RelativeTime))
        {
            var displayTime = GetDisplayTime(data.RelativeTime, data.CreatedAtTimestamp);
            metadata.AppendLine($"Copied: {displayTime}");
        }

        // Content-specific metadata
        switch (data.PreviewType)
        {
            case "Text":
            case "Html":
                if (!string.IsNullOrEmpty(data.TextContent))
                {
                    metadata.AppendLine($"Characters: {data.TextContent.Length:N0}");
                }
                break;
            case "Image":
                int sizeBytes = (data.ImageBytes?.Length ?? 0);
                if (sizeBytes == 0)
                    sizeBytes = (data.Thumbnail?.Length ?? 0);
                if (sizeBytes > 0)
                {
                    metadata.AppendLine($"Size: {sizeBytes / 1024.0:N1} KB");
                }
                break;
            case "Files":
                if (!string.IsNullOrEmpty(data.TextContent))
                {
                    var count = data.TextContent.Split('\n').Where(f => !string.IsNullOrWhiteSpace(f)).Count();
                    metadata.AppendLine($"Files: {count}");
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

        MetadataText.Text = metadata.ToString().TrimEnd();
    }

    private string GetDisplayTime(string relativeTime, ulong timestamp)
    {
        var dateTime = DateTimeOffset.FromUnixTimeSeconds((long)timestamp).ToLocalTime();
        var timeFormat = dateTime.ToString("HH:mm");

        // Show relative time with time for up to 3 days
        if (relativeTime == "just now" || 
            relativeTime.Contains("m ago") || 
            relativeTime.Contains("h ago"))
        {
            return $"{relativeTime}, {timeFormat}";
        }
        
        // Check if it's within 3 days (1d ago, 2d ago, 3d ago)
        if (relativeTime.Contains("d ago"))
        {
            var daysPart = relativeTime.Replace("d ago", "").Trim();
            if (int.TryParse(daysPart, out int days) && days <= 3)
            {
                return $"{relativeTime}, {timeFormat}";
            }
        }

        // Beyond 3 days, show full timestamp as YYYY-MM-DD HH:mm
        return dateTime.ToString("yyyy-MM-dd HH:mm");
    }
}
