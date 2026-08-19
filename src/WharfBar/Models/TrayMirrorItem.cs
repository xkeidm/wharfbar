using System.Windows.Media;

namespace WharfBar.Models;

/// <summary>One visible tray icon shown in the top bar mirror.</summary>
internal sealed record TrayMirrorItem(string Key, int CommandId, int Index, string Tooltip, ImageSource? Icon);