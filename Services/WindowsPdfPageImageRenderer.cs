using OpenCvSharp;

namespace OCRServer.Services;

public sealed class WindowsPdfPageImageRenderer
{
    private readonly IPdfRenderer _pdfRenderer;
    private readonly ILogger<WindowsPdfPageImageRenderer> _logger;

    public WindowsPdfPageImageRenderer(IPdfRenderer pdfRenderer, ILogger<WindowsPdfPageImageRenderer> logger)
    {
        _pdfRenderer = pdfRenderer;
        _logger = logger;
    }

    public Task<RenderedPdfPages> RenderToPngImagesAsync(byte[] pdfBytes, int dpi, CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ocrserver-pdfium-pages", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var pages = _pdfRenderer.RenderPdf(pdfBytes, dpi, cancellationToken);
            var imagePaths = new List<string>(pages.Count);

            for (int i = 0; i < pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var pageMat = pages[i];
                var outputPath = Path.Combine(tempRoot, $"page-{i + 1}.png");
                Cv2.ImWrite(outputPath, pageMat);
                imagePaths.Add(outputPath);
            }

            _logger.LogInformation("PDFium rendered {PageCount} page(s) at {Dpi} DPI into {TempDir}", imagePaths.Count, dpi, tempRoot);
            return Task.FromResult<RenderedPdfPages>(new RenderedPdfPages(tempRoot, imagePaths));
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            throw;
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
