using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The clipboard is a machine-wide singleton: a test that writes and reads it must not run
/// concurrently with the rest of the suite (the write is transiently unreadable while another
/// process — or another test thread — holds the clipboard open, which made
/// <see cref="IslandClipboardWriteTests.FileDropList_UserCopyIsReadBack"/> flaky under parallel load).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ClipboardCollection
{
    internal const string Name = "Clipboard";
}
