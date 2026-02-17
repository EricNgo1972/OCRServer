using System.ComponentModel;
using System.Diagnostics;

namespace OCRServer.Services;

/// <summary>
/// Linux-only PDF renderer that shells out to Poppler's pdftoppm.
/// Pipeline: PDF -> pdftoppm (300 DPI, grayscale) -> per-page image files (.pgm)
/// </summary>
public sealed class PdftoppmPdfRenderer
{
    private readonly ILogger<PdftoppmPdfRenderer> _logger;

    public PdftoppmPdfRenderer(ILogger<PdftoppmPdfRenderer> logger)
    {
        _logger = logger;
    }

    public async Task<RenderedPdfPages> RenderToGrayImagesAsync(
        byte[] pdfBytes,
        int dpi,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            throw new NotSupportedException("PdftoppmPdfRenderer is Linux-only.");

        if (pdfBytes == null || pdfBytes.Length == 0)
            throw new ArgumentException("PDF bytes are empty", nameof(pdfBytes));

        var tempRoot = Path.Combine(Path.GetTempPath(), "ocrserver-pdftoppm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var inputPdfPath = Path.Combine(tempRoot, "input.pdf");
        var outputPrefix = Path.Combine(tempRoot, "page");

        try
        {
            await File.WriteAllBytesAsync(inputPdfPath, pdfBytes, cancellationToken);

            // Prefer grayscale PGM output for stability and OCR quality.
            // Output files will look like: page-1.pgm, page-2.pgm, ...
            var args = $"-r {dpi} -gray \"{inputPdfPath}\" \"{outputPrefix}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "pdftoppm",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = tempRoot,
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            try
            {
                if (!proc.Start())
                    throw new InvalidOperationException("Failed to start pdftoppm process.");
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "pdftoppm was not found. Install Poppler utilities (Ubuntu/Debian: `sudo apt-get install -y poppler-utils`).",
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
                throw new InvalidOperationException(
                    $"pdftoppm failed with exit code {proc.ExitCode}. stderr: {Trim(stderr)} stdout: {Trim(stdout)}");
            }

            var pageFiles = Directory
                .EnumerateFiles(tempRoot, "page-*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".pgm", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".ppm", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(ExtractPageNumber)
                .ToArray();

            if (pageFiles.Length == 0)
                throw new InvalidOperationException("pdftoppm completed successfully but produced no page images.");

            _logger.LogInformation("pdftoppm rendered {PageCount} page(s) at {Dpi} DPI into {TempDir}", pageFiles.Length, dpi, tempRoot);

            return new RenderedPdfPages(tempRoot, pageFiles);
        }
        catch
        {
            // On failure, clean up temp files immediately.
            TryDeleteDirectory(tempRoot);
            throw;
        }
    }

    private static int ExtractPageNumber(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath); // e.g. page-12
        var dash = name.LastIndexOf('-');
        if (dash < 0) return int.MaxValue;
        return int.TryParse(name[(dash + 1)..], out var n) ? n : int.MaxValue;
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private static string Trim(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        return s.Length > 2000 ? s[..2000] + "…" : s;
    }
}

/// <summary>
/// Disposable handle for rendered PDF pages.
/// Deleting the temp directory is required to avoid disk leaks.
/// </summary>
public sealed class RenderedPdfPages : IAsyncDisposable
{
    public string TempDirectory { get; }
    public IReadOnlyList<string> PageImagePaths { get; }

    internal RenderedPdfPages(string tempDirectory, IReadOnlyList<string> pageImagePaths)
    {
        TempDirectory = tempDirectory;
        PageImagePaths = pageImagePaths;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
                Directory.Delete(TempDirectory, recursive: true);
        }
        catch
        {
            // best effort
        }

        return ValueTask.CompletedTask;
    }
}

