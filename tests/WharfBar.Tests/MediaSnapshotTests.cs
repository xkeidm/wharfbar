using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class MediaSnapshotTests
{
    [Fact]
    public void Idle_HasNoSession()
    {
        Assert.False(MediaSnapshot.Idle.HasSession);
        Assert.Equal(string.Empty, MediaSnapshot.Idle.SessionId);
        Assert.Equal(string.Empty, MediaSnapshot.Idle.Title);
        Assert.Equal(string.Empty, MediaSnapshot.Idle.Artist);
        Assert.False(MediaSnapshot.Idle.IsPlaying);
        Assert.Null(MediaSnapshot.Idle.Thumbnail);
    }

    [Fact]
    public void Snapshot_WithSessionId_HasSession()
    {
        var snap = new MediaSnapshot("Spotify.exe", "Spotify", "Başlık", "Sanatçı", true, null);
        Assert.True(snap.HasSession);
        Assert.Equal("Spotify.exe", snap.SessionId);
        Assert.Equal("Spotify", snap.AppName);
        Assert.Equal("Başlık", snap.Title);
        Assert.Equal("Sanatçı", snap.Artist);
        Assert.True(snap.IsPlaying);
    }

    [Fact]
    public void Snapshot_Records_AreValueEqual()
    {
        var a = new MediaSnapshot("id", "app", "t", "a", false, null);
        var b = new MediaSnapshot("id", "app", "t", "a", false, null);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Snapshot_WithThumbnail_HasThumbnail()
    {
        var thumb = new BitmapImage();
        thumb.BeginInit();
        thumb.CacheOption = BitmapCacheOption.OnLoad;
        thumb.StreamSource = new MemoryStream(Convert.FromBase64String("/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AVN//2Q=="));
        thumb.EndInit();
        var snap = new MediaSnapshot("id", "app", "t", "a", false, thumb);
        Assert.NotNull(snap.Thumbnail);
    }
}
