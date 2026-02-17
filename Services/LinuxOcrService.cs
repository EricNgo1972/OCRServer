using OCRServer.Models;
using OCRServer.Ocr;

namespace OCRServer.Services;

/// <summary>
/// Linux OCR implementation:
/// PDF -> pdftoppm (300 DPI grayscale) -> Tesseract
/// Image -> Tesseract (no OpenCV preprocessing)
/// </summary>
public sealed class LinuxOcrService : IOcrService
{
    private readonly PdftoppmPdfRenderer _pdfRenderer;
    private readonly TesseractRunner _tesseractRunner;
    private readonly ILogger<LinuxOcrService> _logger;

    public LinuxOcrService(
        PdftoppmPdfRenderer pdfRenderer,
        TesseractRunner tesseractRunner,
        ILogger<LinuxOcrService> logger)
    {
        _pdfRenderer = pdfRenderer;
        _tesseractRunner = tesseractRunner;
        _logger = logger;
    }

    public async Task<OcrResponse> ProcessAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
            throw new NotSupportedException("LinuxOcrService can only run on Linux.");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var profile = OcrServiceHelpers.ParseProfile(request.Profile); // parsed for API compatibility; not used on Linux
            _ = profile;

            OcrServiceHelpers.ValidateLanguage(request.Language);
            request.Language = OcrServiceHelpers.NormalizeLanguage(request.Language);

            byte[] fileBytes;
            using (var stream = new MemoryStream())
            {
                await request.File.CopyToAsync(stream, cancellationToken);
                fileBytes = stream.ToArray();
            }

            var pages = new List<PageResult>();
            var confidences = new List<float>();

            string contentType = request.File.ContentType.ToLowerInvariant();
            string fileName = request.File.FileName.ToLowerInvariant();

            if (contentType.Contains("pdf") || fileName.EndsWith(".pdf"))
            {
                _logger.LogInformation("Processing PDF file (Linux pdftoppm pipeline): {FileName}", request.File.FileName);

                await using var rendered = await _pdfRenderer.RenderToGrayImagesAsync(fileBytes, dpi: 300, cancellationToken);

                // Optional parallelism: bounded to avoid saturating the box.
                var paths = rendered.PageImagePaths.ToArray();
                var results = new (string text, float confidence)[paths.Length];

                var opts = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4)
                };

                await Parallel.ForEachAsync(
                    Enumerable.Range(0, paths.Length),
                    opts,
                    (i, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var r = _tesseractRunner.PerformOcrFile(paths[i], request.Language);
                        results[i] = r;
                        return ValueTask.CompletedTask;
                    });

                for (int i = 0; i < results.Length; i++)
                {
                    pages.Add(new PageResult { Page = i + 1, Text = results[i].text });
                    confidences.Add(results[i].confidence);
                }
            }
            else if (contentType.Contains("image") ||
                     fileName.EndsWith(".png") ||
                     fileName.EndsWith(".jpg") ||
                     fileName.EndsWith(".jpeg") ||
                     fileName.EndsWith(".pgm") ||
                     fileName.EndsWith(".ppm"))
            {
                _logger.LogInformation("Processing image file (Linux direct Tesseract): {FileName}", request.File.FileName);
                cancellationToken.ThrowIfCancellationRequested();
                var result = _tesseractRunner.PerformOcr(fileBytes, request.Language);
                pages.Add(new PageResult { Page = 1, Text = result.text });
                confidences.Add(result.confidence);
            }
            else
            {
                throw new ArgumentException($"Unsupported file type: {contentType}. Supported: PDF, PNG, JPG", nameof(request));
            }

            stopwatch.Stop();

            double avgConfidence = confidences.Count > 0 ? confidences.Average() : 0.0;

            return new OcrResponse
            {
                Engine = "tesseract-5.x",
                Language = request.Language,
                Profile = request.Profile ?? "scan",
                Confidence = Math.Round(avgConfidence, 2),
                Pages = pages,
                ProcessingMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR processing failed for file: {FileName}", request.File.FileName);
            throw;
        }
    }
}

internal static class OcrServiceHelpers
{
    internal static string NormalizeLanguage(string language)
    {
        static string MapOne(string part)
        {
            var p = part.Trim().ToLowerInvariant();
            return p switch
            {
                "english" or "en" or "en-us" or "en_us" => "eng",
                "french" or "fr" or "fr-fr" or "fr_fr" => "fra",
                "vietnamese" or "vi" or "vi-vn" or "vi_vn" => "vie",
                _ => p
            };
        }

        var parts = language
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MapOne);

        return string.Join('+', parts);
    }

    internal static ProcessingProfile ParseProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return ProcessingProfile.Scan;

        return profile.ToLowerInvariant() switch
        {
            "scan" => ProcessingProfile.Scan,
            "photo" => ProcessingProfile.Photo,
            "fast" => ProcessingProfile.Fast,
            _ => ProcessingProfile.Scan
        };
    }

    internal static void ValidateLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language is required", nameof(language));

        if (!System.Text.RegularExpressions.Regex.IsMatch(language, @"^[a-z0-9+_-]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException($"Invalid language format: {language}. Expected format: 'eng', 'eng+fra', etc.", nameof(language));
    }
}

