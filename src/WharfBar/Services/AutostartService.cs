using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WharfBar.Services;

/// <summary>
/// Keeps a Startup-folder shortcut in sync with <c>AppConfig.autostart</c>.
/// Uses the per-user Startup folder (not Registry Run).
/// </summary>
internal sealed class AutostartService
{
    private const string ShortcutFileName = "WharfBar.lnk";
    private const string ShortcutDescription = "WharfBar";

    internal string ShortcutPath { get; }

    internal AutostartService()
    {
        string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        ShortcutPath = Path.Combine(startupFolder, ShortcutFileName);
    }

    /// <summary>True when the Startup shortcut file exists.</summary>
    internal bool IsEnabled() => File.Exists(ShortcutPath);

    /// <summary>
    /// Makes Startup presence match <paramref name="enabled"/>.
    /// When enabling, the shortcut is rewritten so the target tracks the current build path.
    /// </summary>
    internal void Apply(bool enabled)
    {
        try
        {
            if (enabled)
            {
                CreateOrUpdateShortcut();
            }
            else
            {
                RemoveShortcut();
            }
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or COMException
                                   or InvalidOperationException
                                   or TargetInvocationException)
        {
            Debug.WriteLine($"[WharfBar] Autostart apply failed (enabled={enabled}): {ex.Message}");
        }
    }

    /// <summary>
    /// Applies <paramref name="enabled"/> and returns whether Startup presence now matches.
    /// </summary>
    internal bool Sync(bool enabled)
    {
        Apply(enabled);
        return IsEnabled() == enabled;
    }

    private void CreateOrUpdateShortcut()
    {
        string targetPath = ResolveExecutablePath();
        string? workingDirectory = Path.GetDirectoryName(targetPath);

        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell ProgID is unavailable.");

        object shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Failed to create WScript.Shell.");

        object shortcut = Invoke(shell, "CreateShortcut", ShortcutPath)
            ?? throw new InvalidOperationException("CreateShortcut returned null.");

        SetProperty(shortcut, "TargetPath", targetPath);
        SetProperty(shortcut, "WorkingDirectory", workingDirectory ?? string.Empty);
        SetProperty(shortcut, "Description", ShortcutDescription);
        SetProperty(shortcut, "IconLocation", targetPath + ",0");
        Invoke(shortcut, "Save");
    }

    private void RemoveShortcut()
    {
        if (!File.Exists(ShortcutPath))
        {
            return;
        }

        File.Delete(ShortcutPath);
    }

    private static string ResolveExecutablePath()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(processPath))
        {
            return Path.GetFullPath(processPath);
        }

        string fallback = Path.Combine(AppContext.BaseDirectory, "WharfBar.exe");
        if (File.Exists(fallback))
        {
            return Path.GetFullPath(fallback);
        }

        throw new InvalidOperationException("Could not resolve WharfBar executable path for the Startup shortcut.");
    }

    private static object? Invoke(object target, string memberName, params object?[] args)
    {
        return target.GetType().InvokeMember(
            memberName,
            BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            target: target,
            args: args);
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        target.GetType().InvokeMember(
            propertyName,
            BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            target: target,
            args: [value]);
    }
}
