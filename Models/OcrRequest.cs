namespace OCRServer.Models;

/// <summary>
/// OCR request model for multipart/form-data input
/// </summary>
public class OcrRequest
{
    public const string DefaultLanguage = "eng+fra";

    /// <summary>
    /// Image or PDF file to process
    /// </summary>
    public required IFormFile File { get; set; }

    /// <summary>
    /// Tesseract language code(s), e.g., "eng", "eng+fra", "eng+vie".
    /// Falls back to "eng+fra" when omitted.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Optional preprocessing profile: "scan", "photo", or "fast"
    /// </summary>
    public string? Profile { get; set; }
}
