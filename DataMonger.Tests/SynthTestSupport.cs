using System.Buffers;
using System.Globalization;
using System.Text.Json;
using DataMonger.Jsonl;
using DataMonger.Synth;

namespace DataMonger.Tests;

/// <summary>Shared helpers for the Synth*Tests classes.</summary>
internal static class SynthTestSupport
{
    public static readonly DateTimeOffset Anchor = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public static string FindExampleConfig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(dir.FullName, "data", "synthconfig", "acme.synthconfig"),
                Path.Combine(dir.FullName, "DataMonger", "data", "synthconfig", "acme.synthconfig"),
            })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("acme.synthconfig not found above " + AppContext.BaseDirectory);
    }

    public static SynthConfig LoadExample(int? seed = 42)
    {
        var c = SynthConfigLoader.FromFile(FindExampleConfig());
        c.Seed = seed;
        return c;
    }

    /// <summary>Generates profiles the way the command does and returns each as a JSON string.</summary>
    public static List<string> GenerateProfiles(SynthConfig config, int count, DateTimeOffset? now = null)
    {
        var synth = new UserPropsSynthesizer(config, new Random(config.Seed ?? 0), now ?? Anchor);
        var result = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer, JsonlRender.Options))
            {
                synth.Write(w, config.StartUserSeq + i);
            }
            result.Add(System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
        return result;
    }

    public static ProductCatalog SmallCatalog() => ProductCatalog.Parse(new[]
    {
        """{"sku":"A-1","name":"Acme Tee","price":10.0,"category":"Tops","color":"Red","size":"M","url":"https://www.acme.example.com/p/a-1"}""",
        """{"sku":"A-2","name":"Acme Hat","price":20.5,"category":"Hats","url":"https://www.acme.example.com/p/a-2"}""",
        """{"sku":"K-1","name":"Acme Kids Boot","price":35.0,"category":"Footwear","url":"https://kids.acme.example.com/p/k-1"}""",
        """{"sku":"A-3","name":"Acme Jacket","price":80.0,"category":"Outerwear"}""",
    });

    public static JsonDocument Doc(string json) => JsonDocument.Parse(json);

    public static string Profile(string? timezone = "America/New_York", string email = "ada@example.com", string? userId = "CUST_1",
        string consent = """{"email":true,"sms":true,"push":true,"postal":true}""")
    {
        var tz = timezone is null ? "" : $"\"timezone\":\"{timezone}\",";
        var uid = userId is null ? "" : $"\"user_id\":\"{userId}\",";
        return $$"""{ {{uid}} {{tz}} "identifiers":[{"type":"email","value":"{{email}}"}], "consent": {{consent}} }""";
    }

    public static CultureScope Culture(string name) => new(name);

    public sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _old = CultureInfo.CurrentCulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);
        public void Dispose() => CultureInfo.CurrentCulture = _old;
    }
}
