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

//// Root path = health check + usage instructions (no API key required)
//app.MapGet("/", () =>
//{
//    var instructions = """
//        OCR service is running.

//        How to use
//        ----------
//        POST /api/ocr  (multipart/form-data)

//        Headers:
//          X-API-Key: <your-api-key>   (required)

//        Form fields:
//          file      (required)  PDF, PNG, or JPG file
//          language  (required)  Tesseract code(s), e.g. eng, eng+fra, eng+vie
//          profile   (optional)  scan | photo | fast  (default: scan)

//        Example (curl):
//          curl -X POST https://localhost:5001/api/Ocr \\
//            -H "X-API-Key: your-key" \\
//            -F "file=@document.pdf" \\
//            -F "language=eng"

//        Health check: GET /  (this page)
//        """;
//    return Results.Text(instructions.Trim(), "text/plain");
//});

app.MapGet("/", () =>
{
    var html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<title>OCR Service</title>
<style>
    :root {
        --bg: #0b1220;
        --panel: #111a2e;
        --accent: #3cc8ff;
        --warn: #ff9f43;
        --text: #e6edf3;
        --muted: #9aa4b2;
        --code: #0a0f1c;
    }

    body {
        margin: 0;
        padding: 40px;
        font-family: Inter, Segoe UI, Roboto, sans-serif;
        background: linear-gradient(180deg, #0b1220, #070b14);
        color: var(--text);
    }

    h1 {
        color: var(--accent);
        margin-bottom: 8px;
    }

    h2 {
        margin-top: 40px;
        color: var(--accent);
        font-size: 1.2rem;
    }

    .status {
        margin-bottom: 24px;
        color: var(--muted);
    }

    .box {
        background: var(--panel);
        border-left: 4px solid var(--accent);
        padding: 16px 20px;
        margin: 16px 0;
        border-radius: 6px;
    }

    .warn {
        border-left-color: var(--warn);
    }

    pre {
        background: var(--code);
        padding: 14px 16px;
        border-radius: 6px;
        overflow-x: auto;
        font-size: 0.9rem;
    }

    code {
        color: #d7e3ff;
    }

    .kv {
        line-height: 1.6;
    }

    .muted {
        color: var(--muted);
    }
</style>
</head>
<body>

<h1>OCR Service</h1>
<div class="status">
    Status: <b>Running</b><br />
    Engine: <b>Tesseract OCR</b><br />
    Supported files: <b>PDF, PNG, JPG</b>
</div>

<div class="box warn">
    <b>Authentication Required</b><br />
    All OCR requests must include the HTTP header:
    <pre>X-API-Key: &lt;your-api-key&gt;</pre>
</div>

<h2>Endpoint</h2>
<pre>POST /api/ocr</pre>

<h2>Required Headers</h2>
<pre>
Content-Type: multipart/form-data
X-API-Key: &lt;your-api-key&gt;
</pre>

<h2>Form Fields</h2>
<div class="box">
    <div class="kv">
        <b>file</b> <span class="muted">(required)</span><br />
        PDF, PNG, or JPG file
        <br /><br />

        <b>language</b> <span class="muted">(required)</span><br />
        Tesseract language codes (e.g. <code>eng</code>, <code>eng+fra</code>, <code>eng+vie</code>)
        <br /><br />

        <b>profile</b> <span class="muted">(optional)</span><br />
        scan | photo | fast &nbsp; <span class="muted">(default: scan)</span>
    </div>
</div>

<h2>Example (curl)</h2>
<pre>
curl -X POST https://ocr.phoebus.asia/api/ocr \
  -H "X-API-Key: your-api-key" \
  -F "file=@document.pdf" \
  -F "language=eng"
</pre>

<h2>Health Check</h2>
<pre>GET /</pre>

<div class="muted">
    Returns this page. No API key required.
</div>

</body>
</html>
""";

    return Results.Text(html, "text/html");
});

app.Run();
