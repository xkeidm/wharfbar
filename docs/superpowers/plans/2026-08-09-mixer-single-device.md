# Single-Device Mixer UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** QS Mixer sayfasını tek seçili çıkış cihazı + dropdown, master slider ve seçili aygıttaki uygulama oturumları düzenine çevirmek (WharfBar varsayılanı).

**Architecture:** `AudioMixerService` snapshot’ı çoklu `MixerDeviceSection` bloklarından `Devices` + `Selected` + `Sessions` modeline sadeleşir. `QuickSettingsFlyout` Mixer XAML/code-behind EarTrumpet-benzeri tek kolon UI çizer; aygıt seçimi `TrySetDefaultDevice` ile sistem default’unu günceller. Tek QS host (ADR-006) korunur.

**Tech Stack:** .NET 8 WPF, mevcut Core Audio native katmanı; yeni paket yok.

**Spec:** [`docs/superpowers/specs/2026-08-09-mixer-single-device-design.md`](../specs/2026-08-09-mixer-single-device-design.md)

## Global Constraints

- Mica + `AllowsTransparency=true` yasak
- P/Invoke yalnızca `Native/` `internal`
- `Views/` tercih; `global.json` yok
- ADR-005: yabancı tepsi ikon aynası yok
- EarTrumpet mor teması yok — Fluent / WPF-UI dinamik fırçalar
- Ayrı `VolumeMixerPopup` yok
- Commit yalnızca kullanıcı isterse
- Derleme: `dotnet build WharfBar.sln` (`dotnet run` AppBar riski — manuel doğrulama kullanıcıda)

---

## File map

| File | Role |
|------|------|
| `src/WharfBar/Services/AudioMixerService.cs` | Yeni snapshot modeli; seçili aygıt oturumları |
| `src/WharfBar/Views/QuickSettingsFlyout.xaml` | Mixer sayfası: cihaz satırı + master + oturum listesi |
| `src/WharfBar/Views/QuickSettingsFlyout.xaml.cs` | Dropdown, master, oturum UI; çoklu-blok kodu kaldır |
| `docs/decisions/006-quick-settings-flyout.md` | Mixer UX → tek aygıt + dropdown |
| `docs/01-architecture.md` | Mixer özeti |
| `.agent/project.md` | Ürün özeti mixer satırı |
| `docs/superpowers/specs/2026-08-09-mixer-single-device-design.md` | Durum → plan linki |

---

### Task 1: `AudioMixerService` snapshot sadeleştirmesi

**Files:**
- Modify: `src/WharfBar/Services/AudioMixerService.cs`

**Interfaces:**
- Produces:
  ```csharp
  internal sealed record MixerDeviceInfo(
      string DeviceId,
      string Name,
      bool IsDefault,
      int Percent,
      bool IsMuted);

  internal sealed record MixerSnapshot(
      VolumeSnapshot Master,
      IReadOnlyList<MixerDeviceInfo> Devices,
      MixerDeviceInfo? Selected,
      IReadOnlyList<AudioAppSession> Sessions);
  ```
- Produces: `TrySetDefaultDevice(string deviceId)`, `SetMasterPercent` / `ToggleMasterMute`, `TrySetSessionPercent` / `TrySetSessionMuted` (aynı imzalar)
- Removes: `MixerDeviceSection`, `TrySetDevicePercent(string deviceId, …)`, `TryToggleDeviceMute(string deviceId, …)` — master artık her zaman varsayılan aygıt

- [ ] **Step 1: Replace records and `Read()`**

Replace the top of `AudioMixerService.cs` (records + class body methods that reference `MixerDeviceSection`) with:

