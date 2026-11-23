using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using PrologMobile.Inventory.Core.Catalog;

namespace PrologMobile.Inventory.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<InventoryApiOptions>()
            .Bind(configuration.GetSection(InventoryApiOptions.SectionName));
        services.AddSingleton<IValidateOptions<InventoryApiOptions>, InventoryApiOptionsValidator>();

        services.AddSingleton(serviceProvider =>
        {
            var configured = serviceProvider.GetRequiredService<IOptions<InventoryApiOptions>>().Value;
            var logger = serviceProvider.GetRequiredService<ILogger<InventoryApiClient>>();
            return InventoryResilience.Create(configured, logger);
        });

        // IHttpClientFactory owns the handler lifetime. The client timer is disabled so the
        // Polly attempt timeout is the only deadline and a retry is not cancelled mid-loop.
        services.AddHttpClient<IInventoryCatalog, InventoryApiClient>((serviceProvider, client) =>
        {
            var configured = serviceProvider.GetRequiredService<IOptions<InventoryApiOptions>>().Value;
            client.BaseAddress = configured.CreateBaseAddress();
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        return services;
    }
}
