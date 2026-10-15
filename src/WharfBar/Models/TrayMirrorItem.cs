using System.Windows;
using System.Windows.Media;

namespace WharfBar.Models;

/// <summary>
/// One visible tray icon shown in the top bar mirror. <see cref="Tag"/> carries the UIA
/// <c>AutomationElement</c> for the Windows 25H2 fallback path (clicking routes through it).
/// <see cref="ScreenRect"/> is the icon's on-screen rect on the unparked tray at read time
/// (physical px; empty when unknown) — the click target when the live tree is not exposed.
/// </summary>
internal sealed record TrayMirrorItem(
    string Key,
    int CommandId,
    int Index,
    string Tooltip,
    ImageSource? Icon,
    object? Tag = null,
    Rect ScreenRect = default);
