using OCRServer.Models;
using OCRServer.Ocr;
using OCRServer.Processing;

using OpenCvSharp;

namespace OCRServer.Services;

/// <summary>
/// Main OCR service implementation
/// Handles PDF conversion, image preprocessing, and OCR execution
/// Stateless and thread-safe
/// </summary>
/// <summary>
/// Windows OCR implementation (PDFium + OpenCV preprocessing + Tesseract).
/// Linux should NOT use this path.
/// </summary>
public class WindowsOcrService : IOcrService
{
    private readonly ImagePreprocessor _preprocessor;
    private readonly TesseractRunner _tesseractRunner;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly ILogger<WindowsOcrService> _logger;

    public WindowsOcrService(
        ImagePreprocessor preprocessor,
        TesseractRunner tesseractRunner,
        IPdfRenderer pdfRenderer,
        ILogger<WindowsOcrService> logger)
    {
        _preprocessor = preprocessor;
        _tesseractRunner = tesseractRunner;
        _pdfRenderer = pdfRenderer;
        _logger = logger;
    }

    public async Task<OcrResponse> ProcessAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Parse profile
            ProcessingProfile profile = ParseProfile(request.Profile);

            // Validate language
            ValidateLanguage(request.Language);
            request.Language = NormalizeLanguage(request.Language);

            // Read file content
            byte[] fileBytes;
            using (var stream = new MemoryStream())
            {
                await request.File.CopyToAsync(stream, cancellationToken);
                fileBytes = stream.ToArray();
            }

            // Process based on file type
            List<PageResult> pages = new List<PageResult>();
            List<float> confidences = new List<float>();

            string contentType = request.File.ContentType.ToLower();
            string fileName = request.File.FileName.ToLower();

            if (contentType.Contains("pdf") || fileName.EndsWith(".pdf"))
            {
                _logger.LogInformation("Processing PDF file: {FileName}", request.File.FileName);
                var pdfPages = await ProcessPdfAsync(fileBytes, request.Language, profile, cancellationToken);
                pages.AddRange(pdfPages.Select((p, i) => new PageResult { Page = i + 1, Text = p.text }));
                confidences.AddRange(pdfPages.Select(p => p.confidence));
            }
            else if (contentType.Contains("image") ||
                     fileName.EndsWith(".png") ||
                     fileName.EndsWith(".jpg") ||
                     fileName.EndsWith(".jpeg"))
            {
                _logger.LogInformation("Processing image file: {FileName}", request.File.FileName);
                var result = await ProcessImageAsync(fileBytes, request.Language, profile, cancellationToken);
                pages.Add(new PageResult { Page = 1, Text = result.text });
                confidences.Add(result.confidence);
            }
            else
            {
                throw new ArgumentException($"Unsupported file type: {contentType}. Supported: PDF, PNG, JPG", nameof(request));
            }

            stopwatch.Stop();

            // Calculate average confidence
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

    private Task<List<(string text, float confidence)>> ProcessPdfAsync(byte[] pdfBytes, string language, ProcessingProfile profile, CancellationToken cancellationToken)
    {
        var results = new List<(string text, float confidence)>();

        // Render PDF pages to OpenCV Mats
        var pages = _pdfRenderer.RenderPdf(pdfBytes, dpi: 300, cancellationToken);

        _logger.LogInformation("PDF rendered into {PageCount} pages", pages.Count);

        foreach (var pageMat in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (pageMat)
            {
                // Preprocess
                using var processed = _preprocessor.Preprocess(pageMat, profile);

                // OCR
                Cv2.ImEncode(".png", processed, out var encoded);
                var result = _tesseractRunner.PerformOcr(encoded, language);
                results.Add(result);
            }
        }

        return Task.FromResult(results);
    }

    private string NormalizeLanguage(string language)
    {
        // The API docs encourage Tesseract codes (eng, vie, etc.), but callers often pass names.
        // Normalize a few common ones so local testing "just works".
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



    /// <summary>
    /// Processes a single image file
    /// </summary>
    private async Task<(string text, float confidence)> ProcessImageAsync(
        byte[] imageBytes,
        string language,
        ProcessingProfile profile,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            Mat? image = null;
            Mat? processed = null;

            try
            {
                // Load image from bytes
                image = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                if (image == null || image.Empty())
                {
                    throw new ArgumentException("Failed to decode image");
                }

                // Preprocess image
                processed = _preprocessor.Preprocess(image, profile);

                // Perform OCR
                Cv2.ImEncode(".png", processed, out var encoded);
                var result = _tesseractRunner.PerformOcr(encoded, language);

                return result;
            }
            finally
            {
                image?.Dispose();
                processed?.Dispose();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Parses processing profile from string
    /// </summary>
    private ProcessingProfile ParseProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return ProcessingProfile.Scan;

        return profile.ToLower() switch
        {
            "scan" => ProcessingProfile.Scan,
            "photo" => ProcessingProfile.Photo,
            "fast" => ProcessingProfile.Fast,
            _ => ProcessingProfile.Scan
        };
    }

    /// <summary>
    /// Validates language code format
    /// </summary>
    private void ValidateLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language is required", nameof(language));

        // Basic validation: should contain only letters, numbers, +, and -
        if (!System.Text.RegularExpressions.Regex.IsMatch(language, @"^[a-z0-9+_-]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException($"Invalid language format: {language}. Expected format: 'eng', 'eng+fra', etc.", nameof(language));
    }
}
