# Inventory category summary

A small ASP.NET Core service that reads the PrologMobile inventory API, checks that the catalog it collected is complete, and returns one aggregate per category.

`GET /api/categories`

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0 or newer). .NET 8 is no longer in support, so this solution targets `net10.0`. `global.json` allows any 10.x SDK.

## Configure the API key

The key is not in source control. `appsettings.json` has the base URL and paging settings only.

For local development, store the key in user secrets (loaded when `ASPNETCORE_ENVIRONMENT` is `Development`, which the launch profile sets):

```powershell
dotnet user-secrets set "InventoryApi:ApiKey" "<key from the assignment email>" --project src/PrologMobile.Inventory.Api
```

Anywhere else, set the environment variable `InventoryApi__ApiKey`. The process refuses to start when the key is missing.

## Run

```powershell
dotnet run --project src/PrologMobile.Inventory.Api
```

The app listens on `http://localhost:5080`.

```powershell
curl http://localhost:5080/api/categories
```

A successful body has this shape (the numbers below are illustrative):

```json
{
  "categories": [
    {
      "category": "Electronics",
      "totalInventoryValue": 1799.88,
      "availableRecordCount": 1,
      "mostRecentlyUpdated": {
        "id": 1001,
        "name": "Thermal Receipt Printer",
        "lastUpdated": "2026-10-15T12:30:00Z"
      },
      "items": [
        {
          "id": 1001,
          "name": "Thermal Receipt Printer",
          "price": 149.99,
          "quantity": 12,
          "lastUpdated": "2026-10-15T12:30:00Z"
        }
      ]
    }
  ]
}
```

Money is written with two decimal places. Timestamps are UTC. When the upstream API cannot produce a complete catalog, the response is ProblemDetails (`502` for a bad or inconsistent catalog, `503` when the API is unreachable, rate-limited past the retries, or timing out). The API key is not written to the logs. `System.Net.Http.HttpClient` logging is set to `Warning` so request-header traces do not pick the key up either.

## Tests

```powershell
dotnet test
```

`PrologMobile.Inventory.Core.Tests` covers aggregation and the catalog completeness rules. `PrologMobile.Inventory.Infrastructure.Tests` covers paging, deduplication, retries, non-retryable status codes, and the per-attempt timeout. Those tests use a stub `HttpMessageHandler`. They do not call the staging API.

## Architecture

The solution is three projects, split along the dependency that the brief calls out: aggregation does not know that HTTP exists.

- **Core** holds the record model, `CatalogAssembler`, and `CategoryAggregator`. Both are pure functions over in-memory pages and records. `IInventoryCatalog` is the port the aggregator side depends on. `CategorySummaryService` is the application service registered in DI: load the catalog, then aggregate it.
- **Infrastructure** implements the port with a typed `HttpClient` from `IHttpClientFactory`. It pages, retries, and turns transport failures and non-success status codes into `InventoryCatalogException`. It does not decide category totals.
- **Api** is the composition root. It maps `GET /api/categories` and converts catalog failures into ProblemDetails so an exception does not reach the caller raw.

The interesting rules live in `CatalogAssembler`, where they can be tested without a socket. The client fetches every page the first response declares, asks the assembler whether the distinct ids match `totalRecords`, and only then reads a few pages past `totalPages` if the set is short. Identical copies of an id are dropped. Two different payloads for one id, disagreeing page metadata, or a count that still does not match `totalRecords` fail the request. A partial category list is not returned as success.

Retries are a Polly pipeline, registered once and wrapped around each page call:

- Retried: `408`, `429`, `500`, `502`, `503`, `504`, `HttpRequestException`, and the attempt timeout.
- Not retried: `400`, `401`, `404`, and any other status. Those become a catalog exception immediately.
- Up to 3 extra attempts, exponential backoff from 200ms with jitter. A `Retry-After` header replaces that delay when the API sends one.
- The timeout sits inside the retry, so each attempt has 15 seconds of its own. `HttpClient.Timeout` is `InfiniteTimeSpan`; otherwise the client timer would cancel the whole retry loop.

## Decisions

- **In-stock means `quantity > 0`.** The brief excludes zero. A negative quantity is not available stock either, so it is excluded too. None appeared in the staging data.
- **Categories are grouped by the trimmed label, case-insensitively, with internal whitespace collapsed.** Staging returns the same six categories under several casings and with stray spaces (`Electronics`, `Electronics `, `  electronics `, `OFFICE SUPPLIES`, `outdoor`). Grouping on the raw string would split one category's value. The name in the response is the most common collapsed spelling in the group. A tie uses ordinal order, so the result is stable. `Discontinued` stays when it still has quantity. On the current staging catalog every Discontinued record has quantity 0, so that category is absent and the other five categories contain all 136 in-stock records.
- **`totalInventoryValue` is `decimal`.** It is the sum of `price * quantity` with no extra rounding.
- **The most recently updated record is the greatest `lastUpdated`, then the greater `id`.** No real timestamp tie showed up after duplicates were removed. The tie-break is there so the choice does not depend on page order.
- **Categories are ordered by name, items by id.**
- **Each request reads the catalog again.** The dataset is static, but a cache would hide the upstream changes this exercise is about.
- **Pages are fetched one at a time, at `pageSize` 25.** A short burst against staging returned `429`. Seven pages do not need parallelism.
- **The client does not special-case the overlap I saw.** At `pageSize=25`, pages 2, 4, and 6 each returned 26 rows and repeated the previous page's last id (`1025`, `1075`, `1125`). The copies were identical. Distinct ids were 157, matching `totalRecords`. Pages past the end return `200` with an empty `records` array. The assembler checks distinct ids, metadata agreement, and payload conflicts, which still holds if that overlap moves.

## Known limitations

- If `totalRecords` is still short after `ExtraPageCap` extra pages (default 3), the request fails with `502`. A hole further out than that is reported, not papered over.
- `totalPages` above `MaxPages` (default 50) fails fast so a bad page count cannot crawl forever.
- Our endpoint has no authentication. It is meant to run locally for this assignment.
- The automated tests do not hit staging. A `429` or a future `500` would make that suite flaky, and CI would need the key.
- A negative price with a positive quantity would be included in the sum. I did not see one.
- The Polly pipeline is built from options the first time it is used and then reused. Changing retry settings means restarting the process.

With more time I would add a concurrency cap of two on page fetches, counters for retries and dropped duplicates, and an optional smoke test against staging gated by an environment variable so the default `dotnet test` stays offline.
