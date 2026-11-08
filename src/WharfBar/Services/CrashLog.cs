using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Best-effort crash log for unhandled exceptions.
///
/// Without these handlers a WPF app dies with only "Application Error 1000" (+ WER dump) in the
/// Windows event log and no managed stack: the 2026-11-08 island-tab crash could only be diagnosed
/// by analyzing the WER crash dump. The log also records the process integrity RID, because a
/// restricted launch is the most likely cause of unexpected file-write failures.
///
/// Every write is best-effort: a low-integrity process cannot write into the medium-integrity
/// profile folders, so <c>%LOCALAPPDATA%</c>, <c>%TEMP%</c> and the application folder are tried in
/// turn and any failure is swallowed (a crash log must never become the second crash).
/// </summary>
internal static class CrashLog
{
    private static readonly object Gate = new();
    private static int _installed;

    /// <summary>Hooks the dispatcher, app-domain and task-scheduler unhandled-exception events.</summary>
    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.UnhandledException += (_, e) => Write("dispatcher", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("appdomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Writes one crash record and returns its path, or <c>null</c> if no directory was writable.</summary>
    internal static string? Write(string source, Exception? exception)
    {
        string text = BuildText(source, exception);
        foreach (string directory in CandidateDirectories())
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(
                    directory,
                    $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
                lock (Gate)
                {
                    File.AppendAllText(path, text);
                }

                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        string? local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
        {
            yield return Path.Combine(local, "WharfBar", "logs");
        }

        yield return Path.GetTempPath();
        yield return AppContext.BaseDirectory;
    }

    private static string BuildText(string source, Exception? exception)
    {
        string rid;
        try
        {
            rid = ProcessIntegrity.TryGetCurrentRid(out int value) ? $"0x{value:X} ({value})" : "unreadable";
        }
        catch (Exception)
        {
            rid = "probe failed";
        }

        return string.Join(
            Environment.NewLine,
            $"==== WharfBar crash {DateTime.Now:O} ====",
            $"source:   {source}",
            $"process:  {Environment.ProcessPath}",
            $"pid:      {Environment.ProcessId}",
            $"integrityRid: {rid}",
            $"os:       {RuntimeInformation.OSDescription}",
            $"runtime:  {RuntimeInformation.FrameworkDescription}",
            exception is null ? "(no managed exception object)" : exception.ToString(),
            string.Empty);
    }
}
