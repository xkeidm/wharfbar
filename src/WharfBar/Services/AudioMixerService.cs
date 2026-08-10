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

    /// <param name="refreshUi">
    /// When false, applies Core Audio only and updates <see cref="Snapshot"/> without
    /// raising <see cref="Changed"/> — used while a session slider is being dragged so the
    /// flyout does not rebuild rows mid-gesture.
    /// </param>
    internal bool TrySetSessionPercent(string sessionInstanceId, int percent, bool refreshUi = true)
    {
        if (!CoreAudioSessions.TrySetSessionVolume(sessionInstanceId, percent / 100f))
        {
            return false;
        }

        if (refreshUi)
        {
            Refresh();
        }
        else
        {
            Snapshot = Read();
        }

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

    internal void RefreshNow() => Refresh();

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
