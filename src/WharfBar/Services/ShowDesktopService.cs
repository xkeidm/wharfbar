using System.Diagnostics;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// The Show Desktop sliver: one process-wide toggle fed by the Win32 window source.
/// State is static, so every monitor's top bar clicks flip the same machine —
/// "put everything down" is a single desktop-wide concept, like Windows' sliver.
/// </summary>
internal static class ShowDesktopService
{
    private static readonly ShowDesktopWindowOps Ops = new();
    private static readonly object ClickGate = new();

    internal static ShowDesktopToggle Toggle { get; } = new();

    /// <summary>
    /// One click from a bar. Calls from the WPF UI thread are moved to the pool:
    /// the enumeration pass is <c>EnumWindows</c>, and one pass runs against hung
    /// windows without stalling the AppBar (see WindowEnumeration).
    /// </summary>
    internal static void ClickFromUi()
    {
        if (Dispatcher.FromThread(Thread.CurrentThread) is not null)
        {
            ThreadPool.QueueUserWorkItem(_ => RunClick());
        }
        else
        {
            RunClick();
        }
    }

    /// <summary>
    /// A double-click queues two pool items; the gate hands them to the toggle one
    /// at a time so the phase cannot interleave.
    /// </summary>
    private static void RunClick()
    {
        try
        {
            lock (ClickGate)
            {
                _ = Toggle.Click(Ops);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar show-desktop click failed: {ex.Message}");
        }
    }
}

/// <summary><see cref="IShowDesktopWindows"/> implemented over <see cref="ShowDesktopNative"/>.</summary>
internal sealed class ShowDesktopWindowOps : IShowDesktopWindows
{
    public IReadOnlyList<IntPtr> MinimizableWindows() => ShowDesktopNative.GetMinimizableWindows();

    public bool IsAlive(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);

    public void Minimize(IntPtr hwnd) => ShowDesktopNative.Minimize(hwnd);

    public void Restore(IntPtr hwnd) => ShowDesktopNative.Restore(hwnd);
}
