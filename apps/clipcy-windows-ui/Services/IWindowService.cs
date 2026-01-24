using Microsoft.UI.Windowing;

namespace Clipcy.Services;

public interface IWindowService
{
    /// <summary>
    /// Initializes window styling and behaviors (Acrylic, Rounded corners, Tool window).
    /// </summary>
    void Initialize();

    /// <summary>
    /// Shows the window at the current cursor position, ensuring it stays on screen.
    /// </summary>
    void ShowAtCursor();

    /// <summary>
    /// Hides the window.
    /// </summary>
    void Hide();

    /// <summary>
    /// Gets the current DPI scale factor for the window.
    /// </summary>
    double GetDpiScale();

    /// <summary>
    /// Ensures the backdrop (Acrylic/Mica) remains active.
    /// </summary>
    void EnsureBackdropActive();

    /// <summary>
    /// Checks if the window is currently visible.
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// Gets the underlying AppWindow.
    /// </summary>
    AppWindow AppWindow { get; }
}
