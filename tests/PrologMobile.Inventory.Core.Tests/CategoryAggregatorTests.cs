using PrologMobile.Inventory.Core.Aggregation;
using PrologMobile.Inventory.Core.Catalog;

namespace PrologMobile.Inventory.Core.Tests;

public class CategoryAggregatorTests
{
    private readonly CategoryAggregator _aggregator = new();

    [Fact]
    public void Aggregate_ExcludesZeroAndNegativeQuantity()
    {
        var newest = When(3, quantity: 0, category: "Electronics", at: "2026-12-01T00:00:00Z");
        var inStock = When(1, quantity: 2, price: 10.25m, category: "Electronics", at: "2026-10-01T00:00:00Z");
        var negative = When(2, quantity: -4, category: "Electronics", at: "2026-11-01T00:00:00Z");

        var summary = _aggregator.Aggregate([newest, inStock, negative]);

        var category = Assert.Single(summary.Categories);
        Assert.Equal(20.50m, category.TotalInventoryValue);
        Assert.Equal(1, category.AvailableRecordCount);
        Assert.Equal(1, category.MostRecentlyUpdated.Id);
        var item = Assert.Single(category.Items);
        Assert.Equal(1, item.Id);
    }

    [Fact]
    public void Aggregate_SumsPriceTimesQuantityInDecimal()
    {
        var summary = _aggregator.Aggregate(
        [
            When(1, price: 0.10m, quantity: 3),
            When(2, price: 0.20m, quantity: 1)
        ]);

        Assert.Equal(0.50m, Assert.Single(summary.Categories).TotalInventoryValue);
    }

    [Fact]
    public void Aggregate_GroupsCategoriesByTrimmedCaseInsensitiveLabel()
    {
        var summary = _aggregator.Aggregate(
        [
            When(1, category: "  electronics "),
            When(2, category: "ELECTRONICS"),
            When(3, category: "Home   &   Kitchen"),
            When(4, category: "home & kitchen")
        ]);

        Assert.Equal(["ELECTRONICS", "Home & Kitchen"], summary.Categories.Select(category => category.Category).ToArray());
        Assert.Equal(2, summary.Categories[0].AvailableRecordCount);
        Assert.Equal(2, summary.Categories[1].AvailableRecordCount);
    }

    [Fact]
    public void Aggregate_DisplayNameTieBreaksByOrdinal()
    {
        var summary = _aggregator.Aggregate(
        [
            When(1, category: "Toys"),
            When(2, category: "TOYS")
        ]);

        Assert.Equal("TOYS", Assert.Single(summary.Categories).Category);
    }

    [Fact]
    public void Aggregate_MostRecentTieBreaksByHigherId()
    {
        var summary = _aggregator.Aggregate(
        [
            When(4, at: "2026-10-15T12:30:00Z"),
            When(9, at: "2026-10-15T12:30:00Z")
        ]);

        Assert.Equal(9, Assert.Single(summary.Categories).MostRecentlyUpdated.Id);
    }

    [Fact]
    public void Aggregate_OrdersCategoriesByNameAndItemsById()
    {
        var summary = _aggregator.Aggregate(
        [
            When(30, category: "Toys"),
            When(10, category: "Electronics"),
            When(20, category: "Electronics")
        ]);

        Assert.Equal(["Electronics", "Toys"], summary.Categories.Select(category => category.Category).ToArray());
        Assert.Equal([10, 20], summary.Categories[0].Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public void Aggregate_OmitsCategoriesWithNoAvailableStock()
    {
        var summary = _aggregator.Aggregate([When(1, quantity: 0, category: "Discontinued")]);

        Assert.Empty(summary.Categories);
    }

    [Fact]
    public void Aggregate_EmptyInput_ReturnsNoCategories()
    {
        var summary = _aggregator.Aggregate([]);

        Assert.Empty(summary.Categories);
    }

    [Fact]
    public void Aggregate_DuplicateIds_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => _aggregator.Aggregate([When(5), When(5)]));

        Assert.Contains("5", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregate_BlankCategory_Throws()
    {
        Assert.Throws<ArgumentException>(() => _aggregator.Aggregate([When(1, category: "   ")]));
    }

    private static InventoryRecord When(
        int id,
        string category = "Electronics",
        decimal price = 10m,
        int quantity = 1,
        string? at = null)
    {
        var updated = at is null
            ? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(id)
            : DateTimeOffset.Parse(at);
        return new InventoryRecord(id, $"Item {id}", category, price, quantity, updated);
    }
}
