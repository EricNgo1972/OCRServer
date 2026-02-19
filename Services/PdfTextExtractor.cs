using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OCRServer.Services;

/// <summary>
/// Extracts embedded text from PDFs using pdftotext (Poppler).
/// Used on Linux to skip OCR when the PDF already has a text layer.
/// No temporary files: PDF bytes are piped via stdin.
/// </summary>
public sealed class PdfTextExtractor
{
    private const int MinLengthThreshold = 30;
    private const int MinUniqueWords = 5;

    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(ILogger<PdfTextExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Tries to extract text from the PDF using pdftotext.
    /// Returns null if extraction fails, text is empty, or content is not meaningful (e.g. only page numbers).
    /// Errors from pdftotext do not throw; they result in null (OCR fallback).
    /// </summary>
    public async Task<string?> TryExtractPdfTextAsync(byte[] pdfBytes, CancellationToken cancellationToken)
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
            return null;

        if (!OperatingSystem.IsLinux())
            return null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var psi = new ProcessStartInfo
            {
                FileName = "pdftotext",
                Arguments = "-layout -enc UTF-8 - -",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!proc.Start())
                return null;

            // Write PDF to stdin and close so pdftotext gets EOF
            await proc.StandardInput.BaseStream.WriteAsync(pdfBytes, cts.Token);
            await proc.StandardInput.BaseStream.FlushAsync(cts.Token);
            proc.StandardInput.Close();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            await proc.WaitForExitAsync(cts.Token);
            var stdout = await stdoutTask;
            await stderrTask;

            if (proc.ExitCode != 0)
            {
                _logger.LogDebug("pdftotext exited with {Code}, falling back to OCR", proc.ExitCode);
                return null;
            }

            var text = NormalizeWhitespace(stdout ?? "");
            if (text.Length < MinLengthThreshold)
                return null;

            if (!IsMeaningfulText(text))
            {
                _logger.LogDebug("pdftotext text rejected by heuristic (too few words or only numbers), falling back to OCR");
                return null;
            }

            _logger.LogInformation("PDF text layer extracted ({Length} chars), skipping OCR", text.Length);
            return text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "pdftotext failed, falling back to OCR");
            return null;
        }
    }

    private static string NormalizeWhitespace(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        return Regex.Replace(s, @"[\s]+", " ");
    }

    /// <summary>
    /// Reject text that is only numbers, page headers, or has very few unique words.
    /// </summary>
    private static bool IsMeaningfulText(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var unique = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);

        if (unique.Count < MinUniqueWords)
            return false;

        // Reject if almost all tokens are digits (e.g. page numbers only)
        var digitOnly = words.Count(w => w.Length > 0 && w.All(char.IsDigit));
        if (words.Length > 0 && digitOnly * 10 >= words.Length * 9)
            return false;

        return true;
    }
}
