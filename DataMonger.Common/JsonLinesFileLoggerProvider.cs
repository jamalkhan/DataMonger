using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace DataMonger.Common;

/// <summary>
/// Buffered ILogger sink that writes one JSON object per line ("JSON lines") to a single file.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>Log</c> call serializes its entry on the calling thread (so the message, structured properties and the
/// current scopes are captured exactly as they were at call time) into a pooled per-thread buffer, then hands the
/// resulting bytes to an unbounded <see cref="Channel{T}"/>. One background task drains the channel into a single
/// open <see cref="FileStream"/> and flushes whenever the channel runs empty. Callers never wait on disk I/O, so
/// logging does not distort the timings it records.
/// </para>
/// <para>
/// Line shape: <c>Timestamp</c> (ISO-8601 UTC), <c>Level</c>, <c>Category</c>, <c>EventId</c>, <c>EventName</c>
/// (when set), <c>Message</c> (formatted), then the structured state properties (without <c>{OriginalFormat}</c>),
/// then the scope properties (innermost scope wins on a name clash; non key/value scopes go to a <c>Scope</c> array),
/// and <c>Exception</c> (<see cref="Exception.ToString"/>) when present. A property whose name collides with one of
/// those fixed names is written with a leading underscore.
/// </para>
/// <para>
/// Logging never throws into the application: a file that cannot be opened turns the provider into a no-op, and a
/// write error stops further writes. <see cref="Dispose"/> completes the channel, drains what is queued (waiting at
/// most <see cref="DrainTimeout"/>), flushes and closes the file.
/// </para>
/// </remarks>
public sealed class JsonLinesFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private const string OriginalFormatKey = "{OriginalFormat}";
    private const int MaxRetainedBufferBytes = 1024 * 1024;
    private const int MaxEnumerableItems = 100;

    /// <summary>How long <see cref="Dispose"/> waits for queued lines to reach the file.</summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] ReservedNames =
        ["Timestamp", "Level", "Category", "EventId", "EventName", "Message", "Exception", "Scope"];

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Log files are read by people and tools, never embedded in HTML, so keep non-ASCII readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [ThreadStatic] private static ArrayBufferWriter<byte>? t_buffer;
    [ThreadStatic] private static Utf8JsonWriter? t_writer;
    [ThreadStatic] private static bool t_busy;

    private readonly Channel<byte[]> _channel;
    private readonly FileStream? _stream;
    private readonly Task _writerTask;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private volatile bool _faulted;
    private int _disposed;

    public JsonLinesFileLoggerProvider(string path)
    {
        Path = path;
        _channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        try
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024);
        }
        catch (Exception)
        {
            // Logging must never make the application fail: without a file, the provider is a no-op.
            _stream = null;
            _channel.Writer.TryComplete();
        }

        _writerTask = _stream is null ? Task.CompletedTask : Task.Run(WriteLoopAsync);
    }

    /// <summary>The file this provider appends to.</summary>
    public string Path { get; }

    /// <summary>False when the file could not be opened, a write failed, or the provider was disposed.</summary>
    public bool IsWriting => _stream is not null && !_faulted && Volatile.Read(ref _disposed) == 0;

    public ILogger CreateLogger(string categoryName) => new JsonLinesLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _channel.Writer.TryComplete();
        try
        {
            _writerTask.Wait(DrainTimeout);
        }
        catch (Exception)
        {
            // The write loop swallows its own errors; nothing useful to do here.
        }

        try
        {
            _stream?.Flush();
            _stream?.Dispose();
        }
        catch (Exception)
        {
            // Never throw from logging.
        }
    }

    private async Task WriteLoopAsync()
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out var line))
                {
                    if (_faulted)
                    {
                        continue; // keep draining so the channel does not grow
                    }
                    try
                    {
                        _stream!.Write(line);
                    }
                    catch (Exception)
                    {
                        _faulted = true;
                    }
                }

                if (!_faulted)
                {
                    try
                    {
                        _stream!.Flush();
                    }
                    catch (Exception)
                    {
                        _faulted = true;
                    }
                }
            }
        }
        catch (Exception)
        {
            _faulted = true;
        }
    }

    private void Enqueue<TState>(
        string category, LogLevel level, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (_stream is null || _faulted || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            var line = Serialize(category, level, eventId, state, exception, formatter, _scopeProvider);
            _channel.Writer.TryWrite(line);
        }
        catch (Exception)
        {
            // A value whose ToString throws, a formatter bug, etc.: drop the line, never the app.
        }
    }

    /// <summary>Renders one log entry as a UTF-8 JSON object followed by '\n'.</summary>
    internal static byte[] Serialize<TState>(
        string category, LogLevel level, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter, IExternalScopeProvider? scopeProvider)
    {
        // Re-entrant logging (a value's ToString that logs) gets its own buffer instead of the thread's.
        var reentrant = t_busy;
        var buffer = reentrant ? new ArrayBufferWriter<byte>(256) : t_buffer ??= new ArrayBufferWriter<byte>(1024);
        Utf8JsonWriter json;
        if (reentrant)
        {
            json = new Utf8JsonWriter(buffer, WriterOptions);
        }
        else
        {
            json = t_writer ??= new Utf8JsonWriter(buffer, WriterOptions);
            json.Reset(buffer);
            t_busy = true;
        }

        try
        {
            var written = new List<string>(8);
            json.WriteStartObject();
            json.WriteString("Timestamp", DateTime.UtcNow);
            json.WriteString("Level", LevelName(level));
            json.WriteString("Category", category);
            json.WriteNumber("EventId", eventId.Id);
            if (!string.IsNullOrEmpty(eventId.Name))
            {
                json.WriteString("EventName", eventId.Name);
            }
            json.WriteString("Message", formatter(state, exception));

            if (state is IEnumerable<KeyValuePair<string, object?>> stateProperties)
            {
                WriteProperties(json, stateProperties, written);
            }

            if (scopeProvider is not null)
            {
                var scopes = new List<object?>();
                scopeProvider.ForEachScope(static (scope, list) => list.Add(scope), scopes);
                List<string>? plainScopes = null;
                // Innermost first, so a nested scope overrides an outer one on a name clash.
                for (var i = scopes.Count - 1; i >= 0; i--)
                {
                    switch (scopes[i])
                    {
                        case null:
                            break;
                        case IEnumerable<KeyValuePair<string, object?>> scopeProperties:
                            WriteProperties(json, scopeProperties, written);
                            break;
                        case var other:
                            (plainScopes ??= new List<string>()).Add(other.ToString() ?? "");
                            break;
                    }
                }
                if (plainScopes is not null)
                {
                    json.WriteStartArray("Scope");
                    for (var i = plainScopes.Count - 1; i >= 0; i--)
                    {
                        json.WriteStringValue(plainScopes[i]);
                    }
                    json.WriteEndArray();
                }
            }

            if (exception is not null)
            {
                json.WriteString("Exception", exception.ToString());
            }

            json.WriteEndObject();
            json.Flush();
            buffer.Write("\n"u8);
            return buffer.WrittenSpan.ToArray();
        }
        finally
        {
            if (reentrant)
            {
                json.Dispose();
            }
            else
            {
                t_busy = false;
                if (buffer.Capacity > MaxRetainedBufferBytes)
                {
                    // Don't keep a huge buffer alive on every thread after one big Trace body.
                    t_buffer = null;
                    t_writer = null;
                }
                else
                {
                    buffer.Clear();
                }
            }
        }
    }

    private static void WriteProperties(
        Utf8JsonWriter json, IEnumerable<KeyValuePair<string, object?>> properties, List<string> written)
    {
        foreach (var (rawName, value) in properties)
        {
            if (string.IsNullOrEmpty(rawName) || rawName == OriginalFormatKey)
            {
                continue;
            }
            var name = Array.IndexOf(ReservedNames, rawName) >= 0 ? "_" + rawName : rawName;
            if (written.Contains(name))
            {
                continue;
            }
            written.Add(name);
            json.WritePropertyName(name);
            WriteValue(json, value, depth: 0);
        }
    }

    private static void WriteValue(Utf8JsonWriter json, object? value, int depth)
    {
        switch (value)
        {
            case null: json.WriteNullValue(); break;
            case string s: json.WriteStringValue(s); break;
            case bool b: json.WriteBooleanValue(b); break;
            case int i: json.WriteNumberValue(i); break;
            case long l: json.WriteNumberValue(l); break;
            case short sh: json.WriteNumberValue(sh); break;
            case byte by: json.WriteNumberValue(by); break;
            case sbyte sb: json.WriteNumberValue(sb); break;
            case ushort us: json.WriteNumberValue(us); break;
            case uint ui: json.WriteNumberValue(ui); break;
            case ulong ul: json.WriteNumberValue(ul); break;
            case decimal m: json.WriteNumberValue(m); break;
            case double d when double.IsFinite(d): json.WriteNumberValue(d); break;
            case float f when float.IsFinite(f): json.WriteNumberValue(f); break;
            case double d: json.WriteStringValue(d.ToString(CultureInfo.InvariantCulture)); break;
            case float f: json.WriteStringValue(f.ToString(CultureInfo.InvariantCulture)); break;
            case DateTime dt: json.WriteStringValue(dt); break;
            case DateTimeOffset dto: json.WriteStringValue(dto); break;
            case Guid g: json.WriteStringValue(g); break;
            case TimeSpan ts: json.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture)); break;
            case Enum e: json.WriteStringValue(e.ToString()); break;
            case Uri u: json.WriteStringValue(u.ToString()); break;
            case IEnumerable items when depth == 0:
                json.WriteStartArray();
                var count = 0;
                foreach (var item in items)
                {
                    if (count++ == MaxEnumerableItems)
                    {
                        json.WriteStringValue("...");
                        break;
                    }
                    WriteValue(json, item, depth + 1);
                }
                json.WriteEndArray();
                break;
            case IFormattable formattable: json.WriteStringValue(formattable.ToString(null, CultureInfo.InvariantCulture)); break;
            default: json.WriteStringValue(value.ToString()); break;
        }
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Information",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Critical",
        _ => "None",
    };

    private sealed class JsonLinesLogger(string category, JsonLinesFileLoggerProvider provider) : ILogger
    {
        // Only used when the logger is called directly rather than through a LoggerFactory (which pushes scopes
        // straight onto the external scope provider it hands us via SetScopeProvider).
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && provider.IsWriting;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.None)
            {
                return;
            }
            provider.Enqueue(category, logLevel, eventId, state, exception, formatter);
        }
    }
}
