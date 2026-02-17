using Tesseract;
using OpenCvSharp;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OCRServer.Ocr;

/// <summary>
/// Wrapper for Tesseract OCR engine
/// Thread-safe and stateless
/// </summary>
public class TesseractRunner
{
    private readonly string _tessdataPath;
    private readonly ILogger<TesseractRunner> _logger;

    public TesseractRunner(IConfiguration configuration, ILogger<TesseractRunner> logger)
    {
        _tessdataPath = configuration["Ocr:TesseractDataPath"] ?? "/usr/share/tesseract-ocr/5/tessdata";
        _logger = logger;

        // Verify tessdata path exists
        if (!Directory.Exists(_tessdataPath))
        {
            _logger.LogWarning("Tesseract data path not found: {Path}. OCR may fail.", _tessdataPath);
        }
    }

    /// <summary>
    /// Performs OCR on a preprocessed image
    /// Returns text and confidence score
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    public (string text, float confidence) PerformOcr(Mat image, string language)
    {
        if (image.Empty())
            throw new ArgumentException("Image is empty", nameof(image));

        try
        {
            // Convert OpenCV Mat to byte array for Tesseract
            byte[] imageBytes = MatToByteArray(image);

            using var engine = new TesseractEngine(_tessdataPath, language, EngineMode.LstmOnly);
            engine.SetVariable("tessedit_char_whitelist", ""); // No character restrictions
            engine.SetVariable("preserve_interword_spaces", "1");

            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = engine.Process(pix);

            string text = page.GetText();
            float confidence = page.GetMeanConfidence() / 100.0f; // Convert to 0.0-1.0 range

            _logger.LogDebug("OCR completed: {Length} characters, confidence: {Confidence:F2}", 
                text.Length, confidence);

            return (text, confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed for language: {Language}", language);
            throw new InvalidOperationException($"OCR processing failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Converts OpenCV Mat to byte array (PNG format)
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    private byte[] MatToByteArray(Mat image)
    {
        using var bitmap = MatToBitmap(image);
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>
    /// Converts OpenCV Mat to System.Drawing.Bitmap
    /// Uses OpenCV's ImEncode to convert to PNG, then loads as Bitmap
    /// Works on Linux with libgdiplus installed
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    private Bitmap MatToBitmap(Mat mat)
    {
        // Use OpenCV's ImEncode to convert Mat to PNG bytes, then load as Bitmap
        // This avoids unsafe pointer operations
        byte[] pngBytes;
        Cv2.ImEncode(".png", mat, out pngBytes);
        
        using var ms = new MemoryStream(pngBytes);
        return new Bitmap(ms);
    }
}
