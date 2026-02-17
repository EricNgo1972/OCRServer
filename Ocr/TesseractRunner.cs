using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Tesseract;

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
        var configuredPath = configuration["Ocr:TesseractDataPath"] ?? "/usr/share/tesseract-ocr/5/tessdata";
        _logger = logger;

        _tessdataPath = ResolveTessdataPath(configuredPath);

        // Verify tessdata path exists
        if (!Directory.Exists(_tessdataPath))
        {
            _logger.LogWarning(
                "Tesseract data path not found: {Path}. OCR will fail until tessdata is installed and Ocr:TesseractDataPath is set.",
                _tessdataPath);
        }
    }

    private string ResolveTessdataPath(string configuredPath)
    {
        // Prefer bundled tessdata (copied to output next to the app) so the app works without system Tesseract
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (Directory.Exists(bundledPath))
        {
            _logger.LogInformation("Using bundled Tesseract data path: {Path}", bundledPath);
            return bundledPath;
        }

        if (Directory.Exists(configuredPath))
            return configuredPath;

        // Fallbacks: system installs (Windows) or configured path (Linux)
        if (OperatingSystem.IsWindows())
        {
            var candidates = new List<string?>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tessdata"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tesseract-OCR", "tessdata"),
                Path.Combine(Directory.GetCurrentDirectory(), "tessdata"),
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (Directory.Exists(candidate))
                {
                    _logger.LogInformation("Using Tesseract data path: {Path}", candidate);
                    return candidate;
                }
            }

            var preferred = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tessdata");
            return string.IsNullOrWhiteSpace(preferred) ? configuredPath : preferred;
        }

        return configuredPath;
    }

    /// <summary>
    /// Performs OCR on an encoded image (PNG/JPG/PGM/etc.)
    /// Returns text and confidence score
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    public (string text, float confidence) PerformOcr(byte[] imageBytes, string language)
        => PerformOcrAsync(imageBytes, language, CancellationToken.None).GetAwaiter().GetResult();

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    public async Task<(string text, float confidence)> PerformOcrAsync(byte[] imageBytes, string language, CancellationToken cancellationToken)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            throw new ArgumentException("Image bytes are empty", nameof(imageBytes));

        if (OperatingSystem.IsLinux())
        {
            // On Linux, prefer the CLI to avoid fragile native wrapper filename expectations across distros.
            // We'll write to a temp file and invoke `tesseract` as an external process.
            var tempDir = Path.Combine(Path.GetTempPath(), "ocrserver-tesseract", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var tempImg = Path.Combine(tempDir, "input.png");
            try
            {
                await File.WriteAllBytesAsync(tempImg, imageBytes, cancellationToken);
                return await PerformOcrFileAsync(tempImg, language, cancellationToken);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }

        try
        {
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
        catch (BadImageFormatException ex)
        {
            // Pix couldn't decode the bytes (not a supported image format)
            throw new InvalidOperationException($"OCR failed: Tesseract could not decode the image bytes ({ex.Message}). Ensure the input is a supported image (PNG/JPG/PGM/PPM).", ex);
        }
        catch (Win32Exception ex)
        {
            // Some native dependency is missing (libtesseract/leptonica)
            throw new InvalidOperationException($"OCR failed due to missing native dependency: {ex.Message}. Ensure Tesseract + Leptonica system libraries are installed.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed for language: {Language}, tessdata path: {TessdataPath}", language, _tessdataPath);

            var pathExists = Directory.Exists(_tessdataPath);
            var hint = pathExists
                ? $"Tessdata path exists but may be missing '{language}.traineddata'. Add the language data file to {_tessdataPath}"
                : $"Tessdata path not found: {_tessdataPath}. Install Tesseract (e.g. https://github.com/UB-Mannheim/tesseract/wiki) or set Ocr:TesseractDataPath in appsettings to a folder containing .traineddata files.";

            throw new InvalidOperationException($"OCR processing failed: {ex.Message}. {hint}", ex);
        }
    }

    /// <summary>
    /// Performs OCR on an image file path (PNG/JPG/PGM/etc.)
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    public (string text, float confidence) PerformOcrFile(string imagePath, string language)
        => PerformOcrFileAsync(imagePath, language, CancellationToken.None).GetAwaiter().GetResult();

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    public async Task<(string text, float confidence)> PerformOcrFileAsync(string imagePath, string language, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path is required", nameof(imagePath));

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Image file not found", imagePath);

        if (OperatingSystem.IsLinux())
        {
            // Linux: call `tesseract` CLI for maximum compatibility with distro packaging.
            // Confidence is not available from plain-text output; we return 0.0 for now.
            var result = await RunTesseractCliAsync(imagePath, language, dpi: 300, cancellationToken);
            return (result, 0.0f);
        }

        return await Task.Run(() =>
        {
            using var engine = new TesseractEngine(_tessdataPath, language, EngineMode.LstmOnly);
            engine.SetVariable("tessedit_char_whitelist", "");
            engine.SetVariable("preserve_interword_spaces", "1");

            using var pix = Pix.LoadFromFile(imagePath);
            using var page = engine.Process(pix);

            string text = page.GetText();
            float confidence = page.GetMeanConfidence() / 100.0f;

            return (text, confidence);
        }, cancellationToken);
    }

    /// <summary>
    /// Linux-only: runs `tesseract` external process and returns extracted text.
    /// </summary>
    private async Task<string> RunTesseractCliAsync(string imagePath, string language, int dpi, CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            Quote(imagePath),
            "stdout",
            "-l", language,
            "--dpi", dpi.ToString()
        };

        // If we have a valid tessdata directory (bundled or configured), pass it explicitly.
        if (Directory.Exists(_tessdataPath))
        {
            args.Add("--tessdata-dir");
            args.Add(Quote(_tessdataPath));
        }

        var psi = new ProcessStartInfo
        {
            FileName = "tesseract",
            Arguments = string.Join(' ', args),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("Failed to start tesseract process.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "tesseract CLI was not found. Install it (Ubuntu/Debian: `sudo apt-get install -y tesseract-ocr`).",
                ex);
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await proc.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0)
        {
            var hint = Directory.Exists(_tessdataPath)
                ? $"Verify language data exists under `{_tessdataPath}` (e.g. `{language}.traineddata`)."
                : "Verify tessdata is installed and/or set `Ocr:TesseractDataPath` to a folder containing .traineddata files.";

            throw new InvalidOperationException($"tesseract failed with exit code {proc.ExitCode}. {hint} stderr: {Trim(stderr)}");
        }

        return stdout ?? "";
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    private static string Trim(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        return s.Length > 2000 ? s[..2000] + "…" : s;
    }
}
