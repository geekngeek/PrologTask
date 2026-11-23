using Microsoft.Extensions.Options;

namespace PrologMobile.Inventory.Infrastructure;

public sealed class InventoryApiOptionsValidator : IValidateOptions<InventoryApiOptions>
{
    public ValidateOptionsResult Validate(string? name, InventoryApiOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("InventoryApi:BaseUrl must be an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add(
                "InventoryApi:ApiKey is required. Set it with user secrets or the InventoryApi__ApiKey environment variable.");
        }

        if (options.PageSize is < 1 or > 25)
            failures.Add("InventoryApi:PageSize must be an integer from 1 to 25.");

        if (options.MaxRetryAttempts is < 0 or > 8)
            failures.Add("InventoryApi:MaxRetryAttempts must be from 0 to 8.");

        if (options.RetryBaseDelayMilliseconds is < 0 or > 30_000)
            failures.Add("InventoryApi:RetryBaseDelayMilliseconds must be from 0 to 30000.");

        if (options.AttemptTimeoutMilliseconds is < 100 or > 120_000)
            failures.Add("InventoryApi:AttemptTimeoutMilliseconds must be from 100 to 120000.");

        if (options.ExtraPageCap is < 0 or > 20)
            failures.Add("InventoryApi:ExtraPageCap must be from 0 to 20.");

        if (options.MaxPages is < 1 or > 500)
            failures.Add("InventoryApi:MaxPages must be from 1 to 500.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
