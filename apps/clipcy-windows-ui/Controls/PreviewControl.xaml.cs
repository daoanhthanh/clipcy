using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Clipcy.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Clipcy.Controls;

public sealed partial class PreviewControl : UserControl
{
    private const int MaxTableRows = 12;
    private const int MaxTableColumns = 8;
    private static readonly Regex HtmlRowRegex = new(
        "<tr[^>]*>(.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
    );
    private static readonly Regex HtmlCellRegex = new(
        "<t[dh][^>]*>(.*?)</t[dh]>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
    );
    private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Singleline);
    private static readonly Regex HtmlBreakRegex = new("<br\\s*/?>", RegexOptions.IgnoreCase);
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
        HideTable();
        PreviewImageBorder.Visibility = Visibility.Collapsed;

        // Update Content based on type
        switch (data.PreviewType)
        {
            case "Text":
            case "Html":
            case "Files":
                if (data.PreviewType == "Text")
                {
                    if (TryShowTableFromText(data.TextContent))
                        break;

                    PreviewText.Text = data.TextContent ?? string.Empty;
                    PreviewText.Visibility = Visibility.Visible;
                    break;
                }

                if (data.PreviewType == "Html")
                {
                    if (TryShowTableFromHtml(data.TextContent))
                        break;

                    PreviewText.Text = StripHtmlToText(data.TextContent) ?? string.Empty;
                    PreviewText.Visibility = Visibility.Visible;
                    break;
                }

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

    private void HideTable()
    {
        PreviewTableBorder.Visibility = Visibility.Collapsed;
        PreviewTable.Children.Clear();
        PreviewTable.RowDefinitions.Clear();
        PreviewTable.ColumnDefinitions.Clear();
    }

    private bool TryShowTableFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var rows = new List<List<string>>();
        var hasTabs = false;

        foreach (var rawLine in lines)
        {
            if (rows.Count >= MaxTableRows)
                break;

            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var cells = rawLine.Split('\t');
            if (cells.Length > 1)
                hasTabs = true;

            var row = new List<string>();
            for (int i = 0; i < cells.Length && i < MaxTableColumns; i++)
            {
                row.Add(cells[i].Trim());
            }

            rows.Add(row);
        }

        if (!hasTabs || rows.Count == 0)
            return false;

        ShowTable(rows);
        return true;
    }

    private bool TryShowTableFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        var hasRows = html.Contains("<tr", StringComparison.OrdinalIgnoreCase);
        var hasCells = html.Contains("<td", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<th", StringComparison.OrdinalIgnoreCase);

        if (!hasRows || !hasCells)
            return false;

        var rows = ParseHtmlTable(html);
        if (rows.Count == 0)
            return false;

        ShowTable(rows);
        return true;
    }

    private List<List<string>> ParseHtmlTable(string html)
    {
        var rows = new List<List<string>>();
        var rowMatches = HtmlRowRegex.Matches(html);
        if (rowMatches.Count == 0)
            return rows;

        foreach (Match rowMatch in rowMatches)
        {
            if (rows.Count >= MaxTableRows)
                break;

            var rowHtml = rowMatch.Groups[1].Value;
            var cellMatches = HtmlCellRegex.Matches(rowHtml);
            if (cellMatches.Count == 0)
                continue;

            var row = new List<string>();
            foreach (Match cellMatch in cellMatches)
            {
                if (row.Count >= MaxTableColumns)
                    break;

                var cellHtml = cellMatch.Groups[1].Value;
                var cellText = StripHtmlToText(cellHtml) ?? string.Empty;
                row.Add(cellText);
            }

            rows.Add(row);
        }

        return rows;
    }

    private void ShowTable(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        PreviewTable.Children.Clear();
        PreviewTable.RowDefinitions.Clear();
        PreviewTable.ColumnDefinitions.Clear();

        var maxColumns = rows.Max(r => r.Count);
        if (maxColumns == 0)
            return;

        for (int row = 0; row < rows.Count; row++)
        {
            PreviewTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int col = 0; col < maxColumns; col++)
        {
            PreviewTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var borderBrush = PreviewTableBorder.BorderBrush;
        var textBrush = PreviewText.Foreground;

        for (int row = 0; row < rows.Count; row++)
        {
            var rowValues = rows[row];
            for (int col = 0; col < maxColumns; col++)
            {
                var cellText = col < rowValues.Count ? rowValues[col] : string.Empty;
                var rightBorder = col == maxColumns - 1 ? 0 : 1;
                var bottomBorder = row == rows.Count - 1 ? 0 : 1;

                var cell = new Border
                {
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(0, 0, rightBorder, bottomBorder),
                    Padding = new Thickness(6, 4, 6, 4)
                };

                var textBlock = new TextBlock
                {
                    Text = cellText,
                    FontSize = 12,
                    LineHeight = 16,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = textBrush
                };

                cell.Child = textBlock;
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, col);
                PreviewTable.Children.Add(cell);
            }
        }

        PreviewTableBorder.Visibility = Visibility.Visible;
    }

    private static string? StripHtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        var normalized = HtmlBreakRegex.Replace(html, "\n");
        var withoutTags = HtmlTagRegex.Replace(normalized, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags).Replace("\u00A0", " ");
        return decoded.Trim();
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
