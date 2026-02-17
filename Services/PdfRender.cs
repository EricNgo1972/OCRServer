using OpenCvSharp;

using PDFiumCore;

using static PDFiumCore.fpdfview;

namespace OCRServer.Services
{
    public sealed class PdfiumPdfRenderer : IPdfRenderer
    {
        private static int _initialized;

        private static void EnsurePdfiumInitialized()
        {
            // PDFium is process-wide; initialize once.
            if (Interlocked.Exchange(ref _initialized, 1) == 0)
                FPDF_InitLibrary();
        }

        public IReadOnlyList<Mat> RenderPdf(byte[] pdfBytes, int dpi, CancellationToken ct)
        {
            var mats = new List<Mat>();

            EnsurePdfiumInitialized();

            unsafe
            {
                fixed (byte* data = pdfBytes)
                {
                    FpdfDocumentT doc = FPDF_LoadMemDocument((IntPtr)data, pdfBytes.Length, null);

                    // null/empty handle check for struct-like handles
                    if (doc.Equals(default(FpdfDocumentT)))
                        throw new InvalidOperationException("Failed to load PDF document");

                    int pageCount = FPDF_GetPageCount(doc);

                    for (int i = 0; i < pageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        FpdfPageT page = FPDF_LoadPage(doc, i);
                        if (page.Equals(default(FpdfPageT)))
                            continue;

                        double pageWidth = FPDF_GetPageWidth(page);
                        double pageHeight = FPDF_GetPageHeight(page);

                        int width = (int)(pageWidth * dpi / 72.0);
                        int height = (int)(pageHeight * dpi / 72.0);

                        FpdfBitmapT bitmap = FPDFBitmapCreate(width, height, 1);
                        if (bitmap.Equals(default(FpdfBitmapT)))
                        {
                            FPDF_ClosePage(page);
                            continue;
                        }

                        // white background
                        FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFFu);

                        FPDF_RenderPageBitmap(
                            bitmap,
                            page,
                            0, 0,
                            width, height,
                            0,
                            (int)RenderFlags.RenderAnnotations
                        );

                        IntPtr buffer = FPDFBitmapGetBuffer(bitmap);
                        int stride = FPDFBitmapGetStride(bitmap);

                        // Important: OpenCvSharp Mat ctor expects IntPtr, so cast nint -> IntPtr
                        using var bgra = new Mat(height, width, MatType.CV_8UC4, buffer, stride);

                        var bgr = new Mat();
                        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

                        mats.Add(bgr);

                        FPDFBitmapDestroy(bitmap);
                        FPDF_ClosePage(page);
                    }

                    FPDF_CloseDocument(doc);
                }
            }

            return mats;
        }
    }
}
