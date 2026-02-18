using OCRServer.Security;

namespace OCRServer.Middleware;

/// <summary>
/// OCR endpoint API key authentication.
/// Missing key -> 401. Invalid key -> 403.
/// Attaches resolved client info to HttpContext.Items.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    private const string HeaderName = "X-API-Key";

    private readonly RequestDelegate _next;
    private readonly ApiKeyStore _store;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, ApiKeyStore store, ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _store = store;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!OcrEndpointMatcher.IsOcrRequest(context))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var keyValues) || string.IsNullOrWhiteSpace(keyValues.FirstOrDefault()))
        {
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "missing_api_key";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = $"Missing {HeaderName} header" });
            return;
        }

        var presentedKey = keyValues.First()!;
        if (!_store.TryResolve(presentedKey, out var client))
        {
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "invalid_api_key";
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        context.Items[HttpContextItemKeys.OcrClient] = client;
        _logger.LogDebug("OCR auth ok: client={Client}", client.Name);

        await _next(context);
    }
}

internal static class OcrEndpointMatcher
{
    internal static bool IsOcrRequest(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
            return false;

        // Controller route: /api/Ocr
        return context.Request.Path.Equals("/api/Ocr", StringComparison.OrdinalIgnoreCase);
    }
}

