using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>One EarTrumpet-style block: device header + master + per-app sessions.</summary>
internal sealed record MixerDeviceSection(
    string DeviceId,
    string Name,
    bool IsDefault,
    int Percent,
    bool IsMuted,
    IReadOnlyList<AudioAppSession> Sessions);

internal sealed record MixerSnapshot(
    VolumeSnapshot Master,
    IReadOnlyList<MixerDeviceSection> Devices);

/// <summary>
/// EarTrumpet-like mixer data: per-output-device master volume + sessions, polled while
/// the Quick Settings mixer page is visible.
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

    internal bool TrySetDevicePercent(string deviceId, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        MixerDeviceSection? device = Snapshot.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (device?.IsDefault == true)
        {
            _volume.SetPercent(percent);
            Refresh();
            return true;
        }

        if (!AudioEndpointVolume.TrySetScalarForDevice(deviceId, percent / 100f))
        {
            return false;
        }

        Refresh();
        return true;
    }

    internal bool TryToggleDeviceMute(string deviceId)
    {
        MixerDeviceSection? device = Snapshot.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (device is null)
        {
            return false;
        }

        bool nextMuted = !device.IsMuted;
        if (!AudioEndpointVolume.TrySetMuteForDevice(deviceId, nextMuted))
        {
            return false;
        }

        if (device.IsDefault)
        {
            _volume.SetMuted(nextMuted);
        }

        Refresh();
        return true;
    }

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
        MixerSnapshot next = Read();
        Snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MixerSnapshot Read()
    {
        IReadOnlyList<AudioOutputDevice> devices = CoreAudioDevices.ListActiveRenderDevices();
        IReadOnlyList<AudioAppSession> sessions = CoreAudioSessions.ListSessionsOnAllActiveRenderDevices();

        var sections = new List<MixerDeviceSection>(devices.Count);
        foreach (AudioOutputDevice device in devices)
        {
            // Default first for EarTrumpet-like ordering; rest keep MMDevice order.
            IReadOnlyList<AudioAppSession> deviceSessions = sessions
                .Where(s => string.Equals(s.DeviceId, device.Id, StringComparison.Ordinal))
                .ToList();

            sections.Add(new MixerDeviceSection(
                device.Id,
                device.Name,
                device.IsDefault,
                device.Percent,
                device.IsMuted,
                deviceSessions));
        }

        // Default device on top.
        sections.Sort((a, b) =>
        {
            if (a.IsDefault == b.IsDefault)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            }

            return a.IsDefault ? -1 : 1;
        });

        return new MixerSnapshot(_volume.Snapshot, sections);
    }
}
