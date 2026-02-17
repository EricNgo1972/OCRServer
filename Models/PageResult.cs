namespace OCRServer.Models;

/// <summary>
/// Represents OCR result for a single page
/// </summary>
public class PageResult
{
    /// <summary>
    /// Page number (1-indexed)
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Extracted text content
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
