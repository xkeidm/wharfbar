using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// The island's lifecycle: the clipboard listener, the STA read thread, the single timeline, and
/// debounced writing to disk. Pure logic lives in <see cref="IslandPruner"/>; only
/// Win32/disk/thread management and orchestration live here.
/// </summary>
/// <remarks>
/// <para><b>Single timeline:</b> <see cref="Entries"/>, <c>Config</c> and the snapshot written to
/// disk are read/modified <b>only on the UI dispatcher</b>. The clipboard read runs on the STA
/// worker, but adding the captured entry is handed off to the UI thread; likewise the debounce
/// timer's callback is marshaled to the UI dispatcher. <see cref="List{T}"/> does not tolerate
/// concurrent read/write, and an exception thrown inside a timer callback would take down the
/// process.</para>
/// <para><b>Thread rule:</b> the constructor and <see cref="Dispose"/> must be called on the UI thread
/// (the message-only <c>HwndSource</c> and the clipboard listener require it).</para>
/// <para><b>File I/O is not on the UI thread:</b> deleting old copies left under <c>Drop</c> happens
/// on the STA worker; serializing <c>island.json</c> and writing it to disk is also handed to the
/// STA worker (the snapshot is taken on the UI thread). Because the top bar is a registered AppBar,
/// a large delete on the UI thread or a ~13 MB JSON write would freeze the whole desktop. New drops
/// are not copied (<see cref="AddDroppedFiles"/> records the original path and the <c>Drop</c>
/// folder is never opened); since there is no copy I/O, that work is not dispatched to the STA
/// worker. The single exception is the final write in <see cref="Dispose"/>: it stays synchronous,
/// because queuing it raced the last write against process exit and silently dropped it.</para>
/// </remarks>
internal sealed class IslandService : IDisposable
{
    private const int SaveDebounceMs = 750;
    private const int OpenClipboardRetries = 3;
    private const int OpenClipboardRetryDelayMs = 40;

    /// <summary>How long to wait for the STA worker to be ready; after that it proceeds silently anyway.</summary>
    private const int StaReadyTimeoutMs = 1000;

    private readonly Dispatcher _dispatcher;
    private readonly IslandStore _store;
    private readonly List<IslandEntry> _entries = [];

    // The STA thread writes it and the UI thread reads it: without volatile the assignment might not
    // be visible, and since the window opens in the ctor, the first incoming clipboard change would
    // see sta as null and be silently dropped ("the first copy was lost", a hard-to-reproduce bug).
    private volatile Dispatcher? _staDispatcher;
    private ClipboardMessageWindow? _listener;
    private System.Threading.Timer? _saveTimer;
    private long _nextId;
    private bool _disposed;
    private volatile bool _savePending;

    /// <summary>
    /// Has the persisted history already been attempted in this service's lifetime? <see cref="LoadPersisted"/>
    /// assumes <c>_entries</c> is empty, so a second call would list the same entries twice.
    /// </summary>
    private bool _persistedLoaded;

    /// <summary>Constructed on the UI thread.</summary>
    internal IslandService(Dispatcher dispatcher, IslandStore store, IslandConfig config)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Config = config;

