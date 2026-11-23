using PrologMobile.Inventory.Core.Catalog;

namespace PrologMobile.Inventory.Core.Tests;

public class CatalogAssemblerTests
{
    [Fact]
    public void Assemble_IdenticalOverlap_IsCompleteAndCountsTheCopyOnce()
    {
        var shared = Record(2);
        var pages = new[]
        {
            Page(1, totalPages: 2, totalRecords: 3, Record(1), shared),
            Page(2, totalPages: 2, totalRecords: 3, shared, Record(3))
        };

        var result = CatalogAssembler.Assemble(pages);

        Assert.Equal(AssemblyStatus.Complete, result.Status);
        Assert.Equal([1, 2, 3], result.Records.Select(record => record.Id).ToArray());
        Assert.Equal(1, result.DuplicateCount);
    }

    [Fact]
    public void Assemble_ConflictingPayloadsForTheSameId_Fails()
    {
        var pages = new[]
        {
            Page(1, totalPages: 2, totalRecords: 1, Record(7, name: "First")),
            Page(2, totalPages: 2, totalRecords: 1, Record(7, name: "Second"))
        };

        var result = CatalogAssembler.Assemble(pages);

        Assert.Equal(AssemblyStatus.Conflict, result.Status);
        Assert.Contains("7", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Assemble_ShortCatalog_IsIncomplete()
    {
        var result = CatalogAssembler.Assemble([Page(1, totalPages: 1, totalRecords: 2, Record(1))]);

        Assert.Equal(AssemblyStatus.Incomplete, result.Status);
        Assert.Equal(1, Assert.Single(result.Records).Id);
    }

    [Fact]
    public void Assemble_MoreDistinctIdsThanReported_IsInconsistent()
    {
        var result = CatalogAssembler.Assemble(
            [Page(1, totalPages: 1, totalRecords: 1, Record(1), Record(2))]);

        Assert.Equal(AssemblyStatus.Inconsistent, result.Status);
    }

    [Fact]
    public void Assemble_DisagreeingTotals_IsInconsistent()
    {
        var pages = new[]
        {
            Page(1, totalPages: 2, totalRecords: 2, Record(1)),
            Page(2, totalPages: 3, totalRecords: 2, Record(2))
        };

        var result = CatalogAssembler.Assemble(pages);

        Assert.Equal(AssemblyStatus.Inconsistent, result.Status);
        Assert.Contains("totalPages", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Assemble_ReportedPageDoesNotMatchRequest_IsInconsistent()
    {
        var page = Page(1, totalPages: 1, totalRecords: 1, Record(1)) with { ReportedPage = 4 };

        var result = CatalogAssembler.Assemble([page]);

        Assert.Equal(AssemblyStatus.Inconsistent, result.Status);
        Assert.Contains("reported page 4", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Assemble_EmptyCatalog_IsComplete()
    {
        var result = CatalogAssembler.Assemble([Page(1, totalPages: 1, totalRecords: 0)]);

        Assert.Equal(AssemblyStatus.Complete, result.Status);
        Assert.Empty(result.Records);
    }

    private static InventoryPage Page(int page, int totalPages, int totalRecords, params InventoryRecord[] records) =>
        new(page, page, 25, totalPages, totalRecords, records);

    private static InventoryRecord Record(int id, string name = "Item") =>
        new(id, name, "Electronics", 1m, 1, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
}
