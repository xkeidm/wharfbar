using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// Veil (color overlay) settings: enable, color palette + hex, opacity, hotkey.
/// Every change saves config and applies live via VeilService.ApplyConfig.
/// </summary>
public partial class VeilPage : Page
{
    private static readonly (string Name, string Hex)[] PresetColors =
    [
        ("Mavi", "#4A90D9"),
        ("Yeşil", "#7CB342"),
        ("Pembe", "#E57373"),
        ("Turuncu", "#E6A23C"),
        ("Sarı", "#D4C24A"),
        ("Mor", "#9E7BB5"),
        ("Gri", "#8E8E8E"),
        ("Koyu", "#2B2B2B"),
    ];

    private bool _isLoading = true;
    private bool _capturingHotkey;
    private bool _updatingColorHex;

    public VeilPage()
    {
        InitializeComponent();
        BuildSwatches();
        LoadSettings();

        VeilEnabledSwitch.Checked += OnEnabledChanged;
        VeilEnabledSwitch.Unchecked += OnEnabledChanged;
        OpacitySlider.ValueChanged += OnOpacityChanged;
        ColorHexBox.TextChanged += OnColorHexChanged;
        CaptureHotkeyButton.Click += OnCaptureHotkeyClick;
        RemoveHotkeyButton.Click += OnRemoveHotkeyClick;
        ResetHotkeyButton.Click += OnResetHotkeyClick;
        PreviewKeyDown += OnPageKeyDown;

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    private void BuildSwatches()
    {
        foreach ((string name, string hex) in PresetColors)
        {
            var button = new Button
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 8, 8),
                Tag = hex,
                ToolTip = name,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            button.Click += (_, _) => ApplyColor(hex);
            ColorSwatches.Children.Add(button);
        }
    }

    private void LoadSettings()
    {
        VeilConfig veil = Cfg.Veil;
        VeilEnabledSwitch.IsChecked = veil.Enabled;
        OpacitySlider.Value = VeilNormalizer.ClampOpacity(veil.Opacity);
        OpacityLabel.Text = $"{VeilNormalizer.ClampOpacity(veil.Opacity)}%";
        _updatingColorHex = true;
        ColorHexBox.Text = VeilNormalizer.NormalizeColor(veil.Color).ToUpperInvariant();
        _updatingColorHex = false;
        UpdateHotkeyUi(veil.HotKey);
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Veil.Enabled = VeilEnabledSwitch.IsChecked == true;
        SaveAndApply();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        OpacityLabel.Text = $"{v}%";
        if (_isLoading)
        {
            return;
        }

        Cfg.Veil.Opacity = VeilNormalizer.ClampOpacity(v);
        SaveAndApply();
    }

    private void OnColorHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _updatingColorHex)
        {
            return;
        }

        if (VeilNormalizer.TryParseColor(ColorHexBox.Text, out _))
        {
            ColorErrorText.Visibility = Visibility.Collapsed;
            Cfg.Veil.Color = VeilNormalizer.NormalizeColor(ColorHexBox.Text);
            SaveAndApply();
        }
        else
        {
            ColorErrorText.Visibility = Visibility.Visible;
        }
    }

    private void ApplyColor(string hex)
    {
        string normalized = VeilNormalizer.NormalizeColor(hex);
        _updatingColorHex = true;
        ColorHexBox.Text = normalized.ToUpperInvariant();
        _updatingColorHex = false;
        ColorErrorText.Visibility = Visibility.Collapsed;
        Cfg.Veil.Color = normalized;
        SaveAndApply();
    }

    private void OnCaptureHotkeyClick(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        CaptureHotkeyHint.Visibility = Visibility.Visible;
    }

    private void OnRemoveHotkeyClick(object sender, RoutedEventArgs e)
    {
        Cfg.Veil.HotKey = string.Empty;
        SaveAndApply();
        UpdateHotkeyUi(string.Empty);
    }

    private void OnResetHotkeyClick(object sender, RoutedEventArgs e)
    {
        Cfg.Veil.HotKey = VeilConfig.DefaultHotKey;
        SaveAndApply();
        UpdateHotkeyUi(VeilConfig.DefaultHotKey);
    }

    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey)
        {
            return;
        }

        e.Handled = true;
        if (e.Key is Key.Escape or Key.Enter)
        {
            EndCapture();
            return;
        }

        if (!TryMapKey(e.Key, out uint vk))
        {
            return;
        }

        uint modifiers = MapModifiers(Keyboard.Modifiers);
        if (modifiers == 0)
        {
            return; // en az bir modifier zorunlu
        }

        string hotkey = HotkeyParser.Format(modifiers, vk);
        Cfg.Veil.HotKey = hotkey;
        SaveAndApply();
        EndCapture();
        UpdateHotkeyUi(hotkey);
    }

    private void EndCapture()
    {
        _capturingHotkey = false;
        CaptureHotkeyHint.Visibility = Visibility.Collapsed;
    }

    private void UpdateHotkeyUi(string hotkey)
    {
        HotkeyLabel.Text = string.IsNullOrEmpty(hotkey) ? "Kısayol yok" : hotkey;
        string? error = WharfApp.Veil?.HotkeyRegistrationError;
        HotkeyErrorText.Text = error ?? string.Empty;
        HotkeyErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        WharfApp.Veil?.ApplyConfig(Cfg.Veil);
    }

    private static bool TryMapKey(Key key, out uint vk)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            vk = (uint)(key - Key.A + 0x41);
            return true;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            vk = (uint)(key - Key.D0 + 0x30);
            return true;
        }

        if (key is >= Key.F1 and <= Key.F12)
        {
            vk = (uint)(key - Key.F1 + 0x70);
            return true;
        }

        vk = 0;
        return false;
    }

    private static uint MapModifiers(ModifierKeys modifiers)
    {
        uint m = 0;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            m |= HotkeyNative.MOD_CONTROL;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            m |= HotkeyNative.MOD_ALT;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            m |= HotkeyNative.MOD_SHIFT;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            m |= HotkeyNative.MOD_WIN;
        }

        return m;
    }
}
