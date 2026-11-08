using System;
using System.Diagnostics;
using System.Windows.Interop;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Message-only window that listens for clipboard changes. The same pattern as the existing
/// <see cref="MonitorMessageWindow"/>: invisible on the desktop, receives messages only. The window
/// is created on the UI thread; the listener receives <c>WM_CLIPBOARDUPDATE</c> there, and reading the content is the caller's responsibility.
/// </summary>
internal sealed class ClipboardMessageWindow : IDisposable
{
    private HwndSource? _source;
    private bool _disposed;

    /// <summary>Clipboard content changed. Raised on the UI thread.</summary>
    internal event EventHandler? ClipboardChanged;

    internal ClipboardMessageWindow()
    {
        var parameters = new HwndSourceParameters("WharfBarClipboard")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            // Message-only window: no desktop presence, receives WM_CLIPBOARDUPDATE.
            ParentWindow = new IntPtr(-3),
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        if (!ClipboardNative.AddListener(_source.Handle))
        {
            Debug.WriteLine("[WharfBar] clipboard format listener registration failed.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == ClipboardNative.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            try
            {
                ClipboardChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WharfBar] clipboard change handler failed: {ex.Message}");
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_source is not null)
        {
            ClipboardNative.RemoveListener(_source.Handle);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
