using System.Windows.Media;

namespace WharfBar.Models;

/// <summary>
/// One visible tray icon shown in the top bar mirror. <see cref="Tag"/> carries the UIA
/// <c>AutomationElement</c> for the Windows 25H2 fallback path (clicking routes through it).
/// </summary>
internal sealed record TrayMirrorItem(
    string Key,
    int CommandId,
    int Index,
    string Tooltip,
    ImageSource? Icon,
    object? Tag = null);