using System.Diagnostics;
using System.Windows.Threading;
using WharfBar.Native;
using WharfBar.Views;

namespace WharfBar.Services;

/// <summary>
/// A bare Windows key tap opens (or closes) the Launchpad while every Win+key shortcut keeps working.
/// The shell owns the Win-key hotkey — <c>RegisterHotKey(..., MOD_WIN, 0)</c> comes back with
/// "already registered" — so the key is intercepted with a low-level hook instead: both physical Win
/// key events are swallowed and the hook synthesizes the key only for chords. The trade-offs are
/// documented in docs/04-gotchas.md.
/// </summary>
internal sealed class WindowsKeyLauncher : IDisposable
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromSeconds(2);

    private readonly ConfigService _config;
    private readonly Dispatcher _dispatcher;
    private readonly WindowsKeyGesture _gesture = new();
    private LowLevelKeyboardHook? _hook;
    private bool _disposed;

    internal WindowsKeyLauncher(ConfigService config, Dispatcher dispatcher)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// Installs the hook. False when Windows refuses it (a restricted launch or security software);
    /// the app keeps running without the Windows-key shortcut.
    /// </summary>
    internal bool Start()
    {
        if (_hook is not null)
        {
            return true;
        }

        var hook = new LowLevelKeyboardHook(OnKey);
        if (!hook.Install(InstallTimeout))
        {
            hook.Dispose();
            return false;
        }

        _hook = hook;
        return true;
    }

    /// <summary>Runs on the hook thread; must never block the input queue.</summary>
    private bool OnKey(uint virtualKey, bool keyUp, bool injected)
    {
        if (_disposed)
        {
            return false;
        }

        switch (_gesture.OnKey(virtualKey, keyUp, injected))
        {
            case WindowsKeyAction.Swallow:
                return true;

            case WindowsKeyAction.SwallowAndOpenLaunchpad:
                // The UI work goes to the dispatcher; the hook only answers "swallow".
                _dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(OpenOrCloseLaunchpad));
                return true;

            case WindowsKeyAction.SwallowAndInjectWinUp:
                if (!SendInputKeys.TrySendKeyUp((ushort)virtualKey))
                {
                    Debug.WriteLine("WharfBar: keyboard hook could not release the synthesized Win key");
                }

                return true;

            case WindowsKeyAction.InjectWinDownAndPass:
                if (SendInputKeys.TrySendKeyDown((ushort)_gesture.HeldKey))
                {
                    return false;
                }

                // Never let the chord key through bare: the user asked for Win+<key>, not <key>.
                Debug.WriteLine("WharfBar: keyboard hook could not press the synthesized Win key");
                return true;

            default:
                return false;
        }
    }

    private void OpenOrCloseLaunchpad()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            LaunchpadWindow.Toggle(_config.Current);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar win-key launchpad: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gesture.Reset();
        _hook?.Dispose();
        _hook = null;
    }
}
