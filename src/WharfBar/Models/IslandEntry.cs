namespace WharfBar.Models;

/// <summary>Island entry type: copied text or a file dropped onto the island.</summary>
internal enum IslandEntryKind
{
    /// <summary>Text captured from the clipboard.</summary>
    Text,

    /// <summary>
    /// A file dropped onto the island. New entries keep the original path; older entries
    /// may also hold a <c>Drop</c> copy.
    /// </summary>
    File,
}

/// <summary>
/// A single entry of the island timeline. <see cref="FilePath"/> is populated only for the
/// <see cref="IslandEntryKind.File"/> kind.
/// </summary>
/// <param name="Id">Assigned by the service from an incrementing counter.</param>
/// <param name="Kind">Entry type.</param>
/// <param name="Text">Text to display (the file name for files).</param>
/// <param name="FilePath">
/// Full path of the file. New drops keep the original path; older entries
/// may also hold a copy under <c>%AppData%\WharfBar\Drop</c>.
/// </param>
/// <param name="CapturedAtUtc">The moment the entry was captured (UTC).</param>
/// <param name="SourceApp">Process name of the source; empty for file entries.</param>
internal sealed record IslandEntry(
    long Id,
    IslandEntryKind Kind,
    string Text,
    string? FilePath,
    DateTime CapturedAtUtc,
    string? SourceApp);
