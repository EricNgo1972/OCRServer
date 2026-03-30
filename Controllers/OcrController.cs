using Microsoft.AspNetCore.Mvc;
using OCRServer.Models;
using OCRServer.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace OCRServer.Controllers;

/// <summary>
/// OCR API controller
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OcrController : ControllerBase
{
    private readonly IOcrService _ocrService;
    private readonly ILogger<OcrController> _logger;
    private readonly IHostEnvironment _env;

    public OcrController(IOcrService ocrService, ILogger<OcrController> logger, IHostEnvironment env)
    {
        _ocrService = ocrService;
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// Performs OCR on uploaded image or PDF
    /// </summary>
    /// <param name="request">Multipart form-data body containing file, language, and profile</param>
    /// <returns>OCR results with extracted text and confidence scores</returns>
    [HttpPost]
    [EnableRateLimiting("ocr")]
    [ProducesResponseType(typeof(OcrResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OcrResponse>> ProcessOcr([FromForm] OcrRequest request)
    {
        var file = request.File;
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "File is required and must not be empty" });
        }

        // Validate file size (max 50MB)
        const long maxFileSize = 50 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            return BadRequest(new { error = $"File size exceeds maximum of {maxFileSize / 1024 / 1024}MB" });
        }

        try
        {
            request.Language = string.IsNullOrWhiteSpace(request.Language)
                ? OcrRequest.DefaultLanguage
                : request.Language;

            var response = await _ocrService.ProcessAsync(request);

            _logger.LogInformation(
                "OCR completed: {FileName}, {PageCount} pages, {Confidence:F2} confidence, {Ms}ms",
                file.FileName, response.Pages.Count, response.Confidence, response.ProcessingMs);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid request: {FileName}", file.FileName);
            return BadRequest(new { error = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Unsupported operation: {FileName}", file.FileName);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR processing failed: {FileName}", file.FileName);

            // In Development, let the global exception handler return the real error message
            // (helps with diagnosing missing tessdata, PDF rendering, native library issues, etc.).
            if (_env.IsDevelopment())
                throw;

            return StatusCode(500, new { error = "OCR processing failed. Please check the logs for details." });
        }
    }
}
