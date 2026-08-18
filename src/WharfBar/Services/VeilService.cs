using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Views;

namespace WharfBar.Services;

/// <summary>
/// Owns the full-screen color veil overlay: state, fade, hotkey registration, config persistence.
/// </summary>
internal sealed class VeilService : IDisposable
{
    private const int HotkeyId = 0x5645; // "VE"

    private readonly ConfigService _config;
    private VeilWindow? _window;
    private HwndSource? _hotkeySource;
    private bool _hotkeyRegistered;
    private bool _disposed;

    internal VeilService(ConfigService config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    internal bool IsEnabled { get; private set; }

    /// <summary>Hotkey registration failure text; null when no hotkey or registration succeeded.</summary>
    internal string? HotkeyRegistrationError { get; private set; }

    internal event EventHandler? Changed;

    /// <summary>Applies config (startup, settings page). Always recreates window state.</summary>
    internal void ApplyConfig(VeilConfig cfg)
    {
        EnsureWindow();
        Color color = VeilNormalizer.TryParseColor(cfg.Color, out Color parsed)
            ? parsed
            : DefaultColor;
        _window!.ApplyTint(color, VeilNormalizer.ClampOpacity(cfg.Opacity));
        SetEnabledCore(cfg.Enabled, animate: true);
        ReconfigureHotkey(cfg.HotKey);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Toggles the veil (QS tile, hotkey); persists Enabled to config.</summary>
    internal void SetEnabled(bool enabled)
    {
        SetEnabledCore(enabled, animate: true);
        VeilConfig cfg = _config.Current.Veil ??= new VeilConfig();
        cfg.Enabled = enabled;
        _config.Save(_config.Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterHotkey();
        _hotkeySource?.RemoveHook(WndProc);
        _hotkeySource?.Dispose();
        _hotkeySource = null;
        _window?.Close();
        _window = null;
    }

    private void SetEnabledCore(bool enabled, bool animate)
    {
        IsEnabled = enabled;
        _window?.SetVisible(enabled, animate);
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new VeilWindow();
        _window.Show();
    }

    private void ReconfigureHotkey(string? hotkeyText)
    {
        UnregisterHotkey();
        HotkeyRegistrationError = null;

        if (!HotkeyParser.TryParse(hotkeyText, out uint modifiers, out uint vk))
        {
            return; // boş veya geçersiz → kısayol yok (soft alan)
        }

        if (_hotkeySource is null)
        {
            var parameters = new HwndSourceParameters("WharfBarVeilHotkey")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = 0,
                // Message-only window: no desktop presence, receives WM_HOTKEY.
                ParentWindow = new IntPtr(-3),
            };
            _hotkeySource = new HwndSource(parameters);
            _hotkeySource.AddHook(WndProc);
        }

        if (HotkeyNative.RegisterHotKey(_hotkeySource.Handle, HotkeyId, modifiers, vk))
        {
            _hotkeyRegistered = true;
        }
        else
        {
            HotkeyRegistrationError = "Kısayol kullanılamıyor (başka bir uygulama tarafından kullanılıyor olabilir).";
            Debug.WriteLine("[WharfBar] Veil hotkey registration failed.");
        }
    }

    private void UnregisterHotkey()
    {
        if (!_hotkeyRegistered)
        {
            return;
        }

        HotkeyNative.UnregisterHotKey(_hotkeySource?.Handle ?? IntPtr.Zero, HotkeyId);
        _hotkeyRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyNative.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            SetEnabled(!IsEnabled);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static Color DefaultColor =>
        VeilNormalizer.TryParseColor(VeilConfig.DefaultColor, out Color color) ? color : Colors.Blue;
}
