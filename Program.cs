using Microsoft.OpenApi.Models;
using OCRServer.Middleware;
using OCRServer.Ocr;
using OCRServer.Processing;
using OCRServer.Security;
using OCRServer.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OCR Server API",
        Version = "v1"
    });

    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "OCR API key required in the X-API-Key header.",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddSingleton<ApiKeyStore>();
builder.Services.AddSingleton<PdfInfoPageCounter>();
builder.Services.AddSingleton<OcrDashboardMetrics>();

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
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
    });
});

if (OperatingSystem.IsLinux())
{
    builder.Services.AddSingleton<PdfTextExtractor>();
    builder.Services.AddSingleton<IOcrService, LinuxOcrService>();
    builder.Services.AddSingleton<PdftoppmPdfRenderer>();
}
else
{
    builder.Services.AddSingleton<IOcrService, WindowsOcrService>();
    builder.Services.AddSingleton<IPdfRenderer, PdfiumPdfRenderer>();
    builder.Services.AddSingleton<ImagePreprocessor>();
    builder.Services.AddSingleton<DeskewHelper>();
}

builder.Services.AddSingleton<TesseractRunner>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "OCR Server Swagger";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "OCR Server API v1");
});

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

app.MapGet("/", () => Results.Redirect("/dashboard"));

app.MapGet("/dashboard/stats", (OcrDashboardMetrics metrics) => Results.Ok(metrics.GetSnapshot()));

