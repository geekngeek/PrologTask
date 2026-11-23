namespace PrologMobile.Inventory.Core.Aggregation;

public sealed record CategorySummary(IReadOnlyList<CategoryAggregate> Categories);

public sealed record CategoryAggregate(
    string Category,
    decimal TotalInventoryValue,
    int AvailableRecordCount,
    MostRecentlyUpdatedRecord MostRecentlyUpdated,
    IReadOnlyList<CategoryItem> Items);

public sealed record MostRecentlyUpdatedRecord(
    int Id,
    string Name,
    DateTimeOffset LastUpdated);

public sealed record CategoryItem(
    int Id,
    string Name,
    decimal Price,
    int Quantity,
    DateTimeOffset LastUpdated);
