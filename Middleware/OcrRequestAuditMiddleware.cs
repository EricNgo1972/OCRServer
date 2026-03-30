using System.Diagnostics;
using OCRServer.Security;
using OCRServer.Services;

namespace OCRServer.Middleware;

/// <summary>
/// Logs one line per OCR request with client, size, pages, duration, and result.
/// </summary>
public sealed class OcrRequestAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly OcrDashboardMetrics _metrics;
    private readonly ILogger<OcrRequestAuditMiddleware> _logger;

    public OcrRequestAuditMiddleware(
        RequestDelegate next,
        OcrDashboardMetrics metrics,
        ILogger<OcrRequestAuditMiddleware> logger)
    {
        _next = next;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!OcrEndpointMatcher.IsOcrRequest(context))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        _metrics.RecordRequestSubmitted();

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();

            var client = context.Items.TryGetValue(HttpContextItemKeys.OcrClient, out var v) ? v as ApiKeyClient : null;
            var clientName = client?.Name ?? "unknown";

            var sizeBytes = context.Items.TryGetValue(HttpContextItemKeys.OcrFileSizeBytes, out var s) && s is long l ? l : (long?)null;
            var pages = context.Items.TryGetValue(HttpContextItemKeys.OcrPageCount, out var p) && p is int i ? i : (int?)null;

            var status = context.Response?.StatusCode ?? 0;
            var result = status switch
            {
                StatusCodes.Status200OK => "ok",
                StatusCodes.Status401Unauthorized => "unauthorized",
                StatusCodes.Status403Forbidden => "forbidden",
                StatusCodes.Status413PayloadTooLarge => "too_large",
                StatusCodes.Status429TooManyRequests => "rejected",
                _ => status >= 400 ? "fail" : "ok"
            };

            var reason = context.Items.TryGetValue(HttpContextItemKeys.OcrRejectedReason, out var r) ? r as string : null;
            var isSuccess = status == StatusCodes.Status200OK;
            var hasDocument = sizeBytes.HasValue && sizeBytes.Value > 0;

            _metrics.RecordRequestOutcome(isSuccess, hasDocument, pages);

            _logger.LogInformation(
                "OCR | client={Client} | pages={Pages} | size={SizeMb}MB | ms={Ms} | status={Status} | {Result}{Reason}",
                clientName,
                pages?.ToString() ?? "?",
                sizeBytes.HasValue ? Math.Round(sizeBytes.Value / 1024d / 1024d, 2) : -1,
                sw.ElapsedMilliseconds,
                status,
                result,
                string.IsNullOrWhiteSpace(reason) ? "" : $" | reason={reason}");
        }
    }
}

