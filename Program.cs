using OCRServer.Services;
using OCRServer.Processing;
using OCRServer.Ocr;
using OCRServer.Middleware;
using OCRServer.Security;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// API keys loaded once at startup, kept in-memory (no per-request DB lookups)
builder.Services.AddSingleton<ApiKeyStore>();

// Utilities used by validation middleware
builder.Services.AddSingleton<PdfInfoPageCounter>();

// Rate limiting (per API key client)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("ocr", httpContext =>
    {
        var client = httpContext.Items.TryGetValue(HttpContextItemKeys.OcrClient, out var v) ? v as ApiKeyClient : null;
        var clientName = client?.Name ?? "unknown";
        var rpm = client?.RequestsPerMinute ?? 1;
        if (rpm <= 0) rpm = 1;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientName,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rpm,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0, // do not queue
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
    });
});

// Register OCR services
if (OperatingSystem.IsLinux())
{
    // Linux: try pdf text layer (pdftotext) then fallback to pdftoppm -> Tesseract. No OpenCV/PDFium.
    builder.Services.AddSingleton<PdfTextExtractor>();
    builder.Services.AddSingleton<IOcrService, LinuxOcrService>();
    builder.Services.AddSingleton<PdftoppmPdfRenderer>();
}
else
{
    // Windows: keep existing pipeline (PDFium + OpenCV preprocessing + Tesseract).
    builder.Services.AddSingleton<IOcrService, WindowsOcrService>();
    builder.Services.AddSingleton<IPdfRenderer, PdfiumPdfRenderer>();
    builder.Services.AddSingleton<ImagePreprocessor>();
    builder.Services.AddSingleton<DeskewHelper>();
}

builder.Services.AddSingleton<TesseractRunner>();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseMiddleware<GlobalExceptionHandler>();
app.UseMiddleware<OcrRequestAuditMiddleware>();
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<OcrConcurrencyLimiterMiddleware>();
app.UseMiddleware<OcrRequestValidationMiddleware>();
app.UseAuthorization();
app.MapControllers();

// Root path = health check + usage instructions (no API key required)
app.MapGet("/", () =>
{
    var instructions = """
        OCR service is running.

        How to use
        ----------
        POST /api/ocr  (multipart/form-data)

        Headers:
          X-API-Key: <your-api-key>   (required)

        Form fields:
          file      (required)  PDF, PNG, or JPG file
          language  (required)  Tesseract code(s), e.g. eng, eng+fra, eng+vie
          profile   (optional)  scan | photo | fast  (default: scan)

        Example (curl):
          curl -X POST https://localhost:5001/api/Ocr \\
            -H "X-API-Key: your-key" \\
            -F "file=@document.pdf" \\
            -F "language=eng"

        Health check: GET /  (this page)
        """;
    return Results.Text(instructions.Trim(), "text/plain");
});

app.Run();
