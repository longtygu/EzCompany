using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using EzCompany.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace EzCompany.App.Services;

/// <summary>
/// Thread-safe daily rolling file logger writing structured diagnostics to
/// <c>%LocalAppData%\EzCompany\Profiles\&lt;profile&gt;\Logs\ezcompany-yyyyMMdd.log</c>.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private const int RetentionDays = 14;

    private readonly ILocalStoragePaths _paths;
    private readonly LogLevel _minLevel;
    private readonly int _processId;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, FileCategoryLogger> _loggers = new(StringComparer.Ordinal);

    private string _activeDateKey = string.Empty;
    private StreamWriter? _writer;
    private bool _disposed;

    public RollingFileLoggerProvider(ILocalStoragePaths paths, LogLevel? overrideMinLevel = null)
    {
        _paths = paths;
        _processId = Environment.ProcessId;
        _minLevel = overrideMinLevel ?? ParseMinLevelFromEnvironment();
        Directory.CreateDirectory(_paths.LogsDirectory);
        PruneOldLogs();
        EnsureWriterForDate(DateTimeOffset.Now);
    }

    public string LogsDirectory => _paths.LogsDirectory;
    public LogLevel MinimumLevel => _minLevel;
    public string CurrentLogFilePath { get; private set; } = string.Empty;

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileCategoryLogger(this, name));

    public void Flush()
    {
        lock (_writeLock)
        {
            _writer?.Flush();
        }
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal bool IsEnabled(LogLevel logLevel) =>
        !_disposed && logLevel != LogLevel.None && logLevel >= _minLevel;

    internal void WriteEntry(
        LogLevel logLevel,
        string categoryName,
        EventId eventId,
        string message,
        Exception? exception)
    {
        if (!IsEnabled(logLevel)) return;

        DateTimeOffset now = DateTimeOffset.Now;
        int threadId = Environment.CurrentManagedThreadId;
        string levelTag = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "LOG"
        };

        var sb = new StringBuilder(256);
        sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
          .Append(" [").Append(levelTag).Append(']')
          .Append(" [profile:").Append(_paths.ProfileName).Append(']')
          .Append(" [pid:").Append(_processId).Append(" tid:").Append(threadId).Append("] ")
          .Append(categoryName);

        if (eventId.Id != 0)
        {
            sb.Append(" (").Append(eventId.Id).Append(')');
        }

        sb.Append(": ").Append(message);

        if (exception is not null)
        {
            sb.AppendLine().Append(exception);
        }

        lock (_writeLock)
        {
            if (_disposed) return;
            EnsureWriterForDate(now);
            _writer?.WriteLine(sb.ToString());
        }
    }

    private void EnsureWriterForDate(DateTimeOffset now)
    {
        string dateKey = now.ToString("yyyyMMdd");
        if (_writer is not null && string.Equals(_activeDateKey, dateKey, StringComparison.Ordinal))
        {
            return;
        }

        _writer?.Dispose();
        _activeDateKey = dateKey;
        CurrentLogFilePath = Path.Combine(_paths.LogsDirectory, $"ezcompany-{dateKey}.log");

        var stream = new FileStream(
            CurrentLogFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    private void PruneOldLogs()
    {
        try
        {
            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (string file in Directory.EnumerateFiles(_paths.LogsDirectory, "ezcompany-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Never block startup on log cleanup
        }
    }

    private static LogLevel ParseMinLevelFromEnvironment()
    {
        string? raw = Environment.GetEnvironmentVariable("EZCOMPANY_LOG_LEVEL");
        return Enum.TryParse(raw, ignoreCase: true, out LogLevel parsed) ? parsed : LogLevel.Information;
    }

    private sealed class FileCategoryLogger(RollingFileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string msg = formatter(state, exception);
            if (string.IsNullOrEmpty(msg) && exception is null) return;
            provider.WriteEntry(logLevel, categoryName, eventId, msg, exception);
        }
    }
}
