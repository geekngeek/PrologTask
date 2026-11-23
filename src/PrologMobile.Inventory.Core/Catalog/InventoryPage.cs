namespace PrologMobile.Inventory.Core.Catalog;

public sealed record InventoryPage(
    int RequestedPage,
    int ReportedPage,
    int ReportedPageSize,
    int TotalPages,
    int TotalRecords,
    IReadOnlyList<InventoryRecord> Records);
