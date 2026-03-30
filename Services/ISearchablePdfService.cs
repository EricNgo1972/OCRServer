using OCRServer.Models;

namespace OCRServer.Services;

public interface ISearchablePdfService
{
    Task<SearchablePdfResult> CreateAsync(OcrRequest request, CancellationToken cancellationToken = default);
}