```csharp
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed record MixerDeviceInfo(
    string DeviceId,
    string Name,
    bool IsDefault,
    int Percent,
    bool IsMuted);

internal sealed record MixerSnapshot(
    VolumeSnapshot Master,
    IReadOnlyList<MixerDeviceInfo> Devices,
    MixerDeviceInfo? Selected,
    IReadOnlyList<AudioAppSession> Sessions);

/// <summary>
/// Single-device mixer: device list for picker, master on default render,
/// sessions on the selected (default) device.
/// </summary>
internal sealed class AudioMixerService : IDisposable
{
    private readonly VolumeService _volume;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    internal AudioMixerService(VolumeService volume)
    {
        _volume = volume ?? throw new ArgumentNullException(nameof(volume));
        Snapshot = Read();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _timer.Tick += (_, _) => Refresh();
    }

    internal MixerSnapshot Snapshot { get; private set; }

    internal event EventHandler? Changed;

    internal void StartPolling()
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        _timer.Start();
    }

    internal void StopPolling() => _timer.Stop();

    internal void SetMasterPercent(int percent) => _volume.SetPercent(percent);

    internal void SetMasterMuted(bool muted) => _volume.SetMuted(muted);

    internal void ToggleMasterMute() => _volume.ToggleMute();

    internal bool TrySetDefaultDevice(string deviceId)
    {
        if (!CoreAudioDevices.TrySetDefaultRenderDevice(deviceId))
        {
            return false;
        }

        Refresh();
        return true;
    }

    internal bool TrySetSessionPercent(string sessionInstanceId, int percent)
    {
        if (!CoreAudioSessions.TrySetSessionVolume(sessionInstanceId, percent / 100f))
        {
            return false;
        }

        Refresh();
        return true;
    }

    internal bool TrySetSessionMuted(string sessionInstanceId, bool muted)
    {
        if (!CoreAudioSessions.TrySetSessionMute(sessionInstanceId, muted))
        {
            return false;
        }

        Refresh();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }

    private void Refresh()
    {
        Snapshot = Read();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MixerSnapshot Read()
    {
        IReadOnlyList<AudioOutputDevice> devices = CoreAudioDevices.ListActiveRenderDevices();
        var infos = devices
            .Select(d => new MixerDeviceInfo(d.Id, d.Name, d.IsDefault, d.Percent, d.IsMuted))
            .OrderByDescending(d => d.IsDefault)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        MixerDeviceInfo? selected = infos.FirstOrDefault(d => d.IsDefault) ?? infos.FirstOrDefault();
        IReadOnlyList<AudioAppSession> sessions = selected is null
            ? Array.Empty<AudioAppSession>()
            : CoreAudioSessions.ListSessionsOnDevice(selected.DeviceId);

        return new MixerSnapshot(_volume.Snapshot, infos, selected, sessions);
    }
}
```

- [ ] **Step 2: Build (expect UI compile errors until Task 2)**

Run: `dotnet build WharfBar.sln -c Debug --nologo`

Expected: FAIL — `QuickSettingsFlyout.xaml.cs` still references `MixerDeviceSection` / `TrySetDevicePercent` / `TryToggleDeviceMute`. That is intentional; fix in Task 2.

If WharfBar.exe is locked, stop the running instance gracefully first (not `-Force`), then rebuild.

- [ ] **Step 3: Commit (only if user asked)**

```bash
git add src/WharfBar/Services/AudioMixerService.cs
git commit -m "refactor(mixer): reshape snapshot to single selected device."
```

---

### Task 2: Mixer UI — XAML iskeleti

**Files:**
- Modify: `src/WharfBar/Views/QuickSettingsFlyout.xaml` (Mixer page region only)

**Interfaces:**
- Consumes: none (markup names for code-behind)
- Produces named elements: `DevicePickerButton`, `SelectedDeviceNameText`, `DevicePickerPopup`, `DevicePickerList`, `MixerMasterMuteButton`, `MixerMasterSlider`, `MixerMasterPercentText`, `MixerSessionList`

- [ ] **Step 1: Replace the Mixer page `Grid` in XAML**

Replace the existing `<!-- MIXER ... -->` `Grid x:Name="MixerPage"` block with:

