using System.Text.RegularExpressions;
using PrologMobile.Inventory.Core.Catalog;

namespace PrologMobile.Inventory.Core.Aggregation;

public sealed partial class CategoryAggregator
{
    public CategorySummary Aggregate(IReadOnlyList<InventoryRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var duplicate = records.GroupBy(record => record.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Record {duplicate.Key} appears more than once. Aggregation expects a deduplicated catalog.");
        }

        var categories = records
            .Where(record => record.Quantity > 0)
            .GroupBy(record => Normalize(record.Category))
            .Select(ToCategory)
            .OrderBy(category => category.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CategorySummary(categories);
    }

    private static CategoryAggregate ToCategory(IGrouping<string, InventoryRecord> group)
    {
        if (group.Key.Length == 0)
            throw new ArgumentException("A record has a blank category.");

        var displayName = group
            .Select(record => Collapse(record.Category))
            .GroupBy(name => name, StringComparer.Ordinal)
            .OrderByDescending(spelling => spelling.Count())
            .ThenBy(spelling => spelling.Key, StringComparer.Ordinal)
            .First()
            .Key;

        var items = group
            .OrderBy(record => record.Id)
            .Select(record => new CategoryItem(
                record.Id,
                record.Name,
                record.Price,
                record.Quantity,
                record.LastUpdated))
            .ToArray();

        var mostRecent = group
            .OrderByDescending(record => record.LastUpdated)
            .ThenByDescending(record => record.Id)
            .First();

        var total = group.Aggregate(0m, (sum, record) => sum + record.Price * record.Quantity);

        return new CategoryAggregate(
            displayName,
            total,
            items.Length,
            new MostRecentlyUpdatedRecord(mostRecent.Id, mostRecent.Name, mostRecent.LastUpdated),
            items);
    }

    public static string Normalize(string category) => Collapse(category).ToUpperInvariant();

    public static string Collapse(string category)
    {
        var trimmed = category.Trim();
        return Whitespace().Replace(trimmed, " ");
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
