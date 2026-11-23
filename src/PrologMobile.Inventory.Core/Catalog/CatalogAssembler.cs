namespace PrologMobile.Inventory.Core.Catalog;

public static class CatalogAssembler
{
    public static AssemblyResult Assemble(IReadOnlyList<InventoryPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count == 0)
            return AssemblyResult.Inconsistent("No inventory pages were collected.");

        var expectedRecords = pages[0].TotalRecords;
        var expectedPages = pages[0].TotalPages;

        if (expectedRecords < 0 || expectedPages < 0)
            return AssemblyResult.Inconsistent("The first page reported a negative totalRecords or totalPages.");

        foreach (var page in pages)
        {
            if (page.ReportedPage != page.RequestedPage)
            {
                return AssemblyResult.Inconsistent(
                    $"Requested page {page.RequestedPage}, but the API reported page {page.ReportedPage}.");
            }

            if (page.TotalRecords != expectedRecords || page.TotalPages != expectedPages)
            {
                return AssemblyResult.Inconsistent(
                    $"Page {page.ReportedPage} reported totalRecords {page.TotalRecords} and totalPages {page.TotalPages}, " +
                    $"which does not match the first page ({expectedRecords} records across {expectedPages} pages).");
            }
        }

        if (expectedPages < 1 && expectedRecords > 0)
        {
            return AssemblyResult.Inconsistent(
                $"The API reported totalPages {expectedPages} while totalRecords is {expectedRecords}.");
        }

        var byId = new Dictionary<int, InventoryRecord>();
        var duplicates = 0;

        foreach (var record in pages.SelectMany(page => page.Records))
        {
            if (!byId.TryGetValue(record.Id, out var existing))
            {
                byId.Add(record.Id, record);
                continue;
            }

            duplicates++;
            if (existing != record)
                return AssemblyResult.Conflict(record.Id, duplicates);
        }

        var records = byId.Values.OrderBy(record => record.Id).ToArray();

        if (records.Length == expectedRecords)
            return AssemblyResult.Complete(records, expectedRecords, duplicates);

        if (records.Length < expectedRecords)
            return AssemblyResult.Incomplete(records, expectedRecords, duplicates);

        return AssemblyResult.Inconsistent(
            $"Collected {records.Length} distinct records, which is more than the reported totalRecords of {expectedRecords}.");
    }
}
