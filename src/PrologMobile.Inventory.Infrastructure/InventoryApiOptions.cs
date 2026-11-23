namespace PrologMobile.Inventory.Infrastructure;

public sealed class InventoryApiOptions
{
    public const string SectionName = "InventoryApi";

    public string BaseUrl { get; set; } = "";

    public string ApiKey { get; set; } = "";

    public int PageSize { get; set; } = 25;

    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    public bool UseJitter { get; set; } = true;

    public int AttemptTimeoutMilliseconds { get; set; } = 15_000;

    public int ExtraPageCap { get; set; } = 3;

    public int MaxPages { get; set; } = 50;

    public Uri CreateBaseAddress()
    {
        var text = BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/";
        return new Uri(text, UriKind.Absolute);
    }
}
