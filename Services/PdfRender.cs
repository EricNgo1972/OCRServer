using System;
using System.Collections.Generic;
using System.Threading;

using OpenCvSharp;

using PDFiumCore;

using static PDFiumCore.fpdfview;

namespace OCRServer.Services
{
    public sealed class PdfiumPdfRenderer : IPdfRenderer
    {
        public IReadOnlyList<Mat> RenderPdf(byte[] pdfBytes, int dpi, CancellationToken ct)
        {
            var mats = new List<Mat>();

            FPDF_InitLibrary();

            unsafe
            {
                fixed (byte* data = pdfBytes)
                {
                    // typed handle (your IntelliSense shows this)
                    FpdfDocument doc = FPDF_LoadMemDocument((nint)data, pdfBytes.Length, null);

                    // null/empty handle check for struct-like handles
                    if (doc.Equals(default(FpdfDocument)))
                        throw new InvalidOperationException("Failed to load PDF document");

                    int pageCount = FPDF_GetPageCount(doc);

                    for (int i = 0; i < pageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        FpdfPage page = FPDF_LoadPage(doc, i);
                        if (page.Equals(default(FpdfPage)))
                            continue;

                        double pageWidth = FPDF_GetPageWidth(page);
                        double pageHeight = FPDF_GetPageHeight(page);

                        int width = (int)(pageWidth * dpi / 72.0);
                        int height = (int)(pageHeight * dpi / 72.0);

                        FpdfBitmap bitmap = FPDFBitmap_Create(width, height, 1);
                        if (bitmap.Equals(default(FpdfBitmap)))
                        {
                            FPDF_ClosePage(page);
                            continue;
                        }

                        // white background
                        FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);

                        FPDF_RenderPageBitmap(
                            bitmap,
                            page,
                            0, 0,
                            width, height,
                            0,
                            FPDF_ANNOT
                        );

                        nint buffer = FPDFBitmap_GetBuffer(bitmap);
                        int stride = FPDFBitmap_GetStride(bitmap);

                        // Important: OpenCvSharp Mat ctor expects IntPtr, so cast nint -> IntPtr
                        using var bgra = new Mat(height, width, MatType.CV_8UC4, (IntPtr)buffer, stride);

                        var bgr = new Mat();
                        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

                        mats.Add(bgr);

                        FPDFBitmap_Destroy(bitmap);
                        FPDF_ClosePage(page);
                    }

                    FPDF_CloseDocument(doc);
                }
            }

            return mats;
        }
    }
}
