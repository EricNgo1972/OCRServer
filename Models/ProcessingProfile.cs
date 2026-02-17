namespace OCRServer.Models;

/// <summary>
/// Preprocessing profile types
/// </summary>
public enum ProcessingProfile
{
    /// <summary>
    /// Full preprocessing pipeline (default for scanned documents)
    /// </summary>
    Scan,

    /// <summary>
    /// Stronger contrast and deskew (for photos)
    /// </summary>
    Photo,

    /// <summary>
    /// Minimal preprocessing (fastest)
    /// </summary>
    Fast
}