app.MapGet("/dashboard", () =>
{
    var html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>OCR Dashboard</title>
<style>
    :root {
        --bg: #08111f;
        --panel: rgba(12, 22, 38, 0.84);
        --panel-border: rgba(120, 164, 210, 0.14);
        --accent: #ff7a45;
        --accent-2: #5fd0c4;
        --text: #edf4ff;
        --muted: #93a4b8;
        --shadow: 0 24px 70px rgba(0, 0, 0, 0.38);
    }

    * {
        box-sizing: border-box;
    }

    body {
        margin: 0;
        min-height: 100vh;
        padding: 32px;
        font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif;
        background:
            radial-gradient(circle at top left, rgba(255, 122, 69, 0.18), transparent 34%),
            radial-gradient(circle at top right, rgba(95, 208, 196, 0.15), transparent 28%),
            linear-gradient(180deg, #0b1628 0%, #050b14 100%);
        color: var(--text);
    }

    code {
        font-family: Consolas, "Courier New", monospace;
    }

    .shell {
        max-width: 1120px;
        margin: 0 auto;
    }

    .hero {
        display: grid;
        gap: 18px;
        grid-template-columns: 1.4fr 0.9fr;
        align-items: stretch;
        margin-bottom: 24px;
    }

    .panel {
        background: var(--panel);
        border: 1px solid var(--panel-border);
        border-radius: 24px;
        padding: 24px;
        box-shadow: var(--shadow);
        backdrop-filter: blur(10px);
    }

    .hero-copy h1 {
        margin: 0 0 12px;
        font-size: clamp(2.2rem, 5vw, 4.4rem);
        line-height: 0.95;
        letter-spacing: -0.04em;
    }

    .hero-copy p {
        margin: 0;
        max-width: 56ch;
        font-size: 1.03rem;
        line-height: 1.6;
        color: var(--muted);
    }

    .hero-badges {
        display: flex;
        gap: 10px;
        flex-wrap: wrap;
        margin-top: 20px;
    }

    .badge {
        display: inline-flex;
        align-items: center;
        gap: 8px;
        padding: 8px 12px;
        border-radius: 999px;
        background: rgba(255, 255, 255, 0.06);
        border: 1px solid rgba(120, 164, 210, 0.14);
        color: var(--text);
        font-size: 0.92rem;
    }

    .hero-links {
        display: grid;
        gap: 12px;
        align-content: start;
    }

    .action {
        display: block;
        padding: 16px 18px;
        border-radius: 18px;
        text-decoration: none;
        color: var(--text);
        background: rgba(255, 255, 255, 0.04);
        border: 1px solid rgba(120, 164, 210, 0.14);
        transition: transform 140ms ease, border-color 140ms ease;
    }

    .action:hover {
        transform: translateY(-1px);
        border-color: rgba(255, 122, 69, 0.42);
    }

    .action strong {
        display: block;
        margin-bottom: 6px;
        font-size: 1rem;
    }

    .action span {
        color: var(--muted);
        font-size: 0.94rem;
        line-height: 1.45;
    }

    .metrics {
        display: grid;
        gap: 16px;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        margin-bottom: 24px;
    }

    .metric-card {
        position: relative;
        overflow: hidden;
    }

    .metric-card::after {
        content: "";
        position: absolute;
        inset: auto -20px -30px auto;
        width: 120px;
        height: 120px;
        border-radius: 50%;
        background: radial-gradient(circle, rgba(255, 122, 69, 0.16), transparent 70%);
    }

    .metric-label {
        display: block;
        margin-bottom: 10px;
        color: var(--muted);
        font-size: 0.9rem;
        text-transform: uppercase;
        letter-spacing: 0.08em;
    }

    .metric-value {
        font-size: clamp(2rem, 4vw, 3rem);
        font-weight: 700;
        line-height: 1;
        letter-spacing: -0.04em;
    }

    .metric-note {
        margin-top: 10px;
        color: var(--muted);
        font-size: 0.92rem;
    }

    .grid {
        display: grid;
        gap: 18px;
        grid-template-columns: 1.05fr 0.95fr;
    }

    h2 {
        margin: 0 0 16px;
        font-size: 1.15rem;
        color: var(--accent);
    }

    .list {
        margin: 0;
        padding-left: 18px;
        color: var(--muted);
        line-height: 1.7;
    }

    .status-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 12px;
        flex-wrap: wrap;
        margin-top: 14px;
        color: var(--muted);
        font-size: 0.94rem;
    }

    .live {
        color: var(--accent-2);
        font-weight: 700;
    }

    @media (max-width: 860px) {
        body {
            padding: 18px;
        }

        .hero,
        .grid {
            grid-template-columns: 1fr;
        }

        .panel {
            border-radius: 20px;
            padding: 20px;
        }
    }
</style>
</head>
<body>
<div class="shell">
    <section class="hero">
        <div class="panel hero-copy">
            <h1>OCR Dashboard</h1>
            <p>Operational totals for the OCR service. The counters below are cumulative for the current app process and update from live request traffic.</p>
            <div class="hero-badges">
                <span class="badge">Public dashboard</span>
                <span class="badge">API docs at /swagger</span>
                <span class="badge">OCR endpoint: /api/ocr</span>
            </div>
        </div>
        <div class="hero-links">
            <a class="panel action" href="/swagger">
                <strong>Open Swagger</strong>
                <span>Inspect the OCR contract and send test requests with the <code>X-API-Key</code> header.</span>
            </a>
            <div class="panel action">
                <strong>Current defaults</strong>
                <span><code>language</code> defaults to <code>eng+fra</code>. OCR accepts PDF, PNG, and JPG uploads via multipart form-data.</span>
            </div>
        </div>
    </section>

    <section class="metrics">
        <article class="panel metric-card">
            <span class="metric-label">Requests Submitted</span>
            <div class="metric-value" id="requestsSubmitted">0</div>
            <div class="metric-note">Every OCR request attempt received by the service.</div>
        </article>
        <article class="panel metric-card">
            <span class="metric-label">Documents Processed</span>
            <div class="metric-value" id="documentsProcessed">0</div>
            <div class="metric-note">Requests that contained a readable, non-empty uploaded file.</div>
        </article>
        <article class="panel metric-card">
            <span class="metric-label">Successful Requests</span>
            <div class="metric-value" id="successfulRequests">0</div>
            <div class="metric-note">OCR requests completed with HTTP 200.</div>
        </article>
        <article class="panel metric-card">
            <span class="metric-label">Failed Requests</span>
            <div class="metric-value" id="failedRequests">0</div>
            <div class="metric-note">All rejected or failed OCR requests.</div>
        </article>
        <article class="panel metric-card">
            <span class="metric-label">Pages Read</span>
            <div class="metric-value" id="pagesRead">0</div>
            <div class="metric-note">Pages counted during OCR request validation.</div>
        </article>
    </section>

    <section class="grid">
        <article class="panel">
            <h2>Request Shape</h2>
            <ul class="list">
                <li><code>POST /api/ocr</code> with <code>multipart/form-data</code></li>
                <li>Header: <code>X-API-Key: &lt;your-api-key&gt;</code></li>
                <li>Form field: <code>file</code> is required</li>
                <li>Form field: <code>language</code> is optional and defaults to <code>eng+fra</code></li>
                <li>Form field: <code>profile</code> is optional</li>
            </ul>
        </article>
        <article class="panel">
            <h2>Live Status</h2>
            <ul class="list">
                <li>Dashboard path: <code>/dashboard</code></li>
                <li>Swagger path: <code>/swagger</code></li>
                <li>Stats JSON: <code>/dashboard/stats</code></li>
            </ul>
            <div class="status-row">
                <span class="live" id="status">Loading live metrics...</span>
                <span id="updatedAt">Waiting for first refresh</span>
            </div>
        </article>
    </section>
</div>

<script>
const ids = {
    totalRequestsSubmitted: "requestsSubmitted",
    totalDocumentsProcessed: "documentsProcessed",
    totalSuccessfulRequests: "successfulRequests",
    totalFailedRequests: "failedRequests",
    totalPagesRead: "pagesRead"
};

async function refreshStats() {
    const statusEl = document.getElementById("status");
    const updatedEl = document.getElementById("updatedAt");

    try {
        const response = await fetch("/dashboard/stats", { cache: "no-store" });
        if (!response.ok) {
            throw new Error("HTTP " + response.status);
        }

        const data = await response.json();
        for (const [key, id] of Object.entries(ids)) {
            const value = typeof data[key] === "number" ? data[key] : 0;
            document.getElementById(id).textContent = new Intl.NumberFormat().format(value);
        }

        statusEl.textContent = "Live metrics online";
        updatedEl.textContent = "Updated " + new Date().toLocaleTimeString();
    } catch (error) {
        statusEl.textContent = "Metrics unavailable";
        updatedEl.textContent = String(error);
    }
}

refreshStats();
setInterval(refreshStats, 10000);
</script>
</body>
</html>
""";

    return Results.Text(html, "text/html");
});

app.Run();
