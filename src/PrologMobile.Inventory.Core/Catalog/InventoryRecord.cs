namespace PrologMobile.Inventory.Core.Catalog;

public sealed record InventoryRecord(
    int Id,
    string Name,
    string Category,
    decimal Price,
    int Quantity,
    DateTimeOffset LastUpdated);
