using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace OCRServer.Services;

public sealed class PdfTextLayerDetector
{
    private const int MinLengthThreshold = 30;
    private const int MinUniqueWords = 5;

    private readonly ILogger<PdfTextLayerDetector> _logger;

    public PdfTextLayerDetector(ILogger<PdfTextLayerDetector> logger)
    {
        _logger = logger;
    }

    public bool HasMeaningfulText(byte[] pdfBytes)
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
            return false;

        try
        {
            using var stream = new MemoryStream(pdfBytes, writable: false);
            using var document = PdfDocument.Open(stream);

            var combined = string.Join(' ', document.GetPages().Select(p => p.Text));
            var normalized = NormalizeWhitespace(combined);

            if (normalized.Length < MinLengthThreshold)
                return false;

            return IsMeaningfulText(normalized);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to detect PDF text layer.");
            return false;
        }
    }

    private static string NormalizeWhitespace(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";

        return Regex.Replace(s.Trim(), @"[\s]+", " ");
    }

    private static bool IsMeaningfulText(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var unique = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);

        if (unique.Count < MinUniqueWords)
            return false;

        var digitOnly = words.Count(w => w.Length > 0 && w.All(char.IsDigit));
        if (words.Length > 0 && digitOnly * 10 >= words.Length * 9)
            return false;

        return true;
    }
}
