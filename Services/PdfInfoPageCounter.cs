using System.ComponentModel;
using System.Diagnostics;
using UglyToad.PdfPig;

namespace OCRServer.Services;

/// <summary>
/// Uses Poppler's `pdfinfo` to count pages in a PDF file. Linux/WSL oriented.
/// </summary>
public sealed class PdfInfoPageCounter
{
    private readonly ILogger<PdfInfoPageCounter> _logger;

    public PdfInfoPageCounter(ILogger<PdfInfoPageCounter> logger)
    {
        _logger = logger;
    }

    public async Task<int> GetPageCountAsync(IFormFile pdfFile, CancellationToken cancellationToken)
    {
        if (pdfFile == null) throw new ArgumentNullException(nameof(pdfFile));

        if (OperatingSystem.IsWindows())
            return await GetPageCountWithPdfPigAsync(pdfFile, cancellationToken);

        var tempRoot = Path.Combine(Path.GetTempPath(), "ocrserver-pdfinfo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var pdfPath = Path.Combine(tempRoot, "input.pdf");

        try
        {
            await using (var input = pdfFile.OpenReadStream())
            await using (var output = File.Create(pdfPath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "pdfinfo",
                Arguments = $"\"{pdfPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            try
            {
                if (!proc.Start())
                    throw new InvalidOperationException("Failed to start pdfinfo process.");
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning(ex, "pdfinfo was not found. Falling back to managed PDF page counting.");
                return await GetPageCountWithPdfPigAsync(pdfFile, cancellationToken);
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
                _logger.LogWarning("pdfinfo failed with exit code {Code}. Falling back to managed PDF page counting. stderr={Stderr}", proc.ExitCode, Trim(stderr));
                return await GetPageCountWithPdfPigAsync(pdfFile, cancellationToken);
            }

            var pages = ParsePages(stdout);
            if (pages <= 0)
            {
                _logger.LogWarning("pdfinfo output did not contain a valid Pages count. stdout={Stdout}", Trim(stdout));
                return await GetPageCountWithPdfPigAsync(pdfFile, cancellationToken);
            }

            return pages;
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task<int> GetPageCountWithPdfPigAsync(IFormFile pdfFile, CancellationToken cancellationToken)
    {
        await using var stream = pdfFile.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        using var document = PdfDocument.Open(memory);
        var pages = document.NumberOfPages;

        if (pages <= 0)
            throw new InvalidOperationException("Could not determine PDF page count.");

        return pages;
    }

    private static int ParsePages(string stdout)
    {
        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Pages:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(':', 2);
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var pages))
                    return pages;
            }
        }
        return 0;
    }

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

