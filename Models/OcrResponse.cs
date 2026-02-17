namespace OCRServer.Models;

/// <summary>
/// OCR response model matching the specified API contract
/// </summary>
public class OcrResponse
{
    /// <summary>
    /// OCR engine version identifier
    /// </summary>
    public string Engine { get; set; } = "tesseract-5.x";

    /// <summary>
    /// Language(s) used for OCR
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Preprocessing profile used
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Average confidence score (0.0 to 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// OCR results per page
    /// </summary>
    public List<PageResult> Pages { get; set; } = new();

    /// <summary>
    /// Total processing time in milliseconds
    /// </summary>
    public long ProcessingMs { get; set; }
}
