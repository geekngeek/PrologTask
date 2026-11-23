namespace PrologMobile.Inventory.Core.Catalog;

public enum InventoryFailureKind
{
    RequestRejected,
    CredentialsRejected,
    Unavailable,
    IncompleteCatalog,
    ConflictingRecords,
    InconsistentCatalog
}

public sealed class InventoryCatalogException : Exception
{
    public InventoryCatalogException(InventoryFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public InventoryFailureKind Kind { get; }

    public static InventoryCatalogException FromAssembly(AssemblyResult result) =>
        result.Status switch
        {
            AssemblyStatus.Incomplete => new InventoryCatalogException(
                InventoryFailureKind.IncompleteCatalog,
                $"The inventory catalog is incomplete. {result.Detail}"),
            AssemblyStatus.Conflict => new InventoryCatalogException(
                InventoryFailureKind.ConflictingRecords,
                result.Detail),
            AssemblyStatus.Inconsistent => new InventoryCatalogException(
                InventoryFailureKind.InconsistentCatalog,
                result.Detail),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "A complete catalog is not a failure.")
        };
}
