using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace OCRServer.Services;

public sealed class PdfMergeService
{
    public byte[] MergeFiles(IEnumerable<string> pdfPaths)
    {
        var output = new PdfDocument();

        foreach (var pdfPath in pdfPaths)
        {
            using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            for (int i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }

        using var stream = new MemoryStream();
        output.Save(stream, false);
        return stream.ToArray();
    }
}
