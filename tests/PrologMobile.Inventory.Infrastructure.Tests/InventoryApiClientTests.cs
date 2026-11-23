using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrologMobile.Inventory.Core.Catalog;
using PrologMobile.Inventory.Infrastructure;

namespace PrologMobile.Inventory.Infrastructure.Tests;

public class InventoryApiClientTests
{
    [Fact]
    public async Task GetAll_WalksDeclaredPages_DedupesOverlap_AndSendsTheApiKey()
    {
        var handler = new RecordingHandler((request, _, _) =>
        {
            var page = PageOf(request);
            var json = page switch
            {
                1 => PageJson(1, reportedPageSize: 2, totalPages: 2, totalRecords: 3, Record(1), Record(2)),
                2 => PageJson(2, reportedPageSize: 2, totalPages: 2, totalRecords: 3, Record(2), Record(3)),
                _ => throw new InvalidOperationException($"Unexpected page {page}.")
            };

            Assert.Equal("test-key", request.Headers.GetValues("X-Api-Key").Single());
            Assert.Contains("pageSize=2", request.RequestUri!.Query, StringComparison.Ordinal);
            return Task.FromResult(Json(HttpStatusCode.OK, json));
        });

        var records = await Client(handler, pageSize: 2).GetAllAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3], records.Select(record => record.Id).ToArray());
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetAll_RetriesTransient500_ThenReadsTheSuccessfulPage()
    {
        var handler = new RecordingHandler((_, call, _) =>
        {
            if (call < 3)
                return Task.FromResult(Json(HttpStatusCode.InternalServerError, """{"detail":"boom"}"""));

            return Task.FromResult(Json(HttpStatusCode.OK, PageJson(1, 25, 1, 1, Record(8))));
        });

        var records = await Client(handler, maxRetryAttempts: 2).GetAllAsync(CancellationToken.None);

        Assert.Equal(8, Assert.Single(records).Id);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task GetAll_Retries429_UsingRetryAfter()
    {
        var handler = new RecordingHandler((_, call, _) =>
        {
            if (call == 1)
            {
                var response = Json(HttpStatusCode.TooManyRequests, "");
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }

            return Task.FromResult(Json(HttpStatusCode.OK, PageJson(1, 25, 1, 0)));
        });

        var records = await Client(handler, maxRetryAttempts: 1).GetAllAsync(CancellationToken.None);

        Assert.Empty(records);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, InventoryFailureKind.RequestRejected)]
    [InlineData(HttpStatusCode.Unauthorized, InventoryFailureKind.CredentialsRejected)]
    [InlineData(HttpStatusCode.NotFound, InventoryFailureKind.Unavailable)]
    public async Task GetAll_DoesNotRetryNonTransientStatus(HttpStatusCode statusCode, InventoryFailureKind kind)
    {
        var handler = new RecordingHandler((_, _, _) =>
            Task.FromResult(Json(statusCode, """{"detail":"rejected"}""")));

        var exception = await Assert.ThrowsAsync<InventoryCatalogException>(
            () => Client(handler, maxRetryAttempts: 3).GetAllAsync(CancellationToken.None));

        Assert.Equal(kind, exception.Kind);
        Assert.Contains("rejected", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAll_TransportFailure_IsWrappedAfterTheAttemptsAreUsed()
    {
        var handler = new RecordingHandler((_, _, _) => throw new HttpRequestException("connection reset"));

        var exception = await Assert.ThrowsAsync<InventoryCatalogException>(
            () => Client(handler, maxRetryAttempts: 2).GetAllAsync(CancellationToken.None));

        Assert.Equal(InventoryFailureKind.Unavailable, exception.Kind);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task GetAll_ShortDeclaredPages_FetchesPastTotalPagesUntilComplete()
    {
        var handler = new RecordingHandler((request, _, _) =>
        {
            var page = PageOf(request);
            var json = page switch
            {
                1 => PageJson(1, 25, totalPages: 1, totalRecords: 2, Record(1)),
                2 => PageJson(2, 25, totalPages: 1, totalRecords: 2, Record(2)),
                _ => throw new InvalidOperationException($"Unexpected page {page}.")
            };
            return Task.FromResult(Json(HttpStatusCode.OK, json));
        });

        var records = await Client(handler).GetAllAsync(CancellationToken.None);

        Assert.Equal([1, 2], records.Select(record => record.Id).ToArray());
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetAll_ConflictingDeclaredPages_DoesNotFetchBeyondTotalPages()
    {
        var handler = new RecordingHandler((request, _, _) =>
        {
            var page = PageOf(request);
            var json = page switch
            {
                1 => PageJson(1, 25, 2, 1, Record(4, "Alpha")),
                2 => PageJson(2, 25, 2, 1, Record(4, "Beta")),
                _ => throw new InvalidOperationException($"Unexpected page {page}.")
            };
            return Task.FromResult(Json(HttpStatusCode.OK, json));
        });

        var exception = await Assert.ThrowsAsync<InventoryCatalogException>(
            () => Client(handler).GetAllAsync(CancellationToken.None));

        Assert.Equal(InventoryFailureKind.ConflictingRecords, exception.Kind);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetAll_AttemptTimeout_RetriesThenFails()
    {
        var handler = new RecordingHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return Json(HttpStatusCode.OK, PageJson(1, 25, 1, 0));
        });

        var exception = await Assert.ThrowsAsync<InventoryCatalogException>(
            () => Client(handler, maxRetryAttempts: 1, attemptTimeoutMilliseconds: 150).GetAllAsync(CancellationToken.None));

        Assert.Equal(InventoryFailureKind.Unavailable, exception.Kind);
        Assert.IsType<Polly.Timeout.TimeoutRejectedException>(exception.InnerException);
        Assert.Equal(2, handler.Calls);
    }

    private static InventoryApiClient Client(
        RecordingHandler handler,
        int pageSize = 25,
        int maxRetryAttempts = 3,
        int attemptTimeoutMilliseconds = 5_000)
    {
        var options = new InventoryApiOptions
        {
            BaseUrl = "http://inventory.test/",
            ApiKey = "test-key",
            PageSize = pageSize,
            MaxRetryAttempts = maxRetryAttempts,
            RetryBaseDelayMilliseconds = 0,
            UseJitter = false,
            AttemptTimeoutMilliseconds = attemptTimeoutMilliseconds,
            ExtraPageCap = 3,
            MaxPages = 50
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.CreateBaseAddress(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        return new InventoryApiClient(
            httpClient,
            Options.Create(options),
            InventoryResilience.Create(options, NullLogger<InventoryApiClient>.Instance),
            NullLogger<InventoryApiClient>.Instance);
    }

    private static int PageOf(HttpRequestMessage request)
    {
        var page = request.RequestUri!.Query
            .TrimStart('?')
            .Split('&')
            .Single(part => part.StartsWith("page=", StringComparison.Ordinal));
        return int.Parse(page["page=".Length..], CultureInfo.InvariantCulture);
    }

    private static InventoryRecord Record(int id, string name = "Item") =>
        new(id, name, "Electronics", 1.5m, 2, new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));

    private static string PageJson(
        int page,
        int reportedPageSize,
        int totalPages,
        int totalRecords,
        params InventoryRecord[] records)
    {
        var payload = new
        {
            records = records.Select(record => new
            {
                id = record.Id,
                name = record.Name,
                category = record.Category,
                price = record.Price,
                quantity = record.Quantity,
                lastUpdated = "2026-06-01T08:00:00Z"
            }),
            page,
            pageSize = reportedPageSize,
            totalPages,
            totalRecords
        };

        return JsonSerializer.Serialize(payload);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _respond;

        public RecordingHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond) =>
            _respond = respond;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = ++Calls;
            return _respond(request, call, cancellationToken);
        }
    }
}
