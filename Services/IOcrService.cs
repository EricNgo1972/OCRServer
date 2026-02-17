using OCRServer.Models;

namespace OCRServer.Services;

/// <summary>
/// OCR service interface
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Processes OCR request and returns structured response
    /// </summary>
    Task<OcrResponse> ProcessAsync(OcrRequest request, CancellationToken cancellationToken = default);
}
