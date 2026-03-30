using OCRServer.Security;
using OCRServer.Services;

namespace OCRServer.Middleware;

/// <summary>
/// Validates OCR requests before any expensive processing:
/// - max upload size (bytes)
/// - max PDF pages
/// Returns 413 on violations.
/// </summary>
public sealed class OcrRequestValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PdfInfoPageCounter _pageCounter;
    private readonly ILogger<OcrRequestValidationMiddleware> _logger;
    private readonly long _maxUploadBytes;
    private readonly int _maxPdfPages;

    public OcrRequestValidationMiddleware(
        RequestDelegate next,
        PdfInfoPageCounter pageCounter,
        IConfiguration configuration,
        ILogger<OcrRequestValidationMiddleware> logger)
    {
        _next = next;
        _pageCounter = pageCounter;
        _logger = logger;

        _maxUploadBytes = TryParseLong(configuration["OcrLimits:MaxUploadBytes"], fallback: 20L * 1024 * 1024);
        _maxPdfPages = (int)TryParseLong(configuration["OcrLimits:MaxPdfPages"], fallback: 30);
        if (_maxUploadBytes <= 0) _maxUploadBytes = 1;
        if (_maxPdfPages <= 0) _maxPdfPages = 1;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!OcrEndpointMatcher.IsOcrRequest(context))
        {
            await _next(context);
            return;
        }

        // Best-effort early reject based on Content-Length (multipart adds overhead, so this is approximate).
        if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > _maxUploadBytes)
        {
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "max_upload_bytes";
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = $"Payload too large. Max allowed is {_maxUploadBytes} bytes." });
            return;
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse multipart form for OCR request.");
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "invalid_form";
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid multipart form data." });
            return;
        }

        var file = form.Files.GetFile("file");
        if (file == null || file.Length == 0)
        {
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "missing_file";
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "File is required and must not be empty." });
            return;
        }

        context.Items[HttpContextItemKeys.OcrFileSizeBytes] = file.Length;

        if (file.Length > _maxUploadBytes)
        {
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "max_upload_bytes";
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = $"File too large. Max allowed is {_maxUploadBytes} bytes." });
            return;
        }

        var fileName = file.FileName?.ToLowerInvariant() ?? "";
        var contentType = file.ContentType?.ToLowerInvariant() ?? "";
        var isPdf = contentType.Contains("pdf") || fileName.EndsWith(".pdf");

        if (OcrEndpointMatcher.IsSearchablePdfRequest(context) && !isPdf)
        {
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "searchable_pdf_requires_pdf";
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "The searchable PDF endpoint only accepts PDF files." });
            return;
        }

        int pages = 1;
        if (isPdf)
        {
            // Count pages before OCR work starts.
            pages = await _pageCounter.GetPageCountAsync(file, context.RequestAborted);
        }

        context.Items[HttpContextItemKeys.OcrPageCount] = pages;

        if (isPdf && pages > _maxPdfPages)
        {
            context.Items[HttpContextItemKeys.OcrRejectedReason] = "max_pdf_pages";
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = $"PDF has too many pages ({pages}). Max allowed is {_maxPdfPages}." });
            return;
        }

        await _next(context);
    }

    private static long TryParseLong(string? value, long fallback)
        => long.TryParse(value, out var v) ? v : fallback;
}

