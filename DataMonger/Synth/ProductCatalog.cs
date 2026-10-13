using System.Text.Json;

namespace DataMonger.Synth;

/// <summary>
/// One purchasable product variant (a single sku) picked from the product feed.
/// </summary>
public sealed record ProductVariant(
    string Sku,
    string ProductId,
    string Name,
    string Brand,
    string Category,
    string Subcategory,
    decimal Price,
    decimal ListPrice,
    string Color,
    string Size,
    string ImageUrl,
    string Url);

/// <summary>
/// Loads a product-feed JSONL file (one sku per line; see Documentation/SynthesizeSessionEvents/InputFormat.md)
/// and lets callers pick a random purchasable variant. Rows missing sku, name or a numeric price, rows that repeat
/// an earlier sku, and rows with in_stock=false are skipped (and counted).
/// </summary>
public sealed class ProductCatalog
{
    public const string DefaultBrand = "Acme";
    public const string KidsBrand = "Acme Kids";

    private readonly IReadOnlyList<Entry> _entries;

    private ProductCatalog(IReadOnlyList<Entry> entries, int skippedInvalid, int skippedDuplicate, int skippedOutOfStock)
    {
        _entries = entries;
        SkippedInvalid = skippedInvalid;
        SkippedDuplicate = skippedDuplicate;
        SkippedOutOfStock = skippedOutOfStock;
    }

    public int Count => _entries.Count;
    public int SkippedInvalid { get; }
    public int SkippedDuplicate { get; }
    public int SkippedOutOfStock { get; }

    /// <summary>Picks a random variant; when category/subcategory hold several pipe-separated values one is picked too.</summary>
    public ProductVariant RandomVariant(Random rng)
    {
        var e = _entries[rng.Next(_entries.Count)];
        var category = e.Categories.Length == 0 ? "" : e.Categories[rng.Next(e.Categories.Length)];
        var subcategory = e.Subcategories.Length == 0 ? "" : e.Subcategories[rng.Next(e.Subcategories.Length)];
        return new ProductVariant(
            e.Sku, e.ProductId, e.Name, e.Brand, category, subcategory,
            e.Price, e.ListPrice, e.Color, e.Size, e.ImageUrl, e.Url);
    }

    public static ProductCatalog Load(string path) => Parse(File.ReadLines(path), path);

    /// <summary>Parses feed lines. <paramref name="source"/> is only used in messages.</summary>
    public static ProductCatalog Parse(IEnumerable<string> lines, string source = "product feed")
    {
        var entries = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int invalid = 0, duplicate = 0, outOfStock = 0;
        var lineNo = 0;

        foreach (var line in lines)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                invalid++;
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    invalid++;
                    continue;
                }

                var sku = GetString(root, "sku");
                var name = GetString(root, "name");
                var price = GetDecimal(root, "price");
                if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(name) || price is null || price < 0)
                {
                    invalid++;
                    continue;
                }
                if (!seen.Add(sku))
                {
                    duplicate++;
                    continue;
                }
                if (root.TryGetProperty("in_stock", out var stock) && stock.ValueKind == JsonValueKind.False)
                {
                    outOfStock++;
                    continue;
                }

                var url = GetString(root, "url") ?? "";
                var brand = GetString(root, "brand");
                entries.Add(new Entry(
                    Sku: sku,
                    ProductId: GetString(root, "product_id") ?? sku,
                    Name: name,
                    Brand: string.IsNullOrWhiteSpace(brand) ? BrandFromUrl(url) : brand,
                    Categories: SplitPipes(GetString(root, "category")),
                    Subcategories: SplitPipes(GetString(root, "subcategory")),
                    Price: price.Value,
                    ListPrice: GetDecimal(root, "list_price") ?? price.Value,
                    Color: GetString(root, "color") ?? "",
                    Size: GetString(root, "size") ?? "",
                    ImageUrl: GetString(root, "image_url") ?? "",
                    Url: url));
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"No purchasable products found in {source} (rows need sku, name and a numeric price).");
        }
        return new ProductCatalog(entries, invalid, duplicate, outOfStock);
    }

    /// <summary>
    /// Brand derived from a product URL when the row has no "brand": a host or first path segment
    /// that starts with "kids" (kids.acme.example.com, www.acme.example.com/kids/...) means
    /// "Acme Kids"; anything else, including an empty URL, is "Acme".
    /// </summary>
    public static string BrandFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return DefaultBrand;
        }
        var host = uri.Host.Split('.');
        var firstSegment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var isKids = host.Any(h => h.Equals("kids", StringComparison.OrdinalIgnoreCase))
            || firstSegment.Equals("kids", StringComparison.OrdinalIgnoreCase);
        return isKids ? KidsBrand : DefaultBrand;
    }

    private static string[] SplitPipes(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? GetDecimal(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    private sealed record Entry(
        string Sku, string ProductId, string Name, string Brand, string[] Categories, string[] Subcategories,
        decimal Price, decimal ListPrice, string Color, string Size, string ImageUrl, string Url);
}
