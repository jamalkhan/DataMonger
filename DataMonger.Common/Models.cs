namespace DataMonger.Common;

public record Person(string FirstName, string LastName, string EmailAddress);

public record IdentifiedPerson(
    string FirstName,
    string LastName,
    string EmailAddress,
    string UserId);

public record Product(
    string Id,
    string ResourceType,
    decimal Price,
    int Quantity,
    string ProductName,
    string Category,
    string Subcategory,
    string Size,
    string Url);
