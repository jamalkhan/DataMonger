using Microsoft.Extensions.Logging;

namespace DataMonger.Common;

/// <summary>Writes application diagnostics from the standard ILogger pipeline to a local file.</summary>
public sealed class FileLoggerProvider(string logPath) : ILoggerProvider
{
    private readonly object _sync = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(logPath, categoryName, _sync);

    public void Dispose() { }

    private sealed class FileLogger(string logPath, string categoryName, object sync) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Trace;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {categoryName}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;

            try
            {
                lock (sync) File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never make the application fail.
            }
        }
    }
}
