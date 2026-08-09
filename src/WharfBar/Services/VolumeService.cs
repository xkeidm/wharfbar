using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed record VolumeSnapshot(int Percent, bool IsMuted, bool IsAvailable);

/// <summary>Polls default-render endpoint volume for the top-bar Ses control.</summary>
internal sealed class VolumeService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _timer;
    private bool _disposed;

    internal VolumeService()
    {
        Snapshot = Read();
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    internal VolumeSnapshot Snapshot { get; private set; }

    internal event EventHandler? Changed;

    internal void SetPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (!AudioEndpointVolume.TrySetScalar(percent / 100f))
        {
            return;
        }

        Refresh();
    }

    internal void SetMuted(bool muted)
    {
        if (!AudioEndpointVolume.TrySetMute(muted))
        {
            return;
        }

        Refresh();
    }

    internal void ToggleMute() => SetMuted(!Snapshot.IsMuted);

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
        VolumeSnapshot next = Read();
        if (next == Snapshot)
        {
            return;
        }

        Snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static VolumeSnapshot Read()
    {
        if (!AudioEndpointVolume.TryGetScalar(out float level, out bool muted))
        {
            return new VolumeSnapshot(0, false, IsAvailable: false);
        }

        int percent = (int)Math.Round(level * 100f, MidpointRounding.AwayFromZero);
        return new VolumeSnapshot(Math.Clamp(percent, 0, 100), muted, IsAvailable: true);
    }
}
