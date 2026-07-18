using System.Globalization;
using System.IO;

namespace Launcher.App.Services;

public sealed record LogRunEntry(string FilePath, DateTimeOffset StartedAt);

public interface IInstanceLogWriter : IDisposable
{
    void WriteLine(string text);
}

public interface IInstanceLogHistoryService
{
    Task<IReadOnlyList<LogRunEntry>> GetHistoryAsync(string instanceDirectory, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ReadLogAsync(string filePath, CancellationToken ct = default);

    /// <summary>Opens a new timestamped log file for a launch and returns a writer for its output lines.</summary>
    IInstanceLogWriter StartRun(string instanceDirectory);
}

/// <summary>Persists each launch's Minecraft output to <c>&lt;instance&gt;/launcher_logs/*.log</c> so the
/// Logs tab can show past runs, not just the currently-live one.</summary>
public sealed class InstanceLogHistoryService : IInstanceLogHistoryService
{
    private const string LogsFolderName = "launcher_logs";
    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

    public Task<IReadOnlyList<LogRunEntry>> GetHistoryAsync(string instanceDirectory, CancellationToken ct = default)
    {
        var dir = Path.Combine(instanceDirectory, LogsFolderName);
        if (!Directory.Exists(dir))
        {
            return Task.FromResult<IReadOnlyList<LogRunEntry>>([]);
        }

        var entries = Directory.GetFiles(dir, "*.log")
            .Select(f => new LogRunEntry(f, ParseTimestamp(f)))
            .OrderByDescending(e => e.StartedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<LogRunEntry>>(entries);
    }

    public async Task<IReadOnlyList<string>> ReadLogAsync(string filePath, CancellationToken ct = default) =>
        await File.ReadAllLinesAsync(filePath, ct);

    public IInstanceLogWriter StartRun(string instanceDirectory)
    {
        var dir = Path.Combine(instanceDirectory, LogsFolderName);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTimeOffset.Now.ToString(TimestampFormat)}.log");
        return new LogWriter(path);
    }

    private static DateTimeOffset ParseTimestamp(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return DateTimeOffset.TryParseExact(
            name, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : new DateTimeOffset(File.GetCreationTimeUtc(filePath));
    }

    private sealed class LogWriter : IInstanceLogWriter
    {
        private readonly StreamWriter _writer;

        public LogWriter(string path)
        {
            _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        }

        public void WriteLine(string text)
        {
            lock (_writer)
            {
                _writer.WriteLine(text);
            }
        }

        public void Dispose() => _writer.Dispose();
    }
}
