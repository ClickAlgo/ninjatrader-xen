using System.Text;
using System.Text.RegularExpressions;

namespace NinjaTrader_Xen.Logging;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggingOptions _options;
    private readonly LogLevel _minimumLevel;
    private readonly object _writeLock = new();
    private DateOnly? _lastCleanupDate;
    private int _writeFailureReported;

    public RollingFileLoggerProvider(FileLoggingOptions options)
    {
        _options = options;
        _minimumLevel = Enum.TryParse<LogLevel>(
            options.MinimumLevel,
            ignoreCase: true,
            out var configuredLevel)
                ? configuredLevel
                : LogLevel.Error;
    }

    public ILogger CreateLogger(string categoryName) =>
        new RollingFileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal bool IsEnabled(LogLevel level) =>
        _options.Enabled &&
        level != LogLevel.None &&
        level >= _minimumLevel;

    internal void Write(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        if (!IsEnabled(level))
            return;

        try
        {
            var now = DateTimeOffset.UtcNow;
            var safeMessage = RedactSecrets(message);
            var safeException = exception is null
                ? string.Empty
                : Environment.NewLine + RedactSecrets(exception.ToString());
            var eventText = eventId.Id == 0
                ? string.Empty
                : $" [{eventId.Id}:{eventId.Name}]";
            var entry =
                $"{now:O} [{level}] {category}{eventText} {safeMessage}" +
                safeException + Environment.NewLine;

            lock (_writeLock)
            {
                var path = ResolveCurrentPath(now.UtcDateTime, entry);
                var directory = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory))
                    return;

                Directory.CreateDirectory(directory);
                using var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(entry);

                var date = DateOnly.FromDateTime(now.UtcDateTime);
                if (_lastCleanupDate != date)
                {
                    _lastCleanupDate = date;
                    RemoveExpiredFiles(directory);
                }
            }
        }
        catch (Exception writeException)
        {
            if (Interlocked.Exchange(ref _writeFailureReported, 1) == 0)
            {
                Console.Error.WriteLine(
                    "NinjaTrader Xen file logging is unavailable: " +
                    RedactSecrets(writeException.Message));
            }
        }
    }

    internal static string RedactSecrets(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        value = Regex.Replace(
            value,
            @"(?i)\b(password|pwd)\s*=\s*[^;\s,]+",
            "$1=[REDACTED]");
        value = Regex.Replace(
            value,
            @"(?i)\bBearer\s+[A-Za-z0-9._~+\-/]+=*",
            "Bearer [REDACTED]");
        value = Regex.Replace(
            value,
            @"\b(?:sk|rk|whsec)_[A-Za-z0-9_\-]{12,}\b|\bsk-[A-Za-z0-9_\-]{12,}\b",
            "[REDACTED_KEY]");
        return value;
    }

    private string ResolveCurrentPath(DateTime utcNow, string entry)
    {
        var configuredPath = Environment.ExpandEnvironmentVariables(
            _options.Path?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath =
                @"C:\XenLogs\NinjaTraderXen-.log";
        }

        if (!System.IO.Path.IsPathRooted(configuredPath))
        {
            configuredPath = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                configuredPath);
        }

        var directory = System.IO.Path.GetDirectoryName(configuredPath) ??
            AppContext.BaseDirectory;
        var extension = System.IO.Path.GetExtension(configuredPath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".log";
        var name = System.IO.Path.GetFileNameWithoutExtension(configuredPath);
        var separator = name.EndsWith('-') ? string.Empty : "-";
        var stem =
            $"{name}{separator}{utcNow:yyyyMMdd}-p{Environment.ProcessId}";
        var sizeLimit = Math.Clamp(
            _options.FileSizeLimitMb,
            1,
            1024) * 1024L * 1024L;
        var entryBytes = Encoding.UTF8.GetByteCount(entry);

        for (var index = 0; ; index++)
        {
            var suffix = index == 0 ? string.Empty : $"-{index:000}";
            var candidate = System.IO.Path.Combine(
                directory,
                stem + suffix + extension);
            if (!File.Exists(candidate) ||
                new FileInfo(candidate).Length + entryBytes <= sizeLimit)
            {
                return candidate;
            }
        }
    }

    private void RemoveExpiredFiles(string directory)
    {
        var configuredName = System.IO.Path.GetFileNameWithoutExtension(
            _options.Path);
        configuredName = configuredName.TrimEnd('-');
        if (string.IsNullOrWhiteSpace(configuredName))
            return;

        var retainedCount = Math.Clamp(
            _options.RetainedFileCount,
            1,
            365);
        var extension = System.IO.Path.GetExtension(_options.Path);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".log";

        var files = Directory
            .EnumerateFiles(directory, configuredName + "-*" + extension)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(retainedCount)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Logging cleanup must never affect the application.
            }
        }
    }

    private sealed class RollingFileLogger(
        RollingFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            provider.Write(
                category,
                logLevel,
                eventId,
                formatter(state, exception),
                exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
