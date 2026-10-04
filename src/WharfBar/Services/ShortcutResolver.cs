using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WharfBar.Services;

internal static class ShortcutResolver
{
    internal static bool TryResolve(string lnkPath, out string targetPath, out string args, out string label)
    {
        targetPath = string.Empty;
        args = string.Empty;
        label = Path.GetFileNameWithoutExtension(lnkPath) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(lnkPath) || !File.Exists(lnkPath))
        {
            return false;
        }

        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            object shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("WScript.Shell create failed");
            try
            {
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    [lnkPath])!;

                try
                {
                    targetPath = ((string?)shortcut.GetType().InvokeMember(
                        "TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null))
                        ?.Trim() ?? string.Empty;
                    args = ((string?)shortcut.GetType().InvokeMember(
                        "Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null))
                        ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        label = Path.GetFileNameWithoutExtension(targetPath) ?? "App";
                    }

                    return !string.IsNullOrWhiteSpace(targetPath);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar shortcut resolve failed: {lnkPath} — {ex.Message}");
            return false;
        }
    }
}