        // The clipboard read runs on the STA worker: OpenClipboard can block, so keeping the UI
        // thread unblocked is mandatory. The same pattern as TrayMirrorService.
        // The signal state must start CLOSED (the STA thread assigns and then opens it with Set());
        // not "ManualResetEventSlim(1)": the single int parameter is spinCount and the ctor already
        // starts closed.
        var staReady = new ManualResetEventSlim(initialState: false);
        var staThread = new Thread(() =>
        {
            _staDispatcher = Dispatcher.CurrentDispatcher;
            staReady.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WharfBar.Island",
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();

        // A wait timeout does not throw: a late-starting STA thread is tolerated within this window.
        // But if the timeout does happen, _staDispatcher stays null and OnClipboardChanged returns
        // early every time — NO capture is recorded for the whole session. So it is logged here to
        // keep it from being invisible. staReady is deliberately not disposed — Set() must still be
        // callable, otherwise an ObjectDisposedException on the STA thread would crash the process
        // unhandled.
        if (!staReady.Wait(StaReadyTimeoutMs))
        {
            Debug.WriteLine("[WharfBar] island STA worker not ready; clipboard capture is skipped until it is");
        }

        _saveTimer = new System.Threading.Timer(
            _ => OnSaveTimerTick(), null, Timeout.Infinite, Timeout.Infinite);

        // Loading happens BEFORE setting up the listener (the same order as ApplyConfig): as soon as
        // StartListening enables clipboard monitoring a copy can arrive and the STA worker can run
        // Interlocked.Increment(ref _nextId); the Math.Max in LoadPersisted swallows that write and
        // the same Id lands on two entries. Doing the load first closes the window completely
        // (the id counter only advances on the UI thread).
        LoadPersisted();

        if (Config.Enabled)
        {
            StartListening();
        }
    }

    internal IslandConfig Config { get; private set; }

    internal IReadOnlyList<IslandEntry> Entries => _entries;

    internal IslandEntry? Latest => _entries.Count > 0 ? _entries[0] : null;

    /// <summary>
    /// Returns the newest plain-text entry in the history for "Take from clipboard" (the island
    /// reader body). Read-only: it does not write to the history (<see cref="AddEntry"/> is not
    /// called), raises no events, and never touches the real clipboard. Because <see cref="Entries"/>
    /// is sorted with the newest first, the first text entry is the newest one — otherwise it
    /// returns <c>false</c> and an empty string.
    /// </summary>
    /// <remarks>Class timeline rule: called on the UI dispatcher, like <see cref="Entries"/>.</remarks>
    internal bool TryLastClipboardText(out string text)
    {
        foreach (IslandEntry entry in _entries)
        {
            if (entry.Kind != IslandEntryKind.Text)
            {
                continue;
            }

            text = entry.Text;
            return true;
        }

        text = string.Empty;
        return false;
    }

    internal event EventHandler? Changed;

    /// <summary>Read externally to trigger the pill's pulse when a new entry is added.</summary>
    internal event EventHandler? EntryAdded;

    /// <summary>Persistent mode? The comparison is always made against the normalized value.</summary>
    private bool IsPersistent =>
        IslandNormalizer.NormalizePersistence(Config.Persistence) == IslandConfig.PersistencePersistent;

    /// <summary>
    /// Restores the persisted history in persistent mode. The store returns raw entries and gives no
    /// ordering guarantee (it is a serializer; silently re-sorting a hand-edited file would change the
    /// user's file) — so sorting and pruning happen here, using the user's settings.
    /// </summary>
    private void LoadPersisted()
    {
        // The !Config.Enabled check is required: the service is now constructed even while the setting
        // is off (so it can be enabled later from settings), but touching island.json while off
        // would leave a trace.
        if (_disposed || !Config.Enabled || !IsPersistent)
        {
            return;
        }

        // At most once per service lifetime. Loading prepends to _entries (AddRange), merges with
        // _entries, or replaces it entirely (Commit), so a second call would list the same entries
        // twice. The one caveat: a call rejected by the gate above does not set the flag, because
        // the conditions can change in a later ApplyConfig.
        if (_persistedLoaded)
        {
            return;
        }

        _persistedLoaded = true;

        IReadOnlyList<IslandEntry> loaded = _store.Load();
        if (loaded.Count == 0)
        {
            return;
        }

        var ordered = loaded
            .OrderByDescending(e => e.CapturedAtUtc)
            .ToList();

        // Ids must be monotonic: on a first setup (no session entries) the counter for new entries
        // starts at 1 and collides with the Ids of the loaded entries. Math.Max: the counter cannot
        // go back, so it is only ever raised to avoid dropping to an Id that was already produced.
        _nextId = Math.Max(_nextId, ordered.Max(e => e.Id));

        // Session -> persistent transition: the session's entries are still in the list but NOT on
        // disk (ScheduleSave does not run in session mode). Writing ("replacing") only what was read
        // from disk would silently throw away what the user copied in this session, so the two lists
        // are merged into one and pruned together.
        bool merging = _entries.Count > 0;
        if (merging)
        {
            ordered = MergeWithSession(ordered);
        }

        IReadOnlyList<IslandEntry> pruned = IslandPruner.Prune(
            ordered, Config.MaxItems, DateTimeOffset.UtcNow, Config.RetentionDays, IsPersistent);

        if (!merging && pruned.Count == ordered.Count)
        {
            // Pruning discarded nothing. Prune only eliminates (it does not reorder), so equal counts
            // mean the lists are exactly the same: going to Commit would produce a pointless disk
            // write and a Changed notification on every open.
            _entries.AddRange(ordered);

            // But the notification cannot be skipped: AddRange bypasses Commit (and RaiseChanged).
            // If the list silently fills here when an island started disabled is enabled from
            // settings, and the window is not subscribed, the pill stays stale — "there is history
            // but it is not shown".
            RaiseChanged();
            return;
        }

        // Write the pruned form back to disk: if the user lowered the ceiling (or the day limit ran
        // out), the file yields the same pruned list on the next launch.
        Commit(pruned, added: null, raiseEntryAdded: false);
    }

    /// <summary>
    /// Merges the session entries with the history written to disk into a single list (newest first).
    /// Called on the UI thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Id cannot be the merge key.</b> Because every process starts at 1, session and
    /// persistent entries carry the same Ids; <c>DistinctBy(e =&gt; e.Id)</c> kept the first one (the
    /// session entry) and silently dropped the persistent one. Since the default persistence mode is
    /// <c>session</c> (<c>AppConfig.Persistence</c>), this was the common path: measured — 10
    /// persistent + 3 session entries → 10 entries; <c>kalici-1..3</c> were deleted from both the
    /// list and <c>island.json</c> (<c>Commit</c> → <c>ScheduleSave</c> wrote the file back, leaving
    /// 7/10 on disk).</para>
    /// <para><b>Content instead of identity:</b> the <c>(Kind, Text, FilePath)</c> triple — the same
    /// rules as <c>IslandPruner.IsSameContent</c> (<c>Text</c> ordinal, <c>FilePath</c>
    /// ordinal-ignore-case). Because the list is sorted newest to oldest, the first entry seen is the
    /// newest and wins.</para>
    /// <para><b>Loaded entries' Ids are shifted above <c>_nextId</c>.</b> The counter from the previous
    /// process generation is meaningless (it starts at 1 on every launch), but if the same Id sits on
    /// two entries, <see cref="Remove"/>'s Id comparison and <see cref="QueueDeleteOfDropped"/>'s
    /// membership check go wrong. Session entries keep their Ids: so that the entry references held by
    /// the UI (selected item, <c>Remove</c>/<c>PutBack</c> argument) stay valid.</para>
    /// </remarks>
    private List<IslandEntry> MergeWithSession(IReadOnlyList<IslandEntry> loaded)
    {
        var renumbered = new List<IslandEntry>(loaded.Count);
        foreach (IslandEntry e in loaded)
        {
            renumbered.Add(e with { Id = Interlocked.Increment(ref _nextId) });
        }

        var seen = new HashSet<IslandEntry>(EntryContentComparer.Instance);
        var merged = new List<IslandEntry>(_entries.Count + renumbered.Count);
        foreach (IslandEntry e in _entries
            .Concat(renumbered)
            .OrderByDescending(x => x.CapturedAtUtc))
        {
            if (seen.Add(e))
            {
                merged.Add(e);
            }
        }

        return merged;
    }

    /// <summary>Called on the UI thread.</summary>
    internal void ApplyConfig(IslandConfig config)
    {
        bool wasEnabled = Config.Enabled;
        bool wasPersistent = IsPersistent;
        Config = config;
        if (Config.Enabled)
        {
            // Loading happens BEFORE setting up the listener. As soon as StartListening enables
            // clipboard monitoring a copy can arrive and the STA worker can run
            // Interlocked.Increment(ref _nextId); the Math.Max in LoadPersisted swallows that write
            // and the same Id lands on two entries. Doing the load first closes the window
            // completely (the id counter only advances on the UI thread).
            //
            // The trigger covers both transitions:
            //  - false -> true: an island started disabled was enabled from settings (round 2).
            //  - session -> persistent: because the default mode is session-only, this path did no
            //    loading at all and the persisted history only appeared after a restart.
            if ((!wasEnabled || (!wasPersistent && IsPersistent)) && !_persistedLoaded)
            {
                LoadPersisted();
            }

            StartListening();
        }
        else
        {
            StopListening();
        }

        // Numeric settings (MaxItems / RetentionDays) used to apply only on the next capture: when
        // the user lowered the ceiling from 200 to 20 the list kept showing 200. A settings change
        // must apply pruning immediately too.
        IReadOnlyList<IslandEntry> pruned = IslandPruner.Prune(
            _entries, Config.MaxItems, DateTimeOffset.UtcNow, Config.RetentionDays, IsPersistent);
        if (pruned.Count != _entries.Count)
        {
            // Loading already happened above, triggered by the transition; it is not repeated here
            // (_persistedLoaded) and Commit already calls RaiseChanged, so calling it again would be
            // a second notification.
            Commit(pruned, added: null, raiseEntryAdded: false);
            return;
        }

        RaiseChanged();
    }

    private void StartListening()
    {
        if (_listener is not null || _disposed)
        {
            return;
        }

        _listener = new ClipboardMessageWindow();
        _listener.ClipboardChanged += OnClipboardChanged;
    }

    private void StopListening()
    {
        if (_listener is null)
        {
            return;
        }

        _listener.ClipboardChanged -= OnClipboardChanged;
        _listener.Dispose();
        _listener = null;
    }

    /// <summary>Raised on the UI thread; hands the read work off to the STA worker.</summary>
    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        Dispatcher? sta = _staDispatcher;
        if (sta is null || _disposed)
        {
            return;
        }

        _ = sta.BeginInvoke(
            () =>
            {
                try
                {
                    CaptureFromClipboard();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WharfBar] island capture failed: {ex.Message}");
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>Runs on the STA worker.</summary>
    private void CaptureFromClipboard()
    {
        if (!TryReadWithRetry(out string? text, out bool optOut, out int byteCount))
        {
            return;
        }

        if (!IslandPruner.ShouldCapture(optOut, text, byteCount))
        {
            return;
        }

        AddEntry(new IslandEntry(
            Interlocked.Increment(ref _nextId),
            IslandEntryKind.Text,
            text!,
            null,
            DateTime.UtcNow,
            ClipboardNative.GetOwnerProcessName()));
    }

    private static bool TryReadWithRetry(out string? text, out bool optOut, out int byteCount)
    {
        for (int attempt = 0; attempt < OpenClipboardRetries; attempt++)
        {
            if (ClipboardNative.TryReadText(out text, out optOut, out byteCount))
            {
                return true;
            }

            // The opt-out flag is not the same as missing content: retrying would not capture it.
            if (optOut)
            {
                return false;
            }

            if (attempt < OpenClipboardRetries - 1)
            {
                Thread.Sleep(OpenClipboardRetryDelayMs);
            }
        }

        // The clipboard can be locked by another application once a second; that is not a user error,
        // so it is dropped silently after three attempts.
        text = null;
        optOut = false;
        byteCount = 0;
        return false;
    }

    /// <summary>
    /// Prepends the new entry. It can also be called from the STA worker; the timeline computation
    /// is done on the UI thread (<see cref="_entries"/> is only read there).
    /// </summary>
    private void AddEntry(IslandEntry entry)
    {
        if (!_dispatcher.CheckAccess())
        {
            if (!CanPostToUi)
            {
                return;
            }

            _ = _dispatcher.BeginInvoke(() => AddEntry(entry), DispatcherPriority.Background);
            return;
        }

        IReadOnlyList<IslandEntry> merged = IslandPruner.Prepend(
            _entries,
            entry,
            Config.MaxItems,
            DateTimeOffset.UtcNow,
            Config.RetentionDays,
            IsPersistent);

        ApplySnapshot(merged, entry, raiseEntryAdded: true);
    }

    /// <summary>
    /// Called on the UI thread. Does not copy the dropped file or <b>folder</b> into the <c>Drop</c>
    /// folder; the <see cref="IslandEntry.FilePath"/> of an <see cref="IslandEntryKind.File"/> entry is
    /// the original full path, and <see cref="IslandEntry.Text"/> is the file/folder name. A folder
    /// entry does not open a separate kind: the shelf shows it as the same chip too, and opening goes
    /// to Explorer through the <see cref="CanOpenShelfFile"/> gate. That is why the <c>Drop</c> folder
    /// is not created. Paths that are neither a file nor a folder (<see cref="File.Exists"/> and
    /// <see cref="Directory.Exists"/> both false) are filtered out. Multiple files are merged with
    /// <see cref="MergeDroppedFiles"/>; the "added" notification is only for the last entry.
    /// </summary>
    /// <remarks>
    /// Once the copy was removed there is no file I/O left to justify hopping to the STA worker
    /// (<c>File.Exists</c> is a metadata check). The work finishes on the caller's thread, i.e. the UI
    /// dispatcher. If it is called off the UI thread, the work is handed to the UI so the entry list
    /// is not written there — the timeline contract does not change; in an async hand-off the real
    /// count cannot come back to the caller, so it returns 0.
    /// </remarks>
    /// <returns>
    /// The number of entries <b>actually</b> added to the shelf list. Returns 0 if no entry was added
    /// along the way (deleted/renamed path, neither a file nor a folder). The drop feedback (HUD +
    /// swallow animation) must be gated on this count, not on the payload strings: a path deleted
    /// during the drag must not be assumed to have been "dropped".
    /// </returns>
    internal int AddDroppedFiles(IReadOnlyList<string> sourcePaths)
    {
        if (_disposed || sourcePaths.Count == 0)
        {
            return 0;
        }

        if (!_dispatcher.CheckAccess())
        {
            if (!CanPostToUi)
            {
                return 0;
            }

            _ = _dispatcher.BeginInvoke(
                () =>
                {
                    _ = AddDroppedFiles(sourcePaths);
                },
                DispatcherPriority.Background);
            return 0;
        }

        List<IslandEntry> added = [];
        foreach (string source in sourcePaths)
        {
            // A folder is a shelf target too (B1): File.Exists returns false for folders, so the two
            // checks together form the "real path" gate. A path that is neither a file nor a folder
            // (deleted, inaccessible) is filtered out silently — fail-closed.
            if (string.IsNullOrWhiteSpace(source) || (!File.Exists(source) && !Directory.Exists(source)))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(source);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Debug.WriteLine($"[WharfBar] island drop path invalid: {source} ({ex.Message})");
                continue;
            }

            string name = GetShelfName(full);
            if (name.Length == 0)
            {
                continue;
            }

            added.Add(new IslandEntry(
                Interlocked.Increment(ref _nextId),
                IslandEntryKind.File,
                name,
                full,
                DateTime.UtcNow,
                null));
        }

        if (added.Count == 0)
        {
            return 0;
        }

        MergeDroppedFiles(added);
        return added.Count;
    }

    /// <summary>
    /// Display name of a shelf entry: the file or folder name. A folder path ending in a separator
    /// (<c>C:\a\b\</c>) comes back empty from <see cref="Path.GetFileName(string)"/>; the name is read
    /// with the separator trimmed, otherwise the folder would silently not be saved. A root path stays
    /// empty too. <see cref="TryGetExistingShelfPath"/> uses this name to match the <c>Drop</c> copy.
    /// </summary>
    private static string GetShelfName(string full)
    {
        string name = Path.GetFileName(full);
        return name.Length > 0
            ? name
            : Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    /// <summary>Runs on the UI thread.</summary>
    private void MergeDroppedFiles(List<IslandEntry> added)
    {
        IReadOnlyList<IslandEntry> merged = _entries;
        IslandEntry? last = null;
        foreach (IslandEntry e in added)
        {
            merged = IslandPruner.Prepend(
                merged, e, Config.MaxItems, DateTimeOffset.UtcNow,
                Config.RetentionDays, IsPersistent);
            last = e;
        }

        // Only the last one counts as "added": one notification, one pulse.
        ApplySnapshot(merged, last, raiseEntryAdded: true);
    }

    /// <summary>Called on the UI thread. Writes the text to the clipboard (no paste is performed).</summary>
    internal void PutBack(IslandEntry entry)
    {
        // Putting back while the island is disabled is a clipboard change the user did not ask for:
        // the clipboard would silently change on a request from an invisible list.
        if (entry.Kind == IslandEntryKind.File || !Config.Enabled)
        {
            return;
        }

        Dispatcher? sta = _staDispatcher;
        if (sta is null || _disposed)
        {
            return;
        }

        _ = sta.BeginInvoke(
            () =>
            {
                try
                {
                    if (!ClipboardNative.TryWriteText(entry.Text))
                    {
                        Debug.WriteLine("[WharfBar] island put-back write failed");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WharfBar] island put-back failed: {ex.Message}");
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Called on the UI thread. Writes the text to the clipboard (no paste is performed) — the color
    /// picker's HEX/RGB copy uses this independent entry point alongside <see cref="PutBack"/>'s writer
    /// (review decision: PutBack's STA write block is not refactored, the writer stays duplicated).
    /// </summary>
    /// <remarks>
    /// <para><b>Gate:</b> if the island is disabled, the text is empty, or the service has been
    /// disposed, the write is never attempted and <c>false</c> is reported to <paramref name="completed"/> —
    /// the sibling of PutBack's "do not silently change the clipboard on a request from an invisible
    /// list" principle: the picker only comes from an island that was opened, and a click that arrives
    /// while it is disabled is a stale path, so the clipboard stays put.</para>
    /// <para><b>Result reporting:</b> the write finishes on the STA worker; <paramref name="completed"/>
    /// is invoked from the UI thread (the pattern <see cref="RaiseChanged"/> uses to marshal to the UI).
    /// If the UI dispatcher has shut down, the notification is dropped silently.</para>
    /// <para><b>Recapturing our own write:</b> <see cref="ClipboardNative.TryWriteText"/>
    /// marks the entry it writes as opt-out with a <c>CanIncludeInClipboardHistory = 0</c> block,
    /// so our own listener (<c>AddClipboardFormatListener</c>) does not record this write again.
    /// If it somehow is captured, the capture already uses <see
    /// cref="CaptureFromClipboard"/> → <see cref="AddEntry"/> (the entry-adding code)
    /// — this method never adds the entry by hand, it only writes.</para>
    /// </remarks>
    internal void PutOnClipboard(string text, Action<bool>? completed = null)
    {
        // The same gate as TryWriteText's empty-text rule: empty text cannot be written.
        if (string.IsNullOrEmpty(text) || !Config.Enabled)
        {
            ReportWriteResult(completed, success: false);
            return;
        }

        Dispatcher? sta = _staDispatcher;
        if (sta is null || _disposed)
        {
            ReportWriteResult(completed, success: false);
            return;
        }

        _ = sta.BeginInvoke(
            () =>
            {
                bool written;
                try
                {
                    written = ClipboardNative.TryWriteText(text);
                    if (!written)
                    {
                        Debug.WriteLine("[WharfBar] island put-on-clipboard write failed");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WharfBar] island put-on-clipboard failed: {ex.Message}");
                    written = false;
                }

                ReportWriteResult(completed, written);
            },
            DispatcherPriority.Background);
    }

    /// <summary>Reports the write result to <paramref name="completed"/> on the UI thread.
    /// The gate paths call it synchronously on the caller's thread (UI); the STA path marshals the
    /// result to the UI. If the UI dispatcher has shut down, the notification is dropped silently.</summary>
    private void ReportWriteResult(Action<bool>? completed, bool success)
    {
        if (completed is null)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            completed.Invoke(success);
        }
        else if (CanPostToUi)
        {
            _ = _dispatcher.BeginInvoke(
                () => completed.Invoke(success),
                DispatcherPriority.Background);
        }

        // No UI (dispose / shutdown race): the notification is dropped silently — the same reasoning
        // as CanPostToUi.
    }

    /// <summary>Called on the UI thread.</summary>
    internal void Remove(IslandEntry entry)
    {
        IReadOnlyList<IslandEntry> next = _entries.Where(e => e.Id != entry.Id).ToList();

        // added: null — a removed entry is not "added", and raiseEntryAdded is false too.
        ApplySnapshot(next, null, raiseEntryAdded: false);
    }

    /// <summary>
    /// Called on the UI thread. Drops all entries from the list but does not delete the files:
    /// both the shelf's original path and the old copy under <c>Drop</c> stay on disk.
    /// In settings "Delete dropped files" and "Clear all history" are two separate buttons, and only
    /// the first deletes the copies under <c>Drop</c>. In the previous behavior a user who chose
    /// "clear history" lost the file copies without knowing, and the two buttons were a pointless
    /// duplicate. Leaving the copies on disk is not irreversible data loss;
    /// <see cref="DeleteDroppedFiles"/> cleans them up. A shelf file outside <c>Drop</c> is not
    /// deleted by this action either.
    /// </summary>
    internal void Clear() =>
        ApplySnapshot([], null, raiseEntryAdded: false, deleteDroppedFiles: false);

    /// <summary>
    /// Called on the UI thread. Empties the <c>Drop</c> folder <b>completely</b> (including
    /// subfolders) and drops the file entries from the list; it does not touch the clipboard.
    /// If a shelf entry is outside <c>Drop</c> it is only removed from the list — the original file
    /// is not deleted.
    /// </summary>
    /// <remarks>
    /// <para><b>The folder is scanned, <c>_entries</c> is not.</b> <see cref="Clear"/> does not delete
    /// the file copies (R30); when "Clear all history" is pressed first, the entries are dropped and
    /// the list empties — a cleanup that only looks at the list would then exit without deleting
    /// anything, i.e. the button on the settings page would silently do nothing. For the same reason
    /// orphans that cannot be computed from a single list (including those dropped by
    /// MaxItems/retention pruning) are covered; there is no need to pair entries with files. The scan
    /// never leaves the <c>Drop</c> root: if the shelf's original path is not in this folder, the file
    /// stays.</para>
    /// <para>The delete happens on the STA worker (class rule: file I/O never runs on the UI thread);
    /// dropping the entries from the list happens on the caller's thread because <c>_entries</c> is
    /// UI-only. The order does not matter: dropping the entry is independent of deleting the copy, and
    /// both are part of the same user action.</para>
    /// </remarks>
    internal void DeleteDroppedFiles()
    {
        // Entries are dropped here: a file entry left in the list after its copy was deleted would
        // point at a nonexistent file. The file deletion happens below, on the STA thread.
        IReadOnlyList<IslandEntry> next = _entries.Where(e => e.Kind != IslandEntryKind.File).ToList();
        if (next.Count != _entries.Count)
        {
            // deleteDroppedFiles: false — Commit's "delete the copy of an entry dropped from the list"
            // path is wrong and unnecessary here: it would queue the same files a second time.
            ApplySnapshot(next, null, raiseEntryAdded: false, deleteDroppedFiles: false);
        }

        // The folder is scanned even when there are no entries: that is the actual state after
        // "Clear all history".
        Dispatcher? sta = _staDispatcher;
        if (sta is null || _disposed)
        {
            return;
        }

        _ = sta.BeginInvoke(DeleteDropFolderContents, DispatcherPriority.Background);
    }

    /// <summary>
    /// Empties the <c>Drop</c> folder by scanning it (STA worker). If the folder does not exist it
    /// exits silently — having never dropped a file is the normal case.
    /// </summary>
    private void DeleteDropFolderContents()
    {
        if (!TryGetDropRoot(out string root, out string prefix))
        {
            return;
        }

        if (!Directory.Exists(root))
        {
            return;
        }

        int deleted = 0;
        try
        {
            // Directories marked ReparsePoint (junction / symbolic link) are NOT entered.
            // Because Path.GetFullPath does not resolve reparse points, a junction placed inside Drop
            // took the scan OUTSIDE Drop (measured: …\Drop\baglanti\
            // tehlikeli.docx was being listed) and TryDeleteDropCopy's prefix check counted that path
            // as "under Drop" — i.e. a file outside Drop was silently deleted.
            // Since the app writes plain files into Drop, the only link here is one the user placed;
            // skipping Hidden/System keeps the same behavior as the previous SearchOption overload.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                MatchType = MatchType.Simple,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            };

            // Drop copies are written FLAT (Drop\<name>, " (2)" on a name collision); there is no
            // date-time subfolder. The paths from the enumerator are already under Drop, but the same
            // guard is applied anyway: the single validation point (TryDeleteDropCopy) holds for
            // both paths.
            foreach (string path in Directory.EnumerateFiles(root, "*", options))
            {
                if (TryDeleteDropCopy(path, prefix))
                {
                    deleted++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The count may be left half-done; that should not be invisible either.
            Debug.WriteLine($"[WharfBar] island drop scan failed: {ex.Message}");
        }

        Debug.WriteLine($"[WharfBar] island drop folder cleared: {deleted} file(s) deleted");
    }

    /// <summary>
    /// Deletes the old copies under <c>Drop</c> for the given entries. If the shelf entry's path is
    /// outside <c>Drop</c> the file is left alone; the entry has already been dropped from the list.
    /// If the old copy is not deleted the moment the entry leaves the list,
    /// <c>%AppData%\WharfBar\Drop</c> grows permanently: a user with <c>MaxItems</c> at 200 who
    /// dropped 300 files keeps 100 copies behind. This path only concerns the entry path —
    /// <see cref="DeleteDroppedFiles"/> scans the whole folder.
    /// </summary>
    private void DeleteFilesOf(IEnumerable<IslandEntry> dropped)
    {
        if (!TryGetDropRoot(out _, out string prefix))
        {
            return;
        }

        foreach (IslandEntry entry in dropped)
        {
            if (entry.Kind != IslandEntryKind.File || entry.FilePath is null)
            {
                continue;
            }

            _ = TryDeleteDropCopy(entry.FilePath, prefix);
        }
    }

    /// <summary>
    /// Yields the <c>Drop</c> folder: the full path (search base) and, for comparing against paths
    /// underneath, the prefix with a separator appended. The folder <b>itself</b> does not count as
    /// "under Drop".
    /// </summary>
    private bool TryGetDropRoot(out string root, out string prefix)
    {
        root = string.Empty;
        prefix = string.Empty;
        try
        {
            root = Path.GetFullPath(_store.DropRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Debug.WriteLine($"[WharfBar] island drop root invalid: {_store.DropRoot} ({ex.Message})");
            return false;
        }

        prefix = root + Path.DirectorySeparatorChar;
        return true;
    }

    /// <summary>
    /// Verifies that an entry path is under the <c>Drop</c> root and returns its full path.
    /// <b>Delete boundary</b>: an entry's <c>FilePath</c> is untrustworthy because it came from the
    /// disk itself (<c>island.json</c> can be hand-edited and <c>IslandStore.TryReadEntry</c> performs
    /// no DropRoot check). Without the boundary, a file outside Drop would be deleted irreversibly.
    /// Opening does not use this prefix — the shelf keeps the original path; the open gate is
    /// <see cref="CanOpenShelfFile"/>.
    /// </summary>
    internal bool TryResolveInsideDrop(string? path, out string full)
    {
        if (!TryGetDropRoot(out _, out string prefix))
        {
            full = string.Empty;
            return false;
        }

        return IsInsideDropPrefix(path, prefix, out full);
    }

    /// <summary>
    /// Is the entry path under <c>Drop</c>? The delete path's boundary
    /// (<see cref="TryResolveInsideDrop"/>). For opening, use <see cref="CanOpenShelfFile"/>.
    /// </summary>
    internal bool IsInsideDropRoot(string? path) => TryResolveInsideDrop(path, out _);

    /// <summary>
    /// Resolves the <b>currently existing</b> path of a shelf entry: first the entry's own path (file
    /// or folder), and if that is gone, the copy with the <b>same name</b> under the <c>Drop</c> root.
    /// Older entries kept their copy under <c>Drop</c>; after the copy step was removed, this copy is
    /// the only openable target for an entry whose original was deleted. Matching is bounded the same
    /// way as the delete path (<see cref="TryResolveInsideDrop"/>: the <c>Drop</c> prefix), so no
    /// resolution escapes <c>Drop</c>. The entry is not modified; this is a read only.
    /// </summary>
    /// <param name="existing">
    /// The resolved full path; an empty string if nothing was resolved.
    /// </param>
    /// <returns><c>true</c> if an existing path was resolved; otherwise <c>false</c> (fail-closed).</returns>
    /// <remarks>
    /// The returned path only means "something exists on disk"; the entry path came from
    /// <c>island.json</c>, so it is <b>untrustworthy</b>. The open, reveal, and copy callers
    /// <b>must pass the result through the <see cref="CanOpenShelfFile"/> gate again</b>: device
    /// paths, reparse-point folders, and traversal components are filtered out there. This method
    /// never starts an operation on its own.
    /// </remarks>
    internal bool TryGetExistingShelfPath(string? filePath, out string existing)
    {
        existing = string.Empty;
        if (!TryNormalizeOpenablePath(filePath, out string full))
        {
            return false;
        }

        if (File.Exists(full) || Directory.Exists(full))
        {
            existing = full;
            return true;
        }

        // Fallback: the copy is written FLAT (<c>Drop\&lt;name&gt;</c>), so the name is looked up in
        // the Drop root. The name is a single component; even so, the combined path goes through the
        // single validation point.
        string fileName = GetShelfName(full);
        if (fileName.Length == 0 || !TryResolveInsideDrop(Path.Combine(_store.DropRoot, fileName), out string copy))
        {
            return false;
        }

        if (File.Exists(copy) || Directory.Exists(copy))
        {
            existing = copy;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Can the shelf entry be opened with its default application? <c>island.json</c> is untrustworthy;
    /// only this gate allows opening. The <c>Drop</c> prefix is not required. The path must be an
    /// existing file or <b>folder</b>: <see langword="null"/>, an empty or nonexistent path, a device
    /// path, a reparse-point folder (junction / symbolic link), and directory reparse escapes are
    /// rejected. Nothing is executed.
    /// </summary>
    internal bool CanOpenShelfFile(string? path)
    {
        if (!TryNormalizeOpenablePath(path, out string full))
        {
            return false;
        }

        try
        {
            // A folder is an open target too (B1): Process.Start(UseShellExecute) opens Explorer for
            // a folder. Because File.Exists returns false for folders, the two checks together mean
            // "existing path"; if neither holds, fail-closed.
            bool isDirectory = Directory.Exists(full);
            if (!isDirectory && !File.Exists(full))
            {
                return false;
            }

            // If the folder ITSELF is a reparse point it is rejected: opening follows the link, so a
            // junction placed inside Drop would open a folder outside Drop. Files do not get this
            // check — a cloud placeholder is a reparse point too and must open (macOS/OneDrive); the
            // directory components in the file path are already filtered by
            // AllDirectoryComponentsAreReal.
            if (isDirectory && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            {
                Debug.WriteLine($"[WharfBar] island open skipped, reparse-point folder: {full}");
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"[WharfBar] island open skipped, unreadable: {full} ({ex.Message})");
            return false;
        }

        if (!TryGetVolumePrefix(full, out string volumePrefix))
        {
            return false;
        }

        // Same class as the delete guard: every directory component on the path must be real.
        // The file/folder itself does not count as a component (a folder has its own reparse check
        // above; for a file, a cloud placeholder may be a reparse point). There is no Drop prefix —
        // the root is a drive or a UNC share.
        if (!AllDirectoryComponentsAreReal(volumePrefix, full, out string? reparseDir))
        {
            Debug.WriteLine(
                $"[WharfBar] island open skipped, unsafe path component " +
                $"({reparseDir ?? "unreadable"}): {full}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Opens the shelf entry with its default application. The gate is <see cref="CanOpenShelfFile"/>:
    /// input validation runs once more before opening. The file is not deleted; for a folder entry
    /// <c>UseShellExecute</c> opens Explorer.
    /// </summary>
    /// <returns><c>true</c> if it passed the gate and a launch was attempted; <c>false</c> if the gate
    /// rejected it or no default application was found (the entry stays in the list).</returns>
    internal bool OpenShelfFile(string path)
    {
        if (!CanOpenShelfFile(path))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Broken shortcut, no access, no default application.
            Debug.WriteLine($"[WharfBar] island open failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Converts the path to be opened into a full path. The device namespace (<c>\\.\</c>,
    /// <c>\\?\</c> that stays unresolved) and reserved DOS device names (<c>NUL</c>, <c>CON</c>,
    /// <c>COM1</c> …) are rejected.
    /// </summary>
    private static bool TryNormalizeOpenablePath(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Debug.WriteLine($"[WharfBar] island open path invalid: {path} ({ex.Message})");
            return false;
        }

        // \\.\ is the device namespace (NUL, PhysicalDrive). The \\?\ long-path prefix can be
        // resolved to a normal file by GetFullPath; if it stays unresolved, the check below rejects it.
        if (IsWin32DeviceNamespace(path) || IsWin32DevicePath(full) || ContainsReservedDeviceName(full))
        {
            Debug.WriteLine($"[WharfBar] island open skipped, device path: {path}");
            full = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Produces the arguments to add to <see cref="ProcessStartInfo.ArgumentList"/> for Explorer's
    /// "show file location": a single element, <c>/select,&lt;full path&gt;</c>. The path is <b>not
    /// quoted</b>; quoting and escaping are left to <see cref="ProcessStartInfo.ArgumentList"/>
    /// because the old <c>Arguments = $"/select,\"{path}\""</c> form let a trailing separator
    /// (<c>C:\folder\</c>) escape the closing quote and Explorer opened the default folder.
    /// </summary>
    /// <remarks>
    /// The trailing separator is trimmed except for a root path (<c>C:\</c>, <c>\\server\share\</c>):
    /// a root without its separator would turn into an invalid path (<c>C:</c>). Being a single element
    /// combined with the <c>/select,</c> prefix lets <see cref="ProcessStartInfo.ArgumentList"/> quote
    /// the whole element correctly even when the path contains spaces.
    /// </remarks>
    internal static string[] BuildRevealArguments(string path)
    {
        string normalized = path;
        if (!string.IsNullOrEmpty(path))
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = (Path.GetPathRoot(path) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length > 0
                && !(root.Length > 0 && string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase)))
            {
                normalized = trimmed;
            }
        }

        return ["/select," + normalized];
    }

    /// <summary>
    /// Volume root with a trailing separator, for <see cref="AllDirectoryComponentsAreReal"/>.
    /// <c>C:\</c> or <c>\\server\share\</c>. A device root is not accepted.
    /// </summary>
    private static bool TryGetVolumePrefix(string full, out string prefix)
    {
        prefix = string.Empty;
        string? root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || IsWin32DevicePath(root) || IsWin32DevicePath(full))
        {
            return false;
        }

        prefix = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return true;
    }

    /// <summary>The <c>\\.\</c> device namespace. The <c>\\?\</c> long-path prefix is not part of it.</summary>
    private static bool IsWin32DeviceNamespace(string path) =>
        path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("//./", StringComparison.Ordinal);

    private static bool IsWin32DevicePath(string path) =>
        IsWin32DeviceNamespace(path)
        || path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("//?/", StringComparison.Ordinal);

    /// <summary>
    /// Is one of the path segments a reserved DOS device name (<c>NUL</c>, <c>CON</c>,
    /// <c>COM1</c>–<c>COM9</c>, <c>LPT1</c>–<c>LPT9</c>, including forms with an extension)?
    /// </summary>
    private static bool ContainsReservedDeviceName(string path)
    {
        foreach (string segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (IsReservedDeviceName(segment))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReservedDeviceName(string segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        string name = segment;
        int colon = name.IndexOf(':');
        if (colon >= 0)
        {
            name = name[..colon];
        }

        int dot = name.IndexOf('.');
        if (dot >= 0)
        {
            name = name[..dot];
        }

        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Length != 4)
        {
            return false;
        }

        if (!name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name[3] is >= '1' and <= '9';
    }

    /// <summary>
    /// Boundary check only: the pure part, testable without the service or the store.
    /// </summary>
    /// <param name="rootPrefix">
    /// The separator-appended prefix produced by <see cref="TryGetDropRoot"/>. Including the separator
    /// is mandatory: the sibling folders "…\Drop" and "…\DropYedek" would both count as under "Drop"
    /// in a prefix check without the separator.
    /// </param>
    internal static bool IsInsideDropPrefix(string? path, string rootPrefix, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Debug.WriteLine($"[WharfBar] island drop path invalid: {path} ({ex.Message})");
            full = string.Empty;
            return false;
        }

        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[WharfBar] island drop path rejected, outside Drop: {path}");
            full = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Deletes a single drop copy. Only paths under <c>%AppData%\WharfBar\Drop</c> are deleted; a path
    /// outside is logged and skipped. The entry's <c>FilePath</c> is not trustworthy because it came
    /// from the disk itself (<c>island.json</c> can be hand-edited): <c>IslandStore.TryReadEntry</c>
    /// performs no DropRoot check, so an entry like
    /// <c>{"kind":"File","filePath":"C:\\…\\Documents\\dangerous.docx"}</c> would be deleted without
    /// confirmation and irreversibly.
    /// </summary>
    /// <returns><c>true</c> if the file was actually deleted.</returns>
    private static bool TryDeleteDropCopy(string path, string rootPrefix)
    {
        if (!IsInsideDropPrefix(path, rootPrefix, out string full))
        {
            return false;
        }

        // The prefix check alone is NOT enough: a junction the user placed inside Drop
        // (…\Drop\baglanti\ → C:\Users\…\Documents) passes the prefix test, but File.Delete follows the
        // junction and deletes the file OUTSIDE Drop. The scan path already prevents this by skipping
        // ReparsePoint (DeleteDropFolderContents); the entry path must get the same guarantee,
        // otherwise the two paths apply different security boundaries to the same input.
        if (!AllDirectoryComponentsAreReal(rootPrefix, full, out string? reparseDir))
        {
            Debug.WriteLine(
                $"[WharfBar] island drop delete skipped, unsafe path component " +
                $"({reparseDir ?? "unreadable"}): {full}");
            return false;
        }

        if (!File.Exists(full))
        {
            return false;
        }

        try
        {
            File.Delete(full);
            Debug.WriteLine($"[WharfBar] island drop copy deleted: {full}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[WharfBar] island drop delete failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Is <b>every directory component</b> between <paramref name="rootPrefix"/> and
    /// <paramref name="full"/> a real directory, i.e. not a <c>ReparsePoint</c>-marked junction /
    /// symbolic link?
    /// </summary>
    /// <param name="offender">
    /// The path of the link-marked component, otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <c>true</c> if safe. <c>false</c> on a linked or <b>unreadable</b> component — in case of
    /// doubt no deletion happens (the same choice as the opt-out fail-closed rule).
    /// </returns>
    internal static bool AllDirectoryComponentsAreReal(string rootPrefix, string full, out string? offender)
    {
        offender = null;

        // The root itself (…\Drop) is the user's/app's folder; component counting starts BELOW it.
        // The file itself is not checked: for a file symlink File.Delete removes the link ITSELF, not
        // the target — only directory links are followed.
        int start = rootPrefix.Length;
        for (int i = start; i < full.Length; i++)
        {
            if (full[i] != Path.DirectorySeparatorChar && full[i] != Path.AltDirectorySeparatorChar)
            {
                continue;
            }

            string dir = full[..i];
            if (dir.Length <= start)
            {
                continue;
            }

            try
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                {
                    offender = dir;
                    return false;
                }
            }
            catch (FileNotFoundException)
            {
                // Component missing: File.Exists(full) will return false anyway, so no deletion happens.
                // Treating it as clean and continuing is safe — the following step already filters it out.
            }
            catch (DirectoryNotFoundException)
            {
                // Same reasoning: an intermediate directory is missing.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Whether an inaccessible component is a link cannot be known -> fail-closed.
                offender = dir;
                Debug.WriteLine($"[WharfBar] island drop component unreadable: {dir} ({ex.Message})");
                return false;
            }
        }

        return true;
    }

    /// <param name="deleteDroppedFiles">
    /// Should copies of drop entries that left the list be deleted too? <c>true</c> for the pruning and
    /// <see cref="Remove"/> paths (the copy would be orphaned because the entry is gone);
    /// <c>false</c> for <see cref="Clear"/> and <see cref="DeleteDroppedFiles"/> — deleting files is an
    /// explicit action and those two paths already manage the deleter themselves.
    /// </param>
    private void ApplySnapshot(
        IReadOnlyList<IslandEntry> next,
        IslandEntry? added,
        bool raiseEntryAdded,
        bool deleteDroppedFiles = true)
    {
        if (_dispatcher.CheckAccess())
        {
            Commit(next, added, raiseEntryAdded, deleteDroppedFiles);
        }
        else if (CanPostToUi)
        {
            _ = _dispatcher.BeginInvoke(
                () => Commit(next, added, raiseEntryAdded, deleteDroppedFiles),
                DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Can work be posted to the UI dispatcher? During a dispose/shutdown race <c>BeginInvoke</c> would
    /// throw <see cref="InvalidOperationException"/>: a <see cref="Remove"/>/<see cref="Clear"/> coming
    /// from off the UI thread after Dispose uses exactly this path.
    /// </summary>
    private bool CanPostToUi =>
        !_disposed && !_dispatcher.HasShutdownStarted && !_dispatcher.HasShutdownFinished;

    /// <summary>Runs on the UI thread: change the list, save, and notify in one place.</summary>
    private void Commit(
        IReadOnlyList<IslandEntry> next,
        IslandEntry? added,
        bool raiseEntryAdded,
        bool deleteDroppedFiles = true)
    {
        // Commit is the single choke point: Remove, Clear, pruning, and ApplyConfig all pass through
        // here, so the "entries dropped from the list" computation also happens in one place. Clear
        // disables that computation: pruning keeps deleting copies (otherwise they would be orphaned),
        // while the explicit "delete files" action is DeleteDroppedFiles.
        if (deleteDroppedFiles)
        {
            QueueDeleteOfDropped(next);
        }

        _entries.Clear();
        _entries.AddRange(next);

        if (IsPersistent)
        {
            ScheduleSave();
        }

        RaiseChanged();

        if (raiseEntryAdded && added is not null)
        {
            EntryAdded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Hands deletion of the drop copies for entries missing from <paramref name="next"/> to the STA
    /// worker (file I/O never runs on the UI thread — the top bar is a registered AppBar, and a
    /// blocked UI freezes the whole desktop).
    /// </summary>
    private void QueueDeleteOfDropped(IReadOnlyList<IslandEntry> next)
    {
        // The comparison is MEMBERSHIP, not count: a ceiling overflow happens exactly while the list
        // is already full at MaxItems, and in that case next.Count == _entries.Count is always equal,
        // so the previous early exit skipped every pruning pass and left the files behind (measured:
        // MaxItems=3, 3 files + text, 25 captures → 3 files leaked). The HashSet was already being
        // built; only the decision looked at the count.
        //
        // An empty list cannot have entries that drop out; this is the one cost-free early exit in a
        // new island's first captures (as the list grows, building the HashSet costs ~MaxItems entries).
        if (_entries.Count == 0)
        {
            return;
        }

        var kept = new HashSet<long>(next.Count);
        foreach (IslandEntry e in next)
        {
            _ = kept.Add(e.Id);
        }

        List<IslandEntry> dropped = [];
        foreach (IslandEntry e in _entries)
        {
            if (!kept.Contains(e.Id))
            {
                dropped.Add(e);
            }
        }

        if (dropped.Count == 0)
        {
            return;
        }

        Dispatcher? sta = _staDispatcher;
        if (sta is null || _disposed)
        {
            return;
        }

        _ = sta.BeginInvoke(() => DeleteFilesOf(dropped), DispatcherPriority.Background);
    }

    private void RaiseChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (CanPostToUi)
        {
            _ = _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty), DispatcherPriority.Background);
        }
    }

    private void ScheduleSave()
    {
        // A Commit arriving after Dispose has no reason to revive the timer: Dispose already flushed
        // the pending write synchronously, and calling Timer.Change on top of that would throw
        // ObjectDisposedException.
        if (_disposed)
        {
            return;
        }

        _savePending = true;
        _saveTimer?.Change(SaveDebounceMs, Timeout.Infinite);
    }

    /// <summary>
    /// The timer callback runs on the threadpool; it hops to the UI thread to read the snapshot.
    /// If <see cref="_dispatcher"/> has shut down, the write already happened in Dispose.
    /// </summary>
    private void OnSaveTimerTick()
    {
        if (!CanPostToUi)
        {
            return;
        }

        try
        {
            _ = _dispatcher.BeginInvoke(FlushSaveOffThread, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            // Shutdown race: the dispatcher may reject BeginInvoke. A lost write matters less than the
            // clipboard itself, but it should not be invisible either.
            Debug.WriteLine($"[WharfBar] island save dispatch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs on the UI thread: only the <b>snapshot</b> is taken here; serialization + writing to disk
    /// is handed to the STA worker.
    /// </summary>
    /// <remarks>
    /// By default 200 entries × 64 KB ≈ 13 MB of <c>JsonSerializer.Serialize</c> +
    /// <c>File.Replace</c> with a 750 ms debounce ran <b>on the AppBar UI thread</b>; because the top
    /// bar is a registered AppBar, that froze the entire desktop for a few seconds. The class's own
    /// rationale had already moved file copy/delete to the STA thread but had left the disk write here.
    /// <para><b>Ordering guarantee:</b> <see cref="IslandStore.Save"/> performs serialization inside its
    /// own <c>_ioGate</c> lock too, so two writes can never interleave. An older write still queued is
    /// dropped by <c>BeginInvokeShutdown</c> after the synchronous flush in <see cref="Dispose"/> — so
    /// the latest snapshot stays on disk.</para>
    /// </remarks>
    private void FlushSaveOffThread()
    {
        if (!_savePending)
        {
            return;
        }

        _savePending = false;
        QueueSave([.. _entries]);
    }

    /// <summary>
    /// <b>Synchronous.</b> Used on the exit path: queuing silently dropped the last copy (shutdown
    /// races process exit), so the write here must stay on the UI thread. Older queued writes are
    /// dropped after this flush.
    /// </summary>
    private void FlushSave()
    {
        if (!_savePending)
        {
            return;
        }

        _savePending = false;
        _store.Save([.. _entries]);
    }

    private void QueueSave(List<IslandEntry> snapshot)
    {
        Dispatcher? sta = _staDispatcher;
        if (sta is null)
        {
            // If there is no STA worker (after Dispose), write synchronously rather than drop the write silently.
            _store.Save(snapshot);
            return;
        }

        _ = sta.BeginInvoke(() => _store.Save(snapshot), DispatcherPriority.Background);
    }

    /// <summary>Must be called on the UI thread (HwndSource.Dispose requires the UI thread).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopListening();
        _saveTimer?.Dispose();

        // Pending debounced write must not be lost on shutdown.
        FlushSave();

        _staDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        _staDispatcher = null;
    }

    /// <summary>
    /// Entry equality by <b>content</b>: the same <c>(Kind, Text, FilePath)</c> triple is the same
    /// entry. Id is deliberately <b>not compared</b> — because every process starts at 1, session and
    /// persistent entries can carry the same Id (see <see cref="MergeWithSession"/>). The rules match
    /// <c>IslandPruner.IsSameContent</c>; that method is private, so they are repeated here (a pure
    /// comparison, one line).
    /// </summary>
    private sealed class EntryContentComparer : IEqualityComparer<IslandEntry>
    {
        internal static readonly EntryContentComparer Instance = new();

        public bool Equals(IslandEntry? x, IslandEntry? y) =>
            ReferenceEquals(x, y)
            || (x is not null
                && y is not null
                && x.Kind == y.Kind
                && string.Equals(x.Text, y.Text, StringComparison.Ordinal)
                && string.Equals(x.FilePath, y.FilePath, StringComparison.OrdinalIgnoreCase));

        public int GetHashCode(IslandEntry obj) => HashCode.Combine(
            (int)obj.Kind,
            obj.Text is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Text),
            obj.FilePath is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FilePath));
    }
}
