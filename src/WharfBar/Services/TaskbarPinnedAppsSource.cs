using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>What <see cref="ShortcutResolver"/> read from one <c>.lnk</c>. Resolved is false when the target could not be read.</summary>
internal readonly record struct ShortcutLaunch(bool Resolved, string Path, string Args, string Label);

/// <summary>
/// Windows taskbar User Pinned shortcuts. This type only reads.
/// <see cref="UserPinnedWriter"/> creates and deletes <c>.lnk</c> files in that folder.
/// The Taskband registry value is never written.
/// </summary>
internal sealed class TaskbarPinnedAppsSource : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly Dispatcher _dispatcher;
    private readonly List<PinnedApp> _pins = [];
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private DispatcherTimer? _poll;
    private bool _disposed;

    internal TaskbarPinnedAppsSource(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Refresh();
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => Refresh();
        _poll.Start();
    }

    internal IReadOnlyList<PinnedApp> Pins => _pins;

    internal event EventHandler? Changed;

    /// <summary>
    /// <c>%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar</c>.
    /// </summary>
    internal static string PinnedDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    internal void Refresh() => Apply(ReadPins(PinnedDirectory, ReadFavorites()));

    /// <summary>
    /// Taskbar order is the first UTF-16 hit of each <c>.lnk</c> file name inside the
    /// Taskband <c>Favorites</c> blob. Names the blob does not mention follow, by file name.
    /// </summary>
    internal static IReadOnlyList<string> OrderLinks(IReadOnlyList<string> links, ReadOnlySpan<byte> favorites)
    {
        // Rank before LINQ: a ReadOnlySpan cannot be captured by the Select lambda.
        var ranked = new (string path, int index, int rank)[links.Count];
        for (int i = 0; i < links.Count; i++)
        {
            ranked[i] = (links[i], i, Rank(favorites, links[i]));
        }

        return ranked
            .OrderBy(static item => item.rank == int.MaxValue ? 1 : 0)
            .ThenBy(static item => item.rank)
            .ThenBy(static item => Path.GetFileName(item.path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.index)
            .Select(static item => item.path)
            .ToArray();
    }

    /// <summary>
    /// Turns an ordered shortcut list into pin entries. A folder target stays the folder.
    /// An unresolved shortcut keeps the <c>.lnk</c> path so a closed Store pin still appears.
    /// </summary>
    internal static PinnedApp PinFromShortcut(string lnkPath, ShortcutLaunch launch)
    {
        string label = string.IsNullOrWhiteSpace(launch.Label)
            ? Path.GetFileNameWithoutExtension(lnkPath)
            : launch.Label.Trim();
        if (label.Length == 0)
        {
            label = Path.GetFileNameWithoutExtension(lnkPath);
        }

        if (!launch.Resolved || string.IsNullOrWhiteSpace(launch.Path))
        {
            return new PinnedApp(lnkPath, string.Empty, label);
        }

        string target = DockItemStore.Normalize(launch.Path);
        if (Directory.Exists(target))
        {
            return new PinnedApp(target, string.Empty, label);
        }

        if (IsExplorerImage(target) && TryDirectoryArgument(launch.Args, out string folder))
        {
            return new PinnedApp(folder, string.Empty, label);
        }

        return new PinnedApp(target, launch.Args?.Trim() ?? string.Empty, label);
    }

    internal static IReadOnlyList<PinnedApp> ReadPins(string directory, ReadOnlySpan<byte> favorites)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        string[] links;
        try
        {
            links = Directory.EnumerateFiles(directory, "*.lnk").ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"WharfBar taskbar pin scan failed: {ex.Message}");
            return [];
        }

        var pins = new List<PinnedApp>(links.Length);
        foreach (string link in OrderLinks(links, favorites))
        {
            pins.Add(PinFromShortcut(link, ResolveLaunch(link)));
        }

        return pins;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _poll?.Stop();
        _poll = null;
        _debounce?.Stop();
        _debounce = null;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void Apply(IReadOnlyList<PinnedApp> next)
    {
        if (_disposed)
        {
            return;
        }

        // Start the watcher even when the folder is empty, or the first pin would wait
        // for the 15 s poll.
        EnsureWatcher();
        if (PinsEqual(_pins, next))
        {
            return;
        }

        _pins.Clear();
        _pins.AddRange(next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureWatcher()
    {
        if (_watcher is not null || _disposed || !Directory.Exists(PinnedDirectory))
        {
            return;
        }

        _watcher = new FileSystemWatcher(PinnedDirectory, "*.lnk")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
        };
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Changed += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(ScheduleRefresh);
    }

    private void ScheduleRefresh()
    {
        if (_disposed)
        {
            return;
        }

        _debounce ??= new DispatcherTimer { Interval = Debounce };
        _debounce.Stop();
        _debounce.Tick -= OnDebounce;
        _debounce.Tick += OnDebounce;
        _debounce.Start();
    }

    private void OnDebounce(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        Refresh();
    }

    private static ShortcutLaunch ResolveLaunch(string lnkPath)
    {
        if (!ShortcutResolver.TryResolve(lnkPath, out string path, out string args, out string label))
        {
            return new ShortcutLaunch(false, string.Empty, string.Empty, label);
        }

        return new ShortcutLaunch(true, path, args, label);
    }

    private static byte[] ReadFavorites()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband");
            return key?.GetValue("Favorites") as byte[] ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar taskbar pin order read failed: {ex.Message}");
            return [];
        }
    }

    private static int Rank(ReadOnlySpan<byte> favorites, string path)
    {
        string name = Path.GetFileName(path);
        int index = IndexOfUtf16(favorites, name);
        return index < 0 ? int.MaxValue : index;
    }

    private static int IndexOfUtf16(ReadOnlySpan<byte> haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle) || haystack.Length < needle.Length * 2)
        {
            return -1;
        }

        byte[] raw = Encoding.Unicode.GetBytes(needle);
        for (int i = 0; i + raw.Length <= haystack.Length; i += 2)
        {
            if (haystack.Slice(i, raw.Length).SequenceEqual(raw))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsExplorerImage(string path) =>
        string.Equals(Path.GetFileName(path), "explorer.exe", StringComparison.OrdinalIgnoreCase);

    private static bool TryDirectoryArgument(string? args, out string folder)
    {
        folder = string.Empty;
        if (string.IsNullOrWhiteSpace(args))
        {
            return false;
        }

        foreach (string candidate in ArgumentCandidates(args))
        {
            if (!Path.IsPathRooted(candidate))
            {
                continue;
            }

            string full = DockItemStore.Normalize(candidate);
            if (Directory.Exists(full))
            {
                folder = full;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ArgumentCandidates(string args)
    {
        string trimmed = args.Trim();
        yield return trimmed.Trim('"');

        int start = 0;
        while ((start = trimmed.IndexOf('"', start)) >= 0)
        {
            int end = trimmed.IndexOf('"', start + 1);
            if (end < 0)
            {
                yield break;
            }

            yield return trimmed[(start + 1)..end];
            start = end + 1;
        }
    }

    private static bool PinsEqual(List<PinnedApp> a, IReadOnlyList<PinnedApp> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Path, b[i].Path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Args, b[i].Args, StringComparison.Ordinal)
                || !string.Equals(a[i].Label, b[i].Label, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
