using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WharfBar.Services;

/// <summary>
/// Taskbar baseline persisted while WharfBar hides the taskbar. Read by the watchdog
/// process to restore the taskbar if WharfBar dies without restoring (hard native
/// crash — corrupting-state exceptions never reach managed handlers).
/// </summary>
internal sealed record TaskbarWatchdogState
{
    public int Pid { get; init; }

    public DateTime ProcessStartTime { get; init; }

    public uint BaselineState { get; init; }

    public int WorkAreaLeft { get; init; }

    public int WorkAreaTop { get; init; }

    public int WorkAreaRight { get; init; }

    public int WorkAreaBottom { get; init; }

    public bool HaveWorkArea { get; init; }
}

/// <summary>JSON file store for <see cref="TaskbarWatchdogState"/>. File present = taskbar hidden by us.</summary>
internal sealed class TaskbarWatchdogStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    internal string Path { get; }

    internal TaskbarWatchdogStateStore(string directory)
    {
        Path = System.IO.Path.Combine(directory, "taskbar-watchdog.json");
    }

    internal bool Exists() => File.Exists(Path);

    internal void Save(TaskbarWatchdogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(Path, JsonSerializer.Serialize(state, JsonOptions));
    }

    /// <summary>Deserializes the state file; missing or corrupt file → null.</summary>
    internal TaskbarWatchdogState? Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<TaskbarWatchdogState>(File.ReadAllText(Path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal void Delete()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort — a stale file only makes the next startup self-heal once more.
        }
    }
}