```xml
<!-- MIXER (single device + dropdown + sessions) -->
<Grid x:Name="MixerPage" Visibility="Collapsed">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <DockPanel Grid.Row="0" Margin="0,0,0,12">
        <Button Style="{StaticResource QsIconButton}"
                Content="&#xE76B;"
                Click="OnBackToMainClick"
                ToolTip="Geri" />
        <TextBlock Margin="8,0,0,0"
                   VerticalAlignment="Center"
                   FontWeight="SemiBold"
                   FontSize="14"
                   Text="Ses karıştırıcı" />
    </DockPanel>

    <StackPanel Grid.Row="1" Margin="0,0,0,10">
        <TextBlock FontSize="11"
                   Opacity="0.7"
                   Margin="2,0,0,4"
                   Text="Çıkış cihazı" />
        <Grid>
            <Border x:Name="DevicePickerButton"
                    Background="{DynamicResource ControlFillColorDefaultBrush}"
                    CornerRadius="8"
                    Padding="12,10"
                    Cursor="Hand"
                    MouseLeftButtonUp="OnDevicePickerClick">
                <DockPanel>
                    <TextBlock DockPanel.Dock="Right"
                               FontFamily="Segoe MDL2 Assets"
                               FontSize="12"
                               VerticalAlignment="Center"
                               Opacity="0.8"
                               Text="&#xE70D;" />
                    <TextBlock x:Name="SelectedDeviceNameText"
                               FontSize="13"
                               FontWeight="SemiBold"
                               TextTrimming="CharacterEllipsis"
                               VerticalAlignment="Center"
                               Text="—" />
                </DockPanel>
            </Border>
            <Popup x:Name="DevicePickerPopup"
                   PlacementTarget="{Binding ElementName=DevicePickerButton}"
                   Placement="Bottom"
                   StaysOpen="False"
                   AllowsTransparency="False">
                <Border Background="{DynamicResource ApplicationBackgroundBrush}"
                        BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                        BorderThickness="1"
                        CornerRadius="8"
                        MinWidth="{Binding ActualWidth, ElementName=DevicePickerButton}"
                        MaxHeight="220"
                        Padding="4">
                    <ScrollViewer VerticalScrollBarVisibility="Auto">
                        <StackPanel x:Name="DevicePickerList" />
                    </ScrollViewer>
                </Border>
            </Popup>
        </Grid>
    </StackPanel>

    <Border Grid.Row="2"
            Margin="0,0,0,10"
            Background="{DynamicResource ControlFillColorDefaultBrush}"
            CornerRadius="8"
            Padding="10,8">
        <DockPanel LastChildFill="True">
            <Button x:Name="MixerMasterMuteButton"
                    DockPanel.Dock="Left"
                    Style="{StaticResource QsIconButton}"
                    Content="&#xE767;"
                    Click="OnMixerMasterMuteClick"
                    ToolTip="Sessiz" />
            <TextBlock x:Name="MixerMasterPercentText"
                       DockPanel.Dock="Right"
                       Width="28"
                       VerticalAlignment="Center"
                       TextAlignment="Right"
                       FontSize="12"
                       Text="0" />
            <Slider x:Name="MixerMasterSlider"
                    Minimum="0"
                    Maximum="100"
                    VerticalAlignment="Center"
                    Margin="8,0"
                    TickFrequency="1"
                    IsSnapToTickEnabled="True"
                    ValueChanged="OnMixerMasterVolumeChanged" />
        </DockPanel>
    </Border>

    <ScrollViewer Grid.Row="3"
                  MaxHeight="320"
                  VerticalScrollBarVisibility="Auto"
                  HorizontalScrollBarVisibility="Disabled">
        <StackPanel x:Name="MixerSessionList" />
    </ScrollViewer>
</Grid>
```

Remove `MixerDeviceList` — it is replaced by `MixerSessionList` + device picker.

Keep Main and Network pages unchanged. Keep `Popup AllowsTransparency="False"` (ADR-004).

- [ ] **Step 2: Build markup**

Run: `dotnet build WharfBar.sln -c Debug --nologo`

Expected: FAIL on missing handlers / leftover old code-behind until Step 3–4 of this task’s companion work in Task 3. If you split XAML+CS in one sitting, continue Task 3 immediately.

---

### Task 3: Mixer UI — code-behind

**Files:**
- Modify: `src/WharfBar/Views/QuickSettingsFlyout.xaml.cs`

**Interfaces:**
- Consumes: `MixerSnapshot` (`Devices`, `Selected`, `Sessions`, `Master`), `TrySetDefaultDevice`, `SetMasterPercent` / `ToggleMasterMute`, session setters
- Removes: `_collapsedDevices`, `BuildDeviceSection`, `BuildMasterRow` (device-scoped), `OnDeviceCollapseClick`, `OnDeviceMuteClick`, `OnDeviceVolumeChanged`

- [ ] **Step 1: Strip multi-device fields and replace `RefreshMixer`**

Remove `_collapsedDevices`. Replace `RefreshMixer` and device-building helpers with:

