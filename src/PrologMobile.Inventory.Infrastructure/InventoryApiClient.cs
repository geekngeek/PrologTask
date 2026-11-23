using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using PrologMobile.Inventory.Core.Catalog;

namespace PrologMobile.Inventory.Infrastructure;

public sealed class InventoryApiClient(
    HttpClient httpClient,
    IOptions<InventoryApiOptions> options,
    ResiliencePipeline<HttpResponseMessage> pipeline,
    ILogger<InventoryApiClient> logger) : IInventoryCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly InventoryApiOptions _options = options.Value;

    public async Task<IReadOnlyList<InventoryRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        var first = await FetchPageAsync(1, cancellationToken).ConfigureAwait(false);

        if (first.TotalPages > _options.MaxPages)
        {
            throw new InventoryCatalogException(
                InventoryFailureKind.InconsistentCatalog,
                $"The API reported totalPages {first.TotalPages}, which is above the safety cap of {_options.MaxPages}.");
        }

        var pages = new List<InventoryPage> { first };
        for (var page = 2; page <= first.TotalPages; page++)
            pages.Add(await FetchPageAsync(page, cancellationToken).ConfigureAwait(false));

        var assembled = CatalogAssembler.Assemble(pages);
        if (assembled.Status == AssemblyStatus.Incomplete)
            assembled = await FetchExtraPagesAsync(pages, first.TotalPages, cancellationToken).ConfigureAwait(false);

        if (assembled.Status != AssemblyStatus.Complete)
            throw InventoryCatalogException.FromAssembly(assembled);

        logger.LogInformation(
            "Collected {Count} inventory records. Removed {Duplicates} identical duplicate copies.",
            assembled.Records.Count,
            assembled.DuplicateCount);

        return assembled.Records;
    }

    private async Task<AssemblyResult> FetchExtraPagesAsync(
        List<InventoryPage> pages,
        int reportedTotalPages,
        CancellationToken cancellationToken)
    {
        var assembled = CatalogAssembler.Assemble(pages);
        var last = Math.Min(_options.MaxPages, reportedTotalPages + _options.ExtraPageCap);

        for (var page = reportedTotalPages + 1; page <= last; page++)
        {
            var extra = await FetchPageAsync(page, cancellationToken).ConfigureAwait(false);
            pages.Add(extra);
            assembled = CatalogAssembler.Assemble(pages);

            if (assembled.Status == AssemblyStatus.Complete)
                return assembled;

            if (extra.Records.Count == 0)
                return assembled;

            if (assembled.Status is AssemblyStatus.Conflict or AssemblyStatus.Inconsistent)
                return assembled;
        }

        return assembled;
    }

    private async Task<InventoryPage> FetchPageAsync(int page, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await pipeline.ExecuteAsync(
                async token =>
                {
                    var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        $"api/records?page={page}&pageSize={_options.PageSize}");
                    request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
                    try
                    {
                        return await httpClient
                            .SendAsync(request, HttpCompletionOption.ResponseContentRead, token)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        request.Dispose();
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new InventoryCatalogException(
                InventoryFailureKind.Unavailable,
                "The inventory API could not be reached.",
                exception);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new InventoryCatalogException(
                InventoryFailureKind.Unavailable,
                "The inventory API timed out.",
                exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
                return ParsePage(page, body);

            throw new InventoryCatalogException(
                MapFailure(response.StatusCode),
                $"Inventory API returned {(int)response.StatusCode} for page {page}.{FormatDetail(body)}");
        }
    }

    private InventoryPage ParsePage(int requestedPage, string body)
    {
        InventoryPageDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<InventoryPageDto>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InventoryCatalogException(
                InventoryFailureKind.InconsistentCatalog,
                $"Page {requestedPage} was not valid inventory JSON.",
                exception);
        }

        if (dto?.Records is null)
        {
            throw new InventoryCatalogException(
                InventoryFailureKind.InconsistentCatalog,
                $"Page {requestedPage} did not contain a records array.");
        }

        var records = new List<InventoryRecord>(dto.Records.Count);
        foreach (var record in dto.Records)
        {
            if (record is null || string.IsNullOrWhiteSpace(record.Name) || string.IsNullOrWhiteSpace(record.Category))
            {
                throw new InventoryCatalogException(
                    InventoryFailureKind.InconsistentCatalog,
                    $"Page {requestedPage} contains a record without a name or category.");
            }

            records.Add(new InventoryRecord(
                record.Id,
                record.Name,
                record.Category,
                record.Price,
                record.Quantity,
                record.LastUpdated));
        }

        if (records.Count > dto.PageSize)
        {
            logger.LogWarning(
                "Inventory page {Page} contained {Count} records, above the reported page size {PageSize}.",
                requestedPage,
                records.Count,
                dto.PageSize);
        }
        else
        {
            logger.LogInformation("Fetched inventory page {Page} with {Count} records.", requestedPage, records.Count);
        }

        return new InventoryPage(requestedPage, dto.Page, dto.PageSize, dto.TotalPages, dto.TotalRecords, records);
    }

    private static InventoryFailureKind MapFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => InventoryFailureKind.RequestRejected,
        HttpStatusCode.Unauthorized => InventoryFailureKind.CredentialsRejected,
        _ => InventoryFailureKind.Unavailable
    };

    private static string FormatDetail(string body)
    {
        var detail = TryReadProblemDetail(body);
        return detail is null ? string.Empty : $" {detail}";
    }

    private static string? TryReadProblemDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String)
            {
                var text = detail.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                return text.Length <= 300 ? text : text[..300];
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private sealed class InventoryPageDto
    {
        public List<InventoryRecordDto>? Records { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalPages { get; set; }

        public int TotalRecords { get; set; }
    }

    private sealed class InventoryRecordDto
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Category { get; set; }

        public decimal Price { get; set; }

        public int Quantity { get; set; }

        public DateTimeOffset LastUpdated { get; set; }
    }
}
