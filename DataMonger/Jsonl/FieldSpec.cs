using System.Globalization;
using System.Text.Json;

namespace DataMonger.Jsonl;

/// <summary>How a CSV cell is rendered into JSON.</summary>
public enum FieldType
{
    /// <summary>Emit as a JSON string.</summary>
    String,

    /// <summary>Parse as a 64-bit integer and emit as a JSON number (falls back to a string if unparseable).</summary>
    Number,

    /// <summary>Parse "true"/"false" (case-insensitive) and emit as a JSON boolean (falls back to a string).</summary>
    Bool,

    /// <summary>Split on ';' and emit as a JSON array of strings.</summary>
    Labels,
}

/// <summary>
/// Maps one output JSON property to its source CSV column and rendered type. The JSON name may differ
/// from the column name (e.g. Contacts' <c>value</c> comes from the <c>email_address</c> column).
/// </summary>
public sealed record FieldSpec(string Json, string Column, FieldType Type);

/// <summary>
/// Renders typed JSON properties from raw CSV cells, omitting any field whose source cell is empty
/// (null-omission policy). Used by the User Props and Contacts writers.
/// </summary>
public static class JsonFieldWriter
{
    /// <summary>
    /// Writes the property for <paramref name="spec"/> using the value at <paramref name="columnIndex"/>
    /// in <paramref name="row"/>. Empty/missing cells are skipped entirely.
    /// </summary>
    public static void Write(Utf8JsonWriter w, FieldSpec spec, string[] row, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= row.Length)
        {
            return;
        }

        var raw = row[columnIndex];
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        switch (spec.Type)
        {
            case FieldType.Number:
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    w.WriteNumber(spec.Json, n);
                }
                else
                {
                    w.WriteString(spec.Json, raw); // robustness: keep the raw value rather than dropping data
                }
                break;

            case FieldType.Bool:
                if (bool.TryParse(raw, out var b))
                {
                    w.WriteBoolean(spec.Json, b);
                }
                else
                {
                    w.WriteString(spec.Json, raw);
                }
                break;

            case FieldType.Labels:
                var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0)
                {
                    return;
                }
                w.WriteStartArray(spec.Json);
                foreach (var part in parts)
                {
                    w.WriteStringValue(part);
                }
                w.WriteEndArray();
                break;

            default:
                w.WriteString(spec.Json, raw);
                break;
        }
    }
}