```csharp
private void RefreshMixer()
{
    if (_mixer is null || _page != QuickSettingsPage.Mixer)
    {
        return;
    }

    MixerSnapshot snap = _mixer.Snapshot;
    _deviceSliderUpdating = true;
    _sessionSliderUpdating = true;
    try
    {
        if (snap.Selected is null)
        {
            SelectedDeviceNameText.Text = "Çıkış aygıtı bulunamadı";
            DevicePickerButton.IsEnabled = false;
            MixerMasterSlider.IsEnabled = false;
            MixerMasterMuteButton.IsEnabled = false;
            MixerMasterPercentText.Text = "—";
            MixerSessionList.Children.Clear();
            return;
        }

        DevicePickerButton.IsEnabled = snap.Devices.Count > 0;
        SelectedDeviceNameText.Text = snap.Selected.Name;
        SelectedDeviceNameText.ToolTip = snap.Selected.Name;

        MixerMasterMuteButton.IsEnabled = true;
        MixerMasterSlider.IsEnabled = !snap.Selected.IsMuted;
        MixerMasterMuteButton.Content =
            snap.Selected.IsMuted || snap.Selected.Percent == 0 ? "\uE74F" : "\uE767";
        MixerMasterMuteButton.ToolTip = snap.Selected.IsMuted ? "Sesi aç" : "Sessiz";
        MixerMasterSlider.Value = snap.Selected.Percent;
        MixerMasterPercentText.Text = snap.Selected.Percent.ToString();

        RebuildDevicePickerList(snap);
        RebuildSessionList(snap.Sessions);
    }
    finally
    {
        _deviceSliderUpdating = false;
        _sessionSliderUpdating = false;
    }
}

private void RebuildDevicePickerList(MixerSnapshot snap)
{
    DevicePickerList.Children.Clear();
    foreach (MixerDeviceInfo device in snap.Devices)
    {
        var row = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            Tag = device.DeviceId,
            Background = device.IsDefault
                ? TryFindResource("ControlFillColorSecondaryBrush") as Brush
                    ?? Brushes.Transparent
                : Brushes.Transparent,
        };

        row.Child = new TextBlock
        {
            Text = device.IsDefault ? $"{device.Name} (varsayılan)" : device.Name,
            FontSize = 12,
            FontWeight = device.IsDefault ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.MouseLeftButtonUp += OnDevicePickerItemClick;
        DevicePickerList.Children.Add(row);
    }
}

private void RebuildSessionList(IReadOnlyList<AudioAppSession> sessions)
{
    MixerSessionList.Children.Clear();
    if (sessions.Count == 0)
    {
        MixerSessionList.Children.Add(new TextBlock
        {
            Text = "Uygulama oturumu yok",
            Opacity = 0.55,
            FontSize = 12,
            Margin = new Thickness(4, 4, 0, 0),
        });
        return;
    }

    foreach (AudioAppSession session in sessions)
    {
        MixerSessionList.Children.Add(BuildSessionRow(session));
    }
}
```

Keep existing `BuildSessionRow` / `BuildSessionIcon` / `ResolveSessionIcon` / `OnSessionVolumeChanged` (they already target session instance ids). Ensure mute-on-icon-click still calls `TrySetSessionMuted`.

- [ ] **Step 2: Wire device picker + mixer master handlers**

```csharp
private void OnDevicePickerClick(object sender, MouseButtonEventArgs e)
{
    e.Handled = true;
    if (!DevicePickerButton.IsEnabled || _mixer is null)
    {
        return;
    }

    DevicePickerPopup.IsOpen = !DevicePickerPopup.IsOpen;
}

private void OnDevicePickerItemClick(object sender, MouseButtonEventArgs e)
{
    if (sender is not FrameworkElement { Tag: string id } || _mixer is null)
    {
        return;
    }

    e.Handled = true;
    DevicePickerPopup.IsOpen = false;
    if (!_mixer.TrySetDefaultDevice(id))
    {
        RefreshMixer();
    }
}

private void OnMixerMasterMuteClick(object sender, RoutedEventArgs e) =>
    _mixer?.ToggleMasterMute();

private void OnMixerMasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (_deviceSliderUpdating || _mixer is null)
    {
        return;
    }

    _mixer.SetMasterPercent((int)Math.Round(e.NewValue));
}
```

Delete: `OnDeviceCollapseClick`, `OnDeviceMuteClick`, `OnDeviceVolumeChanged`, `BuildDeviceSection`, `BuildMasterRow` (the per-device ones).

