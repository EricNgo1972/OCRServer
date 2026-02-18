using OCRServer.Security;

namespace OCRServer.Middleware;

/// <summary>
/// Per-client concurrency limiter for OCR.
/// Rejects immediately with 429 when concurrency is exceeded (no queuing).
/// </summary>
public sealed class OcrConcurrencyLimiterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OcrConcurrencyLimiterMiddleware> _logger;

    public OcrConcurrencyLimiterMiddleware(RequestDelegate next, ILogger<OcrConcurrencyLimiterMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!OcrEndpointMatcher.IsOcrRequest(context))
        {
            await _next(context);
            return;
        }

        if (!context.Items.TryGetValue(HttpContextItemKeys.OcrClient, out var v) || v is not ApiKeyClient client)
        {
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "missing_client_context";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Client context missing (API key middleware not executed)." });
            return;
        }

        // CRITICAL: do not queue; reject immediately if no slot.
        if (!client.Concurrency.Wait(0))
        {
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "concurrency_limit";
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Too many concurrent OCR requests for this API key." });
            return;
        }

        try
        {
            _logger.LogDebug("OCR concurrency acquired: client={Client}", client.Name);
            await _next(context);
        }
        finally
        {
            client.Concurrency.Release();
        }
    }
}

