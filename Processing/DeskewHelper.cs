using OpenCvSharp;

namespace OCRServer.Processing;

/// <summary>
/// Helper class for detecting and correcting image skew
/// Uses projection profile method for deskewing
/// </summary>
public class DeskewHelper
{
    /// <summary>
    /// Detects and corrects skew angle in degrees
    /// Returns the angle in degrees (positive = counterclockwise)
    /// </summary>
    public double DetectSkewAngle(Mat image)
    {
        if (image.Empty())
            return 0.0;

        // Convert to grayscale if needed
        Mat gray = image.Channels() == 1 ? image.Clone() : new Mat();
        if (image.Channels() != 1)
        {
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        }

        // Apply binary threshold for better edge detection
        Mat binary = new Mat();
        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // Use morphological operations to connect text lines
        Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Mat morphed = new Mat();
        Cv2.MorphologyEx(binary, morphed, MorphTypes.Close, kernel);

        // Detect edges
        Mat edges = new Mat();
        Cv2.Canny(morphed, edges, 50, 150, 3);

        // Use HoughLinesP to detect lines
        LineSegmentPoint[] lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 100, 100, 10);

        if (lines.Length == 0)
        {
            gray.Dispose();
            binary.Dispose();
            kernel.Dispose();
            morphed.Dispose();
            edges.Dispose();
            return 0.0;
        }

        // Calculate angles from detected lines
        List<double> angles = new List<double>();
        foreach (var line in lines)
        {
            double angle = Math.Atan2(line.P2.Y - line.P1.Y, line.P2.X - line.P1.X) * 180.0 / Math.PI;
            // Normalize to -45 to 45 degrees range
            if (angle > 45)
                angle -= 90;
            if (angle < -45)
                angle += 90;
            angles.Add(angle);
        }

        // Use median angle for robustness
        angles.Sort();
        double medianAngle = angles.Count % 2 == 0
            ? (angles[angles.Count / 2 - 1] + angles[angles.Count / 2]) / 2.0
            : angles[angles.Count / 2];

        // Cleanup
        gray.Dispose();
        binary.Dispose();
        kernel.Dispose();
        morphed.Dispose();
        edges.Dispose();

        // Only correct if angle is significant (> 0.5 degrees)
        return Math.Abs(medianAngle) > 0.5 ? medianAngle : 0.0;
    }

    /// <summary>
    /// Rotates image to correct skew
    /// </summary>
    public Mat Deskew(Mat image, double angle)
    {
        if (Math.Abs(angle) < 0.5 || image.Empty())
            return image.Clone();

        // Get image center
        Point2f center = new Point2f(image.Width / 2.0f, image.Height / 2.0f);

        // Create rotation matrix
        Mat rotationMatrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);

        // Calculate new image size to avoid cropping
        double radians = angle * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(radians));
        double sin = Math.Abs(Math.Sin(radians));
        int newWidth = (int)(image.Height * sin + image.Width * cos);
        int newHeight = (int)(image.Height * cos + image.Width * sin);

        // Adjust rotation matrix center
        rotationMatrix.Set<double>(0, 2, rotationMatrix.Get<double>(0, 2) + (newWidth / 2.0) - center.X);
        rotationMatrix.Set<double>(1, 2, rotationMatrix.Get<double>(1, 2) + (newHeight / 2.0) - center.Y);

        // Apply rotation
        Mat rotated = new Mat();
        Cv2.WarpAffine(image, rotated, rotationMatrix, new Size(newWidth, newHeight),
            InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);

        rotationMatrix.Dispose();
        return rotated;
    }
}