Note: Main page still uses `MasterVolumeSlider` / `OnMasterVolumeChanged` / `OnMasterMuteClick` — do **not** remove those.

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln -c Debug --nologo`

Expected: `Oluşturma başarılı oldu` / `0 Hata`.

- [ ] **Step 4: Manual smoke (user or agent with AppBar caution)**

1. Launch WharfBar (prefer existing `run.bat` / normal close path).
2. Click volume → Mixer shows **Çıkış cihazı**, one selected name, master row, sessions.
3. Open picker → other devices listed; select one → Windows default changes; list refreshes.
4. Master / app sliders work; empty sessions show “Uygulama oturumu yok”.

- [ ] **Step 5: Commit (only if user asked)**

```bash
git add src/WharfBar/Views/QuickSettingsFlyout.xaml src/WharfBar/Views/QuickSettingsFlyout.xaml.cs
git commit -m "feat(mixer): single-device picker with master and app sessions."
```

---

### Task 4: Doküman senkronu

**Files:**
- Modify: `docs/decisions/006-quick-settings-flyout.md`
- Modify: `docs/01-architecture.md` (mixer bullet / service table only)
- Modify: `.agent/project.md` (mixer one-liner)
- Modify: `docs/superpowers/specs/2026-08-09-mixer-single-device-design.md` (status → plan link)

- [ ] **Step 1: Update ADR-006 mixer section**

Replace the “Ses karıştırıcı (EarTrumpet düzeni)” bullet list with:

```markdown
### Ses karıştırıcı (tek aygıt + dropdown)

- Üstte **Çıkış cihazı** etiketi + seçili aygıt adı (tıklanınca aktif render
  listesi; seçim → `IPolicyConfig.SetDefaultEndpoint`)
- Altında master mute + slider + % (`VolumeService` / varsayılan endpoint)
- Altında seçili aygıttaki **tüm** uygulama oturumları (ikon + slider + %;
  `CoreAudioSessions.ListSessionsOnDevice`)
- Canlı yenileme: flyout mixer sayfası açıkken ~750 ms poll
- Fluent tema; EarTrumpet mor kopyası yok; ayrı volume popup yok
```

Update ADR “Durum” line to note 2026-08-09 tek-aygıt UX güncellemesi and link the spec.

- [ ] **Step 2: Sync architecture + project brief**

In `docs/01-architecture.md` and `.agent/project.md`, change wording from “çoklu aygıt blokları” / “EarTrumpet aygıt blokları” to “tek aygıt + dropdown + oturumlar” where mixer is described. Keep ADR-006 reference.

- [ ] **Step 3: Point spec at this plan**

In the design spec header `Durum` line:

```markdown
**Durum:** Onaylandı (kullanıcı 2026-08-09) — uygulama planı:
  [`../plans/2026-08-09-mixer-single-device.md`](../plans/2026-08-09-mixer-single-device.md).
```

- [ ] **Step 4: Build still green**

Run: `dotnet build WharfBar.sln -c Debug --nologo`  
Expected: success (docs-only should not affect build; confirms tree still compiles after Task 3).

- [ ] **Step 5: Commit (only if user asked)**

```bash
git add docs/decisions/006-quick-settings-flyout.md docs/01-architecture.md .agent/project.md docs/superpowers/specs/2026-08-09-mixer-single-device-design.md
git commit -m "docs: sync ADR-006 and briefs to single-device mixer."
```

---

## Spec coverage checklist

| Spec requirement | Task |
|------------------|------|
| Çıkış cihazı etiketi + yalnız seçili ad | Task 2–3 |
| Tıklanınca diğer cihazlar / seçilebilir | Task 2–3 |
| Seçim → sistem default | Task 1 `TrySetDefaultDevice` + Task 3 |
| Master seviye seçili (default) aygıt | Task 1–3 |
| Tüm oturumlar (ses vermese de) | Task 1 `ListSessionsOnDevice` |
| Fluent, mor yok, ayrı popup yok | Task 2–3 + constraints |
| ADR / docs sync | Task 4 |
| Boş aygıt / boş oturum metinleri | Task 3 |
| SetDefault fail → refresh mevcut | Task 3 `OnDevicePickerItemClick` |

## Self-review notes

- No test project in repo — verification is `dotnet build` + manual smoke (same pattern as status-strip plan).
- Main-page master slider remains; mixer has its own master controls bound to the same `VolumeService`.
- `MixerDeviceSection` fully removed after Task 1+3; no leftover call sites.
