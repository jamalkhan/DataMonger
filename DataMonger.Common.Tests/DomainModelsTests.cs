using DataMonger.Common;

namespace DataMonger.Common.Tests;

[TestClass]
public class DomainModelsTests
{
    // Smoke test for the shared domain records that live in DataMonger.Common. Records use value
    // equality, so this also guards against an accidental change to class/struct. Expand as the
    // Common library grows.
    [TestMethod]
    public void Product_ExposesValuesAndUsesValueEquality()
    {
        var a = new Product("1", "product", 9.99m, 3, "Tee", "Tops", "Shirts", "M", "https://example.com/tee");
        var b = new Product("1", "product", 9.99m, 3, "Tee", "Tops", "Shirts", "M", "https://example.com/tee");

        Assert.AreEqual("Tee", a.ProductName);
        Assert.AreEqual(9.99m, a.Price);
        Assert.AreEqual(3, a.Quantity);
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void IdentifiedPerson_RoundTripsValues()
    {
        var p = new IdentifiedPerson("Ada", "Lovelace", "ada@example.com", "1001");

        Assert.AreEqual("Ada", p.FirstName);
        Assert.AreEqual("Lovelace", p.LastName);
        Assert.AreEqual("ada@example.com", p.EmailAddress);
        Assert.AreEqual("1001", p.UserId);
        Assert.AreEqual(p, new IdentifiedPerson("Ada", "Lovelace", "ada@example.com", "1001"));
    }
}
