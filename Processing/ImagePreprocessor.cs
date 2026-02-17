using OpenCvSharp;
using OCRServer.Models;

namespace OCRServer.Processing;

/// <summary>
/// Image preprocessing pipeline for OCR
/// Implements deterministic preprocessing steps:
/// Grayscale → CLAHE → Noise Reduction → Deskew → Adaptive Threshold
/// </summary>
public class ImagePreprocessor
{
    private readonly DeskewHelper _deskewHelper;
    private readonly ILogger<ImagePreprocessor> _logger;

    public ImagePreprocessor(DeskewHelper deskewHelper, ILogger<ImagePreprocessor> logger)
    {
        _deskewHelper = deskewHelper;
        _logger = logger;
    }

    /// <summary>
    /// Preprocesses image according to the specified profile
    /// </summary>
    public Mat Preprocess(Mat image, ProcessingProfile profile)
    {
        if (image.Empty())
            throw new ArgumentException("Input image is empty", nameof(image));

        Mat processed = image.Clone();

        try
        {
            // Step 1: Convert to grayscale
            processed = ConvertToGrayscale(processed);

            // Step 2: Apply profile-specific preprocessing
            switch (profile)
            {
                case ProcessingProfile.Scan:
                    processed = PreprocessScan(processed);
                    break;
                case ProcessingProfile.Photo:
                    processed = PreprocessPhoto(processed);
                    break;
                case ProcessingProfile.Fast:
                    processed = PreprocessFast(processed);
                    break;
            }

            return processed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during image preprocessing");
            processed.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Converts image to grayscale
    /// </summary>
    private Mat ConvertToGrayscale(Mat image)
    {
        if (image.Channels() == 1)
            return image.Clone();

        Mat gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    /// <summary>
    /// Full preprocessing pipeline for scanned documents
    /// </summary>
    private Mat PreprocessScan(Mat image)
    {
        // CLAHE (Contrast Limited Adaptive Histogram Equalization)
        Mat clahe = ApplyClahe(image);

        // Noise reduction
        Mat denoised = ReduceNoise(clahe);
        clahe.Dispose();

        // Deskew
        double angle = _deskewHelper.DetectSkewAngle(denoised);
        Mat deskewed = _deskewHelper.Deskew(denoised, angle);
        denoised.Dispose();

        // Adaptive threshold
        Mat thresholded = ApplyAdaptiveThreshold(deskewed);
        deskewed.Dispose();

        return thresholded;
    }

    /// <summary>
    /// Preprocessing for photos (stronger contrast + deskew)
    /// </summary>
    private Mat PreprocessPhoto(Mat image)
    {
        // Stronger CLAHE for photos
        Mat clahe = ApplyClahe(image, clipLimit: 3.0, tileGridSize: new Size(8, 8));

        // Noise reduction
        Mat denoised = ReduceNoise(clahe);
        clahe.Dispose();

        // Deskew (photos often have more skew)
        double angle = _deskewHelper.DetectSkewAngle(denoised);
        Mat deskewed = _deskewHelper.Deskew(denoised, angle);
        denoised.Dispose();

        // Adaptive threshold
        Mat thresholded = ApplyAdaptiveThreshold(deskewed);
        deskewed.Dispose();

        return thresholded;
    }

    /// <summary>
    /// Minimal preprocessing for fast processing
    /// </summary>
    private Mat PreprocessFast(Mat image)
    {
        // Only basic contrast normalization
        Mat clahe = ApplyClahe(image, clipLimit: 2.0, tileGridSize: new Size(4, 4));

        // Simple threshold
        Mat thresholded = ApplyAdaptiveThreshold(clahe);
        clahe.Dispose();

        return thresholded;
    }

    /// <summary>
    /// Applies CLAHE (Contrast Limited Adaptive Histogram Equalization)
    /// </summary>
    private Mat ApplyClahe(Mat image, double clipLimit = 2.0, Size? tileGridSize = null)
    {
        tileGridSize ??= new Size(8, 8);

        using var clahe = Cv2.CreateCLAHE(clipLimit: clipLimit, tileGridSize: tileGridSize.Value);
        Mat result = new Mat();
        clahe.Apply(image, result);
        return result;
    }

    /// <summary>
    /// Reduces noise using bilateral filter
    /// </summary>
    private Mat ReduceNoise(Mat image)
    {
        Mat denoised = new Mat();
        Cv2.BilateralFilter(image, denoised, d: 9, sigmaColor: 75, sigmaSpace: 75);
        return denoised;
    }

    /// <summary>
    /// Applies adaptive thresholding
    /// </summary>
    private Mat ApplyAdaptiveThreshold(Mat image)
    {
        Mat thresholded = new Mat();
        Cv2.AdaptiveThreshold(
            image,
            thresholded,
            maxValue: 255,
            adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
            thresholdType: ThresholdTypes.Binary,
            blockSize: 11,
            c: 2
        );
        return thresholded;
    }
}
