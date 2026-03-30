namespace OCRServer.Models;

public sealed class SearchablePdfResult
{
    public required byte[] PdfBytes { get; init; }

    public required string FileName { get; init; }

    public required int PageCount { get; init; }

    public bool WasAlreadySearchable { get; init; }

    public required string Language { get; init; }
}
