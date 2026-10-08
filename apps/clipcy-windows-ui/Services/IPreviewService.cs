using System;
using Microsoft.UI.Xaml;

namespace Clipcy.Services;

public interface IPreviewService : IDisposable
{
    /// <summary>
    /// Schedules a preview to be shown for the given data after a delay.
    /// </summary>
    /// <param name="data">The data to preview.</param>
    /// <param name="target">The UI element that triggered the preview (used for positioning).</param>
    void ScheduleShow(PreviewData data, FrameworkElement target);

    /// <summary>Whether automatic hover/focus previews are enabled.</summary>
    bool AutomaticPreviewsEnabled { get; }

    bool IsManualPreviewOpen { get; }
    event Action? ManualPreviewDismissed;

    /// <summary>Opens a requested preview immediately, even when automatic previews are disabled.</summary>
    void ShowManual(PreviewData data, FrameworkElement target);

    /// <summary>
    /// Schedules the preview to be hidden after a delay.
    /// </summary>
    void ScheduleHide();

    /// <summary>
    /// Hides the preview immediately.
    /// </summary>
    void Hide();
}
