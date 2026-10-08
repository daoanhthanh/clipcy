using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Clipcy.Services;

/// <summary>Observes the next input action without reading its content or consuming it.</summary>
internal sealed class PreviewInputMonitor : IDisposable
{
    private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
    private readonly HookCallback _mouseCallback;
    private readonly HookCallback _keyboardCallback;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private bool _disposed;
    private bool _notified;

    public PreviewInputMonitor(Action onInput)
    {
        void Notify()
        {
            if (_disposed || _notified)
                return;
            _notified = true;
            onInput();
        }

        _mouseCallback = (code, message, data) =>
        {
            if (code >= 0 && IsMouseAction((int)message))
                Notify();
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        _keyboardCallback = (code, message, data) =>
        {
            if (code >= 0 && IsKeyboardAction((int)message))
                Notify();
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        var module = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(14, _mouseCallback, module, 0); // WH_MOUSE_LL
        _keyboardHook = SetWindowsHookEx(13, _keyboardCallback, module, 0); // WH_KEYBOARD_LL
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Unable to monitor preview dismissal input.");
        }
    }

    // Only button-down and wheel messages dismiss; movement and button-up do not.
    internal static bool IsMouseAction(int message) =>
        message is 0x0201 or 0x0204 or 0x0207 or 0x020B or 0x020A or 0x020E;

    // Key-up must not dismiss a preview opened using Enter in the action menu.
    internal static bool IsKeyboardAction(int message) => message is 0x0100 or 0x0104;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_mouseHook != IntPtr.Zero)
            UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero)
            UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = _keyboardHook = IntPtr.Zero;
        GC.KeepAlive(_mouseCallback);
        GC.KeepAlive(_keyboardCallback);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookCallback callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
