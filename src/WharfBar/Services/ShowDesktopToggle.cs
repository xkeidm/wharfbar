namespace WharfBar.Services;

/// <summary>What one click on the Show Desktop sliver did.</summary>
internal enum ShowDesktopAction
{
    Minimize,
    Restore,
}

/// <summary>Result of one toggle step: the action taken and how many windows it touched.</summary>
internal readonly record struct ShowDesktopResult(ShowDesktopAction Action, int Affected);

/// <summary>
/// Window source the pure toggle works through. The Win32 implementation lives in
/// <see cref="ShowDesktopWindowOps"/>; tests pass an in-memory fake.
/// </summary>
internal interface IShowDesktopWindows
{
    /// <summary>Visible, non-minimized user windows, in z-order (top first).</summary>
    IReadOnlyList<IntPtr> MinimizableWindows();

    bool IsAlive(IntPtr hwnd);

    void Minimize(IntPtr hwnd);

    void Restore(IntPtr hwnd);
}

/// <summary>
/// The Show Desktop toggle state machine, kept free of Win32 so it can be unit tested.
/// <para>
/// Click 1 records every top-level window that is showing and minimizes it; click 2
/// restores exactly the recorded list (closed windows are skipped) and the cycle
/// repeats. Windows the user minimized themselves are never in the recorded list, so
/// the restore does not undo the user's own choices.
/// </para>
/// </summary>
internal sealed class ShowDesktopToggle
{
    private List<IntPtr> _saved = [];

    /// <summary>True between the minimizing click and its matching restore click.</summary>
    internal bool IsOnDesktop { get; private set; }

    internal ShowDesktopResult Click(IShowDesktopWindows windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        return IsOnDesktop ? RestoreClick(windows) : MinimizeClick(windows);
    }

    private ShowDesktopResult MinimizeClick(IShowDesktopWindows windows)
    {
        IReadOnlyList<IntPtr> targets = windows.MinimizableWindows();
        for (int i = 0; i < targets.Count; i++)
        {
            windows.Minimize(targets[i]);
        }

        // A fresh list: the caller may reuse — and later mutate — the collection it returned.
        _saved = [.. targets];
        IsOnDesktop = true;
        return new ShowDesktopResult(ShowDesktopAction.Minimize, _saved.Count);
    }

    private ShowDesktopResult RestoreClick(IShowDesktopWindows windows)
    {
        List<IntPtr> saved = _saved;
        _saved = [];
        IsOnDesktop = false;

        int affected = 0;
        // Reverse z-order: the previously topmost window is restored last and lands on top.
        for (int i = saved.Count - 1; i >= 0; i--)
        {
            IntPtr hwnd = saved[i];
            if (!windows.IsAlive(hwnd))
            {
                continue;
            }

            windows.Restore(hwnd);
            affected++;
        }

        return new ShowDesktopResult(ShowDesktopAction.Restore, affected);
    }
}
