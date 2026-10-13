using System.Globalization;

namespace DataMonger.Common;

// TODO: Remove CsvLoader in favor of bleak.Data.InfiniteDataLoader's CsvReader. Both read delimited files, but
// CsvLoader is the older, naive implementation: it splits on a bare ',' (no RFC 4180 / quoted-field handling),
// reads the whole line eagerly, and is hard-coded to the Person/Product/IdentifiedPerson domain records. CsvReader
// is the forward-only, RFC-4180-aware, bounded-memory replacement. Migrate the remaining callers
// (PurchaseAnonymous, PurchaseKnown, GenerateEmailConversions, Program), then delete this class.
public static class CsvLoader
{
    // All three readers share the same contract: the first line is a header and is discarded
    // (columns are positional), blank/whitespace-only lines are skipped, and a row with too few
    // columns (or an unparseable number) raises InvalidDataException naming the file and the
    // 1-based physical line number. No quoting / escaping and no header detection.

    public static IEnumerable<Person> ReadPeople(string path)
    {
        foreach (var (lineNumber, line) in ReadDataLines(path))
        {
            var f = line.Split(',');
            RequireColumns(path, lineNumber, f.Length, 3);
            yield return new Person(f[0], f[1], f[2]);
        }
    }

    public static IEnumerable<Product> ReadProducts(string path)
    {
        foreach (var (lineNumber, line) in ReadDataLines(path))
        {
            var f = line.Split(',', 9);
            RequireColumns(path, lineNumber, f.Length, 9);
            if (!decimal.TryParse(f[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                throw Invalid(path, lineNumber, $"column 3 (price) '{f[2]}' is not a number");
            }
            if (!int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
            {
                throw Invalid(path, lineNumber, $"column 4 (quantity) '{f[3]}' is not an integer");
            }
            yield return new Product(
                Id: f[0],
                ResourceType: f[1],
                Price: price,
                Quantity: quantity,
                ProductName: f[4],
                Category: f[5],
                Subcategory: f[6],
                Size: f[7],
                Url: f[8]);
        }
    }

    public static IEnumerable<IdentifiedPerson> ReadIdentified(string path)
    {
        foreach (var (lineNumber, line) in ReadDataLines(path))
        {
            var f = line.Split(',');
            RequireColumns(path, lineNumber, f.Length, 4);
            yield return new IdentifiedPerson(
                FirstName: f[0],
                LastName: f[1],
                EmailAddress: f[2],
                UserId: f[3]);
        }
    }

    // Yields (1-based physical line number, line) for every non-blank line after the header.
    private static IEnumerable<(int LineNumber, string Line)> ReadDataLines(string path)
    {
        using var reader = new StreamReader(path);
        reader.ReadLine(); // header, discarded
        var lineNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            yield return (lineNumber, line);
        }
    }

    private static void RequireColumns(string path, int lineNumber, int actual, int required)
    {
        if (actual < required)
        {
            throw Invalid(path, lineNumber, $"expected at least {required} comma-separated columns but found {actual}");
        }
    }

    private static InvalidDataException Invalid(string path, int lineNumber, string detail) =>
        new($"{path}, line {lineNumber}: {detail}.");
}
