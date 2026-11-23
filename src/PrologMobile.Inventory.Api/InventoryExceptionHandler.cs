using Microsoft.AspNetCore.Diagnostics;
using PrologMobile.Inventory.Core.Catalog;

namespace PrologMobile.Inventory.Api;

public sealed class InventoryExceptionHandler(ILogger<InventoryExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is InventoryCatalogException catalog)
        {
            logger.LogWarning(catalog, "Inventory catalog request failed ({Kind}).", catalog.Kind);
            var (statusCode, title) = Describe(catalog.Kind);
            await Results.Problem(title: title, detail: catalog.Message, statusCode: statusCode)
                .ExecuteAsync(httpContext);
            return true;
        }

        logger.LogError(exception, "Unhandled exception while building the category summary.");
        await Results.Problem(
                title: "The category summary could not be built.",
                detail: "The request failed before a complete summary was available.",
                statusCode: StatusCodes.Status500InternalServerError)
            .ExecuteAsync(httpContext);
        return true;
    }

    private static (int StatusCode, string Title) Describe(InventoryFailureKind kind) => kind switch
    {
        InventoryFailureKind.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Inventory API unavailable"),
        InventoryFailureKind.CredentialsRejected => (StatusCodes.Status502BadGateway, "Inventory API rejected the configured API key"),
        InventoryFailureKind.RequestRejected => (StatusCodes.Status502BadGateway, "Inventory API rejected the page request"),
        InventoryFailureKind.IncompleteCatalog => (StatusCodes.Status502BadGateway, "Inventory catalog is incomplete"),
        InventoryFailureKind.ConflictingRecords => (StatusCodes.Status502BadGateway, "Inventory catalog contains conflicting records"),
        InventoryFailureKind.InconsistentCatalog => (StatusCodes.Status502BadGateway, "Inventory catalog is inconsistent"),
        _ => (StatusCodes.Status502BadGateway, "Inventory catalog request failed")
    };
}
