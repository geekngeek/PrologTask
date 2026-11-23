using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace PrologMobile.Inventory.Infrastructure;

public static class InventoryResilience
{
    public static ResiliencePipeline<HttpResponseMessage> Create(InventoryApiOptions options, ILogger logger)
    {
        var retry = new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(options.RetryBaseDelayMilliseconds),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = options.UseJitter,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TimeoutRejectedException>()
                .HandleResult(static response => IsTransient(response.StatusCode)),
            DelayGenerator = static arguments =>
            {
                var retryAfter = arguments.Outcome.Result?.Headers.RetryAfter;
                if (retryAfter?.Delta is TimeSpan delta)
                    return new ValueTask<TimeSpan?>(delta < TimeSpan.Zero ? TimeSpan.Zero : delta);

                if (retryAfter?.Date is DateTimeOffset date)
                {
                    var delay = date - DateTimeOffset.UtcNow;
                    return new ValueTask<TimeSpan?>(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
                }

                return new ValueTask<TimeSpan?>((TimeSpan?)null);
            },
            OnRetry = arguments =>
            {
                var reason = Describe(arguments);
                arguments.Outcome.Result?.Dispose();
                logger.LogWarning(
                    arguments.Outcome.Exception,
                    "Retrying inventory request after {Reason}. Next attempt waits {Delay}.",
                    reason,
                    arguments.RetryDelay);
                return default;
            }
        };

        // Retry is the outer strategy, so each attempt gets its own timeout.
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(retry)
            .AddTimeout(TimeSpan.FromMilliseconds(options.AttemptTimeoutMilliseconds))
            .Build();
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static string Describe(OnRetryArguments<HttpResponseMessage> arguments)
    {
        if (arguments.Outcome.Result is HttpResponseMessage response)
            return ((int)response.StatusCode).ToString();

        return arguments.Outcome.Exception?.GetType().Name ?? "unknown failure";
    }
}
