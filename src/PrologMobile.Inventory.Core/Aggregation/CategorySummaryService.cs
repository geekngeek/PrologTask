using PrologMobile.Inventory.Core.Catalog;

namespace PrologMobile.Inventory.Core.Aggregation;

public interface ICategorySummaryService
{
    Task<CategorySummary> GetSummaryAsync(CancellationToken cancellationToken);
}

public sealed class CategorySummaryService(IInventoryCatalog catalog, CategoryAggregator aggregator) : ICategorySummaryService
{
    public async Task<CategorySummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var records = await catalog.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return aggregator.Aggregate(records);
    }
}
