using OCRServer.Models;
using OCRServer.Ocr;

namespace OCRServer.Services;

public sealed class LinuxSearchablePdfService : ISearchablePdfService
{
    private readonly PdfTextLayerDetector _textLayerDetector;
    private readonly PdftoppmPdfRenderer _pdfRenderer;
    private readonly PdfMergeService _pdfMergeService;
    private readonly TesseractRunner _tesseractRunner;
    private readonly ILogger<LinuxSearchablePdfService> _logger;

    public LinuxSearchablePdfService(
        PdfTextLayerDetector textLayerDetector,
        PdftoppmPdfRenderer pdfRenderer,
        PdfMergeService pdfMergeService,
        TesseractRunner tesseractRunner,
        ILogger<LinuxSearchablePdfService> logger)
    {
        _textLayerDetector = textLayerDetector;
        _pdfRenderer = pdfRenderer;
        _pdfMergeService = pdfMergeService;
        _tesseractRunner = tesseractRunner;
        _logger = logger;
    }

    public async Task<SearchablePdfResult> CreateAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
            throw new NotSupportedException("LinuxSearchablePdfService can only run on Linux.");

        return await SearchablePdfServiceHelpers.CreateAsync(
            request,
            async pdfBytes => await _pdfRenderer.RenderToGrayImagesAsync(pdfBytes, dpi: 300, cancellationToken),
            _textLayerDetector,
            _pdfMergeService,
            _tesseractRunner,
            _logger,
            cancellationToken);
    }
}

public sealed class WindowsSearchablePdfService : ISearchablePdfService
{
    private readonly PdfTextLayerDetector _textLayerDetector;
    private readonly WindowsPdfPageImageRenderer _pdfRenderer;
    private readonly PdfMergeService _pdfMergeService;
    private readonly TesseractRunner _tesseractRunner;
    private readonly ILogger<WindowsSearchablePdfService> _logger;

    public WindowsSearchablePdfService(
        PdfTextLayerDetector textLayerDetector,
        WindowsPdfPageImageRenderer pdfRenderer,
        PdfMergeService pdfMergeService,
        TesseractRunner tesseractRunner,
        ILogger<WindowsSearchablePdfService> logger)
    {
        _textLayerDetector = textLayerDetector;
        _pdfRenderer = pdfRenderer;
        _pdfMergeService = pdfMergeService;
        _tesseractRunner = tesseractRunner;
        _logger = logger;
    }

    public async Task<SearchablePdfResult> CreateAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new NotSupportedException("WindowsSearchablePdfService can only run on Windows.");

        return await SearchablePdfServiceHelpers.CreateAsync(
            request,
            async pdfBytes => await _pdfRenderer.RenderToPngImagesAsync(pdfBytes, dpi: 300, cancellationToken),
            _textLayerDetector,
            _pdfMergeService,
            _tesseractRunner,
            _logger,
            cancellationToken);
    }
}

internal static class SearchablePdfServiceHelpers
{
    internal static async Task<SearchablePdfResult> CreateAsync(
        OcrRequest request,
        Func<byte[], Task<RenderedPdfPages>> renderPagesAsync,
        PdfTextLayerDetector textLayerDetector,
        PdfMergeService pdfMergeService,
        TesseractRunner tesseractRunner,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        request.Language = string.IsNullOrWhiteSpace(request.Language)
            ? OcrRequest.DefaultLanguage
            : request.Language;

        OcrServiceHelpers.ValidateLanguage(request.Language);
        request.Language = OcrServiceHelpers.NormalizeLanguage(request.Language);

        byte[] pdfBytes;
        using (var stream = new MemoryStream())
        {
            await request.File.CopyToAsync(stream, cancellationToken);
            pdfBytes = stream.ToArray();
        }

        var fileName = request.File.FileName ?? "document.pdf";
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The searchable PDF endpoint only accepts PDF files.", nameof(request));

        if (textLayerDetector.HasMeaningfulText(pdfBytes))
        {
            logger.LogInformation("PDF already contains meaningful text. Returning original file: {FileName}", fileName);
            return new SearchablePdfResult
            {
                PdfBytes = pdfBytes,
                FileName = fileName,
                PageCount = 0,
                WasAlreadySearchable = true,
                Language = request.Language
            };
        }

        await using var rendered = await renderPagesAsync(pdfBytes);
        var pageImagePaths = rendered.PageImagePaths.ToArray();
        var pagePdfPaths = new List<string>(pageImagePaths.Length);

        foreach (var pageImagePath in pageImagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pagePdfPath = Path.ChangeExtension(pageImagePath, ".pdf");
            await tesseractRunner.GenerateSearchablePdfAsync(pageImagePath, request.Language, pagePdfPath, cancellationToken);
            pagePdfPaths.Add(pagePdfPath);
        }

        var mergedPdfBytes = pdfMergeService.MergeFiles(pagePdfPaths);

        return new SearchablePdfResult
        {
            PdfBytes = mergedPdfBytes,
            FileName = BuildOutputFileName(fileName),
            PageCount = pageImagePaths.Length,
            WasAlreadySearchable = false,
            Language = request.Language
        };
    }

    private static string BuildOutputFileName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return $"{baseName}-searchable.pdf";
    }
}
