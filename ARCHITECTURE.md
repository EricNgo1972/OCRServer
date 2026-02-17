# Architecture Documentation

## Design Principles

### Deterministic Processing
- Same input always produces same output
- No randomness in preprocessing pipeline
- Reproducible results for auditing

### Stateless Design
- No database dependencies
- No session state
- Each request is independent
- Horizontally scalable

### Error Resilience
- OCR failures don't crash the service
- Global exception handler catches all errors
- Meaningful error messages returned to clients
- Comprehensive logging for debugging

### Clean Separation
- Controllers: HTTP handling only
- Services: Business orchestration
- Processing: Image preprocessing algorithms
- OCR: Tesseract integration
- Models: Data contracts

## Processing Pipeline

### Image Preprocessing Flow

```
Input Image (PNG/JPG/PDF)
    ↓
[PDF] → Poppler (pdftoppm) → Page Images (PNG)
    ↓
Grayscale Conversion
    ↓
Profile Selection:
  - scan: Full pipeline
  - photo: Enhanced contrast
  - fast: Minimal processing
    ↓
CLAHE (Contrast Limited Adaptive Histogram Equalization)
    ↓
Noise Reduction (Bilateral Filter)
    ↓
Deskew Detection & Correction
    ↓
Adaptive Threshold
    ↓
Tesseract OCR (LSTM Mode)
    ↓
Text Extraction + Confidence Score
```

### Profile Details

**scan** (default):
- CLAHE clipLimit: 2.0, tileGridSize: 8x8
- Bilateral filter: d=9, sigmaColor=75, sigmaSpace=75
- Full deskew correction
- Adaptive threshold: blockSize=11, C=2

**photo**:
- CLAHE clipLimit: 3.0, tileGridSize: 8x8 (stronger)
- Same noise reduction
- Full deskew correction
- Same adaptive threshold

**fast**:
- CLAHE clipLimit: 2.0, tileGridSize: 4x4 (smaller tiles)
- No noise reduction
- No deskew
- Same adaptive threshold

## Component Responsibilities

### OcrController
- Validates HTTP request
- Handles multipart/form-data parsing
- Returns structured JSON responses
- Error handling and status codes

### OcrService
- Orchestrates entire OCR pipeline
- Handles PDF conversion to images using Poppler (pdftoppm)
- Manages page-by-page processing
- Calculates aggregate metrics (confidence, timing)

### ImagePreprocessor
- Implements preprocessing algorithms
- Profile-based processing selection
- OpenCV operations
- Memory management (Mat disposal)

### DeskewHelper
- Skew angle detection (HoughLinesP)
- Image rotation correction
- Handles edge cases (no lines found, small angles)

### TesseractRunner
- Tesseract engine initialization
- Mat to Pix conversion
- OCR execution (LSTM mode)
- Confidence score extraction

## Thread Safety

All components are designed to be thread-safe:
- Stateless services (no shared mutable state)
- TesseractEngine created per request (not reused)
- OpenCV Mat objects are not shared
- No static mutable state

## Memory Management

- OpenCV Mat objects are disposed explicitly
- Bitmaps are disposed after conversion
- Streams are properly closed
- Large files processed in chunks (PDF pages)

## Error Handling Strategy

1. **Validation Errors** (400 Bad Request):
   - Missing file
   - Invalid language code
   - Unsupported file type
   - File too large

2. **Processing Errors** (500 Internal Server Error):
   - Image decode failure
   - OCR engine failure
   - PDF conversion failure (pdftoppm errors)
   - Preprocessing errors

3. **Service Errors**:
   - Logged with full exception details
   - User-friendly error message returned
   - Service continues operating

## Performance Considerations

- Async/await for I/O operations
- Task.Run for CPU-intensive preprocessing
- Cancellation token support
- Processing time logged per request
- No blocking operations

## Extensibility Points

### Adding New OCR Engine
1. Create new runner class implementing same interface pattern
2. Update OcrService to use new engine
3. Update response model if needed

### Adding New Preprocessing Step
1. Add method to ImagePreprocessor
2. Integrate into profile pipelines
3. Update documentation

### Adding New Profile
1. Add enum value to ProcessingProfile
2. Implement profile method in ImagePreprocessor
3. Update API documentation

## Deployment Considerations

### Linux Dependencies
- Tesseract OCR 5.x
- Tesseract language packs
- OpenCV runtime libraries
- Poppler (poppler-utils package, for PDF support via pdftoppm)
- libgdiplus (for System.Drawing)

### Resource Requirements
- CPU: Multi-core recommended for parallel requests
- Memory: ~500MB base + ~50MB per concurrent request
- Disk: Tessdata files (~100MB per language)

### Scaling
- Stateless design allows horizontal scaling
- Load balancer can distribute requests
- No shared state requires coordination

## Monitoring Points

1. **Request Metrics**:
   - Processing time per request
   - File sizes processed
   - Page counts (PDFs)

2. **Quality Metrics**:
   - Confidence scores
   - Character counts extracted
   - Language distribution

3. **Error Metrics**:
   - Error rates by type
   - Failed OCR attempts
   - Invalid requests

4. **Resource Metrics**:
   - Memory usage
   - CPU utilization
   - Request queue depth
