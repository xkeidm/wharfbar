using System.Windows.Media.Imaging;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Holds a minimize <c>PrintWindow</c> frame so dock restore can reverse the cue.
/// </summary>
internal sealed class DockRestoreSnapshotCache
{
    internal const int MaxEntries = 32;

    private readonly Dictionary<nint, Entry> _byHwnd = new();
    private readonly LinkedList<nint> _order = new();

    internal int Count => _byHwnd.Count;

    internal void Put(IntPtr hwnd, BitmapSource shot, RECT restorePx)
    {
        if (hwnd == IntPtr.Zero || shot is null)
        {
            return;
        }

        nint key = hwnd;
        if (_byHwnd.ContainsKey(key))
        {
            _order.Remove(key);
        }

        _byHwnd[key] = new Entry(shot, restorePx);
        _order.AddLast(key);

        while (_byHwnd.Count > MaxEntries && _order.First is not null)
        {
            nint oldest = _order.First.Value;
            _order.RemoveFirst();
            _byHwnd.Remove(oldest);
        }
    }

    internal bool TryGet(IntPtr hwnd, out BitmapSource shot, out RECT restorePx)
    {
        shot = null!;
        restorePx = default;
        if (hwnd == IntPtr.Zero || !_byHwnd.TryGetValue(hwnd, out Entry? entry))
        {
            return false;
        }

        shot = entry.Shot;
        restorePx = entry.RestorePx;
        return true;
    }

    internal void Remove(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        nint key = hwnd;
        if (_byHwnd.Remove(key))
        {
            _order.Remove(key);
        }
    }

    internal void Clear()
    {
        _byHwnd.Clear();
        _order.Clear();
    }

    private sealed record Entry(BitmapSource Shot, RECT RestorePx);
}
