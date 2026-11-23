namespace PrologMobile.Inventory.Core.Catalog;

public enum AssemblyStatus
{
    Complete,
    Incomplete,
    Conflict,
    Inconsistent
}

public sealed record AssemblyResult(
    AssemblyStatus Status,
    IReadOnlyList<InventoryRecord> Records,
    int ExpectedRecords,
    int DuplicateCount,
    string Detail)
{
    public static AssemblyResult Complete(IReadOnlyList<InventoryRecord> records, int expectedRecords, int duplicateCount) =>
        new(AssemblyStatus.Complete, records, expectedRecords, duplicateCount, string.Empty);

    public static AssemblyResult Incomplete(IReadOnlyList<InventoryRecord> records, int expectedRecords, int duplicateCount) =>
        new(
            AssemblyStatus.Incomplete,
            records,
            expectedRecords,
            duplicateCount,
            $"Collected {records.Count} distinct records, expected {expectedRecords}.");

    public static AssemblyResult Conflict(int id, int duplicateCount) =>
        new(
            AssemblyStatus.Conflict,
            [],
            0,
            duplicateCount,
            $"Record {id} was returned with two different payloads.");

    public static AssemblyResult Inconsistent(string detail) =>
        new(AssemblyStatus.Inconsistent, [], 0, 0, detail);
}
