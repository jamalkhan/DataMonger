using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DataMonger.Jsonl;

/// <summary>Shared JSON writer configuration and a convenience renderer (used by tests).</summary>
public static class JsonlRender
{
    /// <summary>
    /// Compact output (one line per record) with relaxed escaping, so values like <c>+00:00</c>
    /// timestamps and <c>@</c> in emails are written literally rather than as \uXXXX escapes.
    /// </summary>
    public static readonly JsonWriterOptions Options = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Renders a single JSON document to a string via a fresh buffer. Convenience for tests.</summary>
    public static string ToJsonString(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            write(writer);
            writer.Flush();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Resolves each field spec's source column index against a header row (case-insensitive).</summary>
    public static int[] ResolveColumns(IReadOnlyList<FieldSpec> specs, IReadOnlyList<string> headers)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            lookup[headers[i]] = i;
        }

        var indices = new int[specs.Count];
        for (var i = 0; i < specs.Count; i++)
        {
            indices[i] = lookup.TryGetValue(specs[i].Column, out var idx) ? idx : -1;
        }
        return indices;
    }
}
