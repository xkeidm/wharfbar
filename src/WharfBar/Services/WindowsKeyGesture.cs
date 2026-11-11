namespace WharfBar.Services;

/// <summary>What the Win-key hook does with one keyboard event.</summary>
internal enum WindowsKeyAction
{
    /// <summary>Let the event through untouched.</summary>
    Pass,

    /// <summary>Block the event; another decision was already taken for this key.</summary>
    Swallow,

    /// <summary>A bare Windows key tap: block it and open (or close) the Launchpad.</summary>
    SwallowAndOpenLaunchpad,

    /// <summary>End of a synthesized Win chord: block the physical key-up after injecting one.</summary>
    SwallowAndInjectWinUp,

    /// <summary>A chord key arrived while the Windows key is held: synthesize the Win key-down, then pass.</summary>
    InjectWinDownAndPass,
}

/// <summary>
/// PURE decision table for "the Windows key opens the Launchpad". Both physical Win key events of a tap
/// are swallowed, so the shell's own Win-key hotkey never arms and the Start menu stays closed. A chord
/// key (Win+E, Win+Tab, Win+Shift+S) makes the hook synthesize the Win key-down first and the key-up on
/// release, which keeps every Windows shortcut working. Injected events are never touched: the app
/// itself synthesizes Win+Tab for the dock Task View tile and Win+B/N/P for the tray and flyouts.
/// </summary>
internal sealed class WindowsKeyGesture
{
    private const uint VkLWin = 0x5B;
    private const uint VkRWin = 0x5C;

    private uint _heldKey;
    private bool _injectedDown;

    /// <summary>The physical Windows key currently held, or 0.</summary>
    internal uint HeldKey => _heldKey;

    internal static bool IsWindowsKey(uint virtualKey) => virtualKey is VkLWin or VkRWin;

    internal WindowsKeyAction OnKey(uint virtualKey, bool keyUp, bool injected)
    {
        if (IsWindowsKey(virtualKey))
        {
            if (injected)
            {
                return WindowsKeyAction.Pass;
            }

            if (!keyUp)
            {
                // Auto-repeat while held: already swallowed once.
                return _heldKey != 0
                    ? WindowsKeyAction.Swallow
                    : BeginHold(virtualKey);
            }

            return EndHold(virtualKey);
        }

        if (keyUp || injected || _heldKey == 0 || _injectedDown)
        {
            return WindowsKeyAction.Pass;
        }

        _injectedDown = true;
        return WindowsKeyAction.InjectWinDownAndPass;
    }

    private WindowsKeyAction BeginHold(uint virtualKey)
    {
        _heldKey = virtualKey;
        _injectedDown = false;
        return WindowsKeyAction.Swallow;
    }

    private WindowsKeyAction EndHold(uint virtualKey)
    {
        if (_heldKey == 0)
        {
            // A Win key-up we never saw going down (hook installed mid-press): pass it on.
            return WindowsKeyAction.Pass;
        }

        if (_heldKey != virtualKey)
        {
            // The other Windows key came up first; keep the state and drop the event.
            return WindowsKeyAction.Swallow;
        }

        _heldKey = 0;
        if (!_injectedDown)
        {
            return WindowsKeyAction.SwallowAndOpenLaunchpad;
        }

        _injectedDown = false;
        return WindowsKeyAction.SwallowAndInjectWinUp;
    }

    /// <summary>Forgets the held key (hook teardown or a lost key-up).</summary>
    internal void Reset()
    {
        _heldKey = 0;
        _injectedDown = false;
    }
}
