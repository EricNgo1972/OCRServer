using OCRServer.Services;
using OCRServer.Processing;
using OCRServer.Ocr;
using OCRServer.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register OCR services
if (OperatingSystem.IsLinux())
{
    // Linux: boring/stable pipeline (PDF -> pdftoppm -> Tesseract). No OpenCV/PDFium usage.
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
app.UseMiddleware<GlobalExceptionHandler>();
app.UseAuthorization();
app.MapControllers();

app.Run();
