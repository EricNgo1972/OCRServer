using System.ComponentModel;
using System.Diagnostics;

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
                throw new InvalidOperationException(
                    "pdfinfo was not found. Install Poppler utilities (Ubuntu/Debian: `sudo apt-get install -y poppler-utils`).",
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
                throw new InvalidOperationException($"pdfinfo failed with exit code {proc.ExitCode}. stderr: {Trim(stderr)}");

            var pages = ParsePages(stdout);
            if (pages <= 0)
            {
                _logger.LogWarning("pdfinfo output did not contain a valid Pages count. stdout={Stdout}", Trim(stdout));
                throw new InvalidOperationException("Could not determine PDF page count (pdfinfo output missing 'Pages:').");
            }

            return pages;
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
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

