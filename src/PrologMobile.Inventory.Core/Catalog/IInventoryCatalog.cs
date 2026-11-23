namespace PrologMobile.Inventory.Core.Catalog;

public interface IInventoryCatalog
{
    Task<IReadOnlyList<InventoryRecord>> GetAllAsync(CancellationToken cancellationToken);
}
