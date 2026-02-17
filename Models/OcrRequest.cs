namespace OCRServer.Models;

/// <summary>
/// OCR request model for multipart/form-data input
/// </summary>
public class OcrRequest
{
    /// <summary>
    /// Image or PDF file to process
    /// </summary>
    public required IFormFile File { get; set; }

    /// <summary>
    /// Tesseract language code(s), e.g., "eng", "eng+fra", "eng+vie"
    /// </summary>
    public required string Language { get; set; }

    /// <summary>
    /// Optional preprocessing profile: "scan", "photo", or "fast"
    /// </summary>
    public string? Profile { get; set; }
}
