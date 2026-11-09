using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Which cursor-free UI Automation gesture replays a mirrored tray click on the Windows 11 25H2
/// XAML tray button. Left is the button's own <c>InvokePattern</c>; a double-left is two invoked
/// clicks (UIA cannot express a double-click); right is keyboard focus + the Menu key, because
/// UIA has no right-click action and a physical click would move the user's pointer.
/// </summary>
internal enum TrayUiaGesture
{
    /// <summary><c>InvokePattern.Invoke()</c> once.</summary>
    Invoke,

    /// <summary><c>InvokePattern.Invoke()</c> twice with a short gap (double-click).</summary>
    InvokeTwice,

    /// <summary>Focus the button, then press the Menu key (VK_APPS).</summary>
    FocusMenuKey,
}

internal static class TrayUiaGestures
{
    internal static TrayUiaGesture For(TrayClick kind) => kind switch
    {
        TrayClick.Right => TrayUiaGesture.FocusMenuKey,
        TrayClick.DoubleLeft => TrayUiaGesture.InvokeTwice,
        _ => TrayUiaGesture.Invoke,
    };
}
