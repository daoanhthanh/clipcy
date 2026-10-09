using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Clipcy.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

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

    private bool _suppressSelectionEvents;
    private bool _isLanguageSubscribed;
    private string _displayType = "common.loading";
    private string _fileStatusKey = string.Empty;
    private FilePreviewResult? _statusPreview;
    private string? _statusFallback;

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

    /// <summary>Raised when the user picks a different file in a multi-file selection.</summary>
    public event EventHandler<int>? SelectedFileIndexChanged;

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
        ContentTypeText.Text = LocalizationService.Get("common.loading");
        UpdateAccessibilityNames();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }


    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isLanguageSubscribed)
        {
            LocalizationService.LanguageChanged += OnLanguageChanged;
            _isLanguageSubscribed = true;
        }
        OnLanguageChanged(this, EventArgs.Empty);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_isLanguageSubscribed)
        {
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            _isLanguageSubscribed = false;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateAccessibilityNames();
        UpdateContentTypeLabel();
        if (PreviewData is { } data)
        {
            UpdateMetadata(data);
            UpdateFileCount(data.FilePaths?.Count ?? 0);
            if (FileStatusText.Visibility == Visibility.Visible)
            {
                FileStatusText.Text = _statusPreview == null
                    ? LocalizationService.Get(_fileStatusKey)
                    : _statusFallback == null
                        ? FormatStatus(_statusPreview)
                        : GetStatusMessage(_statusPreview, _statusFallback);
            }
            if (data.PreviewType == "RichText"
                && PreviewText.Visibility == Visibility.Visible
                && (data.TextContent == null || data.TextContent == "[RTF Content]"))
            {
                PreviewText.Text = LocalizationService.Get("content.rtf_placeholder");
            }
        }
    }

    private void UpdateAccessibilityNames()
    {
        AutomationProperties.SetName(FileSelector, LocalizationService.Get("preview.selected_file"));
        AutomationProperties.SetName(AudioSymbol, LocalizationService.Get("preview.audio_file"));
    }

    private void UpdateContentTypeLabel()
    {
        ContentTypeText.Text = LocalizationService.Get(_displayType switch
        {
            "Html" => "content.html",
            "RichText" => "content.rich_text",
            "Text" => "content.text",
            "Image" => "content.image",
            "Files" => "content.files",
            "Table" => "content.table",
            "Video" => "content.video",
            "Audio" => "content.audio",
            "Folder" => "content.folder",
            "Other" => "content.other",
            _ => _displayType,
        });
    }

    private static void OnPreviewDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PreviewControl control)
            return;

        if (e.NewValue is PreviewData data)
            control.UpdateContent(data);
        else
            control.ResetAllContent();
    }


    private void ResetAllContent()
    {
        ClearContentSurfaces();
        FileSelectorRow.Visibility = Visibility.Collapsed;
        FileNameText.Visibility = Visibility.Collapsed;
        FileNameText.Text = string.Empty;
        _displayType = string.Empty;
        ContentTypeText.Text = string.Empty;
        MetadataText.Text = string.Empty;
        SourceAppRow.Visibility = Visibility.Collapsed;
    }

    private void ClearContentSurfaces()
    {
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewText.Text = string.Empty;
        PreviewRichText.Visibility = Visibility.Collapsed;
        HideTable();
        PreviewImage.Source = null;
        PreviewImageBorder.Visibility = Visibility.Collapsed;
        AudioIconPanel.Visibility = Visibility.Collapsed;
        FileStatusText.Visibility = Visibility.Collapsed;
        FileStatusText.Text = string.Empty;
        _fileStatusKey = string.Empty;
        _statusPreview = null;
        _statusFallback = null;
    }


    private void UpdateContent(PreviewData data)
    {
        ClearContentSurfaces();

        var displayType = data.PreviewType;

        switch (data.PreviewType)
        {
            case "Text":
                if (TryShowTableFromText(data.TextContent))
                {
                    displayType = "Table";
                    break;
                }

                PreviewText.Text = data.TextContent ?? string.Empty;
                PreviewText.Visibility = Visibility.Visible;
                break;

            case "Html":
                if (TryShowTableFromHtml(data.TextContent))
                {
                    displayType = "Table";
                    break;
                }

                PreviewText.Text = StripHtmlToText(data.TextContent) ?? string.Empty;
                PreviewText.Visibility = Visibility.Visible;
                break;

            case "Files":
                UpdateFilesContent(data, ref displayType);
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
                        PreviewText.Text = data.TextContent == null || data.TextContent == "[RTF Content]"
                            ? LocalizationService.Get("content.rtf_placeholder") : data.TextContent;
                        PreviewText.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    PreviewText.Text = data.TextContent == null || data.TextContent == "[RTF Content]"
                        ? LocalizationService.Get("content.rtf_placeholder") : data.TextContent;
                    PreviewText.Visibility = Visibility.Visible;
                }
                break;

            case "Image":
                UpdateImage(data);
                break;
        }

        _displayType = displayType;
        UpdateContentTypeLabel();
        UpdateMetadata(data);
    }


    private void UpdateFilesContent(PreviewData data, ref string displayType)
    {
        BindFileSelector(data);

        var paths = data.FilePaths;
        if (paths is null || paths.Count == 0)
        {
            if (data.IsFilePreviewLoading)
            {
                ShowFileStatus("preview.loading_ellipsis");
            }
            else if (!string.IsNullOrEmpty(data.TextContent))
            {
                // Fallback: path list text until full data arrives.
                PreviewText.Text = data.TextContent;
                PreviewText.Visibility = Visibility.Visible;
            }
            else
            {
                ShowFileStatus("preview.paths_unavailable");
            }

            displayType = "Files";
            return;
        }

        var index = Math.Clamp(data.SelectedFileIndex, 0, paths.Count - 1);
        var path = paths[index];
        FileNameText.Text = path;
        ToolTipService.SetToolTip(FileNameText, path);
        FileNameText.Visibility = Visibility.Visible;
        AutomationProperties.SetName(FileNameText, path);

        if (data.IsFilePreviewLoading && data.FilePreview is null)
        {
            ShowFileStatus("preview.loading");
            displayType = "Files";
            return;
        }

        var preview = data.FilePreview;
        if (preview is null)
        {
            ShowFileStatus("preview.none");
            displayType = "Files";
            return;
        }

        displayType = preview.Kind switch
        {
            FilePreviewKind.Image => "preview.files_image",
            FilePreviewKind.Text => "preview.files_text",
            FilePreviewKind.Video => "preview.files_video",
            FilePreviewKind.Audio => "preview.files_audio",
            FilePreviewKind.Folder => "preview.files_folder",
            _ => "Files",
        };

        switch (preview.Status)
        {
            case FilePreviewStatus.Ready:
            case FilePreviewStatus.Truncated:
                RenderFilePreview(preview);
                break;
            case FilePreviewStatus.Loading:
            case FilePreviewStatus.Canceled:
                // Canceled stays quiet; a newer request should replace this.
                ShowFileStatus("preview.loading");
                break;
            default:
                ShowPreviewStatus(preview);
                break;
        }
    }

    private void ShowFileStatus(string message)
    {
        _fileStatusKey = message;
        _statusPreview = null;
        _statusFallback = null;
        FileStatusText.Text = LocalizationService.Get(message);
        FileStatusText.Visibility = Visibility.Visible;
    }

    private void ShowPreviewStatus(FilePreviewResult preview, string? fallback = null)
    {
        _statusPreview = preview;
        _statusFallback = fallback;
        FileStatusText.Text = fallback == null ? FormatStatus(preview) : GetStatusMessage(preview, fallback);
        FileStatusText.Visibility = Visibility.Visible;
    }


    private void RenderFilePreview(FilePreviewResult preview)
    {
        switch (preview.Kind)
        {
            case FilePreviewKind.Image:
            case FilePreviewKind.Video:
                if (preview.PreviewPngBytes is { Length: > 0 } png)
                {
                    SetImageFromBytes(png);
                }
                else
                {
                    ShowPreviewStatus(preview, "preview.image_unavailable");
                }
                break;

            case FilePreviewKind.Text:
                PreviewText.Text = preview.TextContent ?? string.Empty;
                PreviewText.Visibility = Visibility.Visible;
                if (preview.IsTruncated || preview.Status == FilePreviewStatus.Truncated)
                    ShowPreviewStatus(preview, "preview.text_truncated");
                break;

            case FilePreviewKind.Audio:
                AudioFileNameText.Text = preview.DisplayName;
                AudioIconPanel.Visibility = Visibility.Visible;
                break;

            default:
                ShowPreviewStatus(preview);
                break;
        }
    }


    private static string FormatStatus(FilePreviewResult preview)
    {
        var baseMsg = preview.Status switch
        {
            FilePreviewStatus.Unavailable => "status.unavailable",
            FilePreviewStatus.Unsupported => "status.unsupported",
            FilePreviewStatus.RemoteOrCloud => "status.remote_or_cloud",
            FilePreviewStatus.ResourceLimited => "status.resource_limit",
            FilePreviewStatus.Truncated => "status.truncated",
            FilePreviewStatus.Ready => "main.ready",
            FilePreviewStatus.Loading => "preview.loading_ellipsis",
            FilePreviewStatus.Canceled => "status.canceled",
            _ => "status.unavailable",
        };
        var localizedBase = LocalizationService.Get(baseMsg);
        if (!string.IsNullOrWhiteSpace(preview.StatusMessage))
            return LocalizationService.Format("common.pair", localizedBase, GetStatusMessage(preview, baseMsg));
        return localizedBase;
    }

    private static string GetStatusMessage(FilePreviewResult preview, string fallback)
    {
        if (string.IsNullOrWhiteSpace(preview.StatusMessage))
            return LocalizationService.Get(fallback);
        return preview.StatusMessageArguments is { } arguments
            ? LocalizationService.Format(preview.StatusMessage, arguments)
            : LocalizationService.Get(preview.StatusMessage);
    }

    private void BindFileSelector(PreviewData data)
    {
        var paths = data.FilePaths;
        if (paths is null || paths.Count == 0)
        {
            FileSelectorRow.Visibility = Visibility.Collapsed;
            return;
        }

        _suppressSelectionEvents = true;
        try
        {
            FileSelector.Items.Clear();
            foreach (var p in paths)
            {
                FileSelector.Items.Add(new ComboBoxItem
                {
                    Content = GetFileNameSafe(p),
                    Tag = p,
                });
            }

            var idx = Math.Clamp(data.SelectedFileIndex, 0, paths.Count - 1);
            FileSelector.SelectedIndex = idx;
            UpdateFileCount(paths.Count);
            // Multi-file only: single file uses FileNameText instead.
            FileSelectorRow.Visibility = paths.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }

    private void UpdateFileCount(int count)
    {
        FileCountText.Text = count == 1
            ? LocalizationService.Get("preview.one_file")
            : LocalizationService.Format("preview.n_files", count);
    }


    private void FileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;
        if (FileSelector.SelectedIndex < 0)
            return;
        SelectedFileIndexChanged?.Invoke(this, FileSelector.SelectedIndex);
    }

    private void SetImageFromBytes(byte[] imageData)
    {
        try
        {
            using var stream = new MemoryStream(imageData);
            var bitmap = new BitmapImage();
            bitmap.SetSource(stream.AsRandomAccessStream());
            PreviewImage.Source = bitmap;
            PreviewImageBorder.Visibility = Visibility.Visible;
        }
        catch
        {
            PreviewImage.Source = null;
            PreviewImageBorder.Visibility = Visibility.Collapsed;
            ShowFileStatus("preview.render_image_failed");
        }
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

        var hasCells =
            html.Contains("<td", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<th", StringComparison.OrdinalIgnoreCase);

        if (!hasCells)
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
        {
            var cellMatches = HtmlCellRegex.Matches(html);
            if (cellMatches.Count == 0)
                return rows;

            var row = new List<string>();
            foreach (Match cellMatch in cellMatches)
            {
                if (row.Count >= MaxTableColumns)
                    break;

                var cellHtml = cellMatch.Groups[1].Value;
                var cellText = StripHtmlToText(cellHtml) ?? string.Empty;
                row.Add(cellText);
            }

            if (row.Count > 0)
            {
                rows.Add(row);
            }

            return rows;
        }

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

        var cellTemplate = (DataTemplate)Resources["PreviewTableCellTemplate"];

        for (int row = 0; row < rows.Count; row++)
        {
            var rowValues = rows[row];
            for (int col = 0; col < maxColumns; col++)
            {
                var cellText = col < rowValues.Count ? rowValues[col] : string.Empty;
                var rightBorder = col == maxColumns - 1 ? 0 : 1;
                var bottomBorder = row == rows.Count - 1 ? 0 : 1;

                var cell = (Border)cellTemplate.LoadContent();
                cell.DataContext = cellText;
                cell.BorderThickness = new Thickness(0, 0, rightBorder, bottomBorder);

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
        var imageData = data.ImageBytes ?? data.Thumbnail;
        if (imageData is { Length: > 0 })
        {
            SetImageFromBytes(imageData);
            return;
        }

        PreviewImage.Source = null;
        PreviewImageBorder.Visibility = Visibility.Visible;
    }


    private void UpdateMetadata(PreviewData data)
    {
        Localization.SetText(PinHintText, data.IsPinned ? "preview.hint_unpin" : "preview.hint_pin");
        UpdateSourceApp(data);
        var metadata = new System.Text.StringBuilder();

        if (!string.IsNullOrEmpty(data.RelativeTime))
        {
            var displayTime = GetDisplayTime(data.CreatedAtTimestamp);
            metadata.AppendLine(LocalizationService.Format("preview.copied", displayTime));
        }

        switch (data.PreviewType)
        {
            case "Image":
                int sizeBytes = (data.ImageBytes?.Length ?? 0);
                if (sizeBytes == 0)
                    sizeBytes = (data.Thumbnail?.Length ?? 0);
                if (sizeBytes > 0)
                {
                    metadata.AppendLine(LocalizationService.Format("preview.size_kb", sizeBytes / 1024.0));
                }
                break;
            case "Files":
            {
                var count = data.FilePaths?.Count
                    ?? (!string.IsNullOrEmpty(data.TextContent)
                        ? data.TextContent.Split('\n').Count(f => !string.IsNullOrWhiteSpace(f))
                        : 0);
                if (count > 0)
                    metadata.AppendLine(LocalizationService.Format("preview.files_count", count));

                if (data.FilePreview is { } fp)
                {
                    metadata.AppendLine(LocalizationService.Format("preview.kind", GetFileKindLabel(fp.Kind)));
                    if (fp.IsTruncated || fp.Status == FilePreviewStatus.Truncated)
                        metadata.AppendLine(LocalizationService.Get("preview.truncated_label"));
                    if (fp.Status is FilePreviewStatus.Unsupported
                        or FilePreviewStatus.Unavailable
                        or FilePreviewStatus.RemoteOrCloud
                        or FilePreviewStatus.ResourceLimited)
                    {
                        metadata.AppendLine(FormatStatus(fp));
                    }
                }
                break;
            }

        }

        MetadataText.Text = metadata.ToString().TrimEnd();
    }

    private void UpdateSourceApp(PreviewData data)
    {
        if (string.IsNullOrWhiteSpace(data.SourceApp))
        {
            SourceAppRow.Visibility = Visibility.Collapsed;
            SourceAppText.Text = string.Empty;
            SourceAppIcon.Source = null;
            SourceAppIcon.Visibility = Visibility.Collapsed;
            return;
        }

        SourceAppRow.Visibility = Visibility.Visible;
        SourceAppText.Text = data.SourceApp;

        if (data.SourceAppIcon != null && data.SourceAppIcon.Length > 0)
        {
            using var stream = new MemoryStream(data.SourceAppIcon);
            var bitmap = new BitmapImage();
            bitmap.SetSource(stream.AsRandomAccessStream());
            SourceAppIcon.Source = bitmap;
            SourceAppIcon.Visibility = Visibility.Visible;
        }
        else
        {
            SourceAppIcon.Source = null;
            SourceAppIcon.Visibility = Visibility.Collapsed;
        }
    }

    private static string GetDisplayTime(ulong timestamp)
    {
        var dateTime = DateTimeOffset.FromUnixTimeSeconds((long)timestamp).ToLocalTime();
        var diff = DateTimeOffset.Now - dateTime;
        if (diff.TotalDays >= 4)
            return dateTime.ToString("g");

        var relativeTime = diff.TotalMinutes switch
        {
            < 1 => LocalizationService.Get("time.just_now"),
            < 60 => LocalizationService.Format("time.m_ago", (int)diff.TotalMinutes),
            < 1440 => LocalizationService.Format("time.h_ago", (int)diff.TotalHours),
            _ => LocalizationService.Format("time.d_ago", (int)diff.TotalDays),
        };
        return LocalizationService.Format("common.list_pair", relativeTime, dateTime.ToString("t"));
    }

    private static string GetFileKindLabel(FilePreviewKind kind)
    {
        return LocalizationService.Get(kind switch
        {
            FilePreviewKind.Image => "content.image",
            FilePreviewKind.Text => "content.text",
            FilePreviewKind.Video => "content.video",
            FilePreviewKind.Audio => "content.audio",
            FilePreviewKind.Folder => "content.folder",
            _ => "content.other",
        });
    }

    private static string GetFileNameSafe(string path)
    {
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
}
