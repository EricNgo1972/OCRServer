namespace OCRServer.Services;

using OpenCvSharp;

public interface IPdfRenderer
{
    IReadOnlyList<Mat> RenderPdf(byte[] pdfBytes, int dpi, CancellationToken ct);
}
