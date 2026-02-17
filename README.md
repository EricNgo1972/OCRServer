# OCR Server

Production-grade OCR microservice for ERP systems. Built with .NET 8, OpenCV, and Tesseract OCR.

## Design Philosophy

- **Deterministic**: Same input produces same output
- **Auditable**: Clear logging and processing metrics
- **ERP-safe**: Stateless, thread-safe, error-resilient
- **Replaceable**: Clean separation allows swapping OCR engines

## Technology Stack

- .NET 8 Web API
- OpenCV (via OpenCvSharp) for image preprocessing
- Tesseract OCR 5.x (LSTM mode, offline)
- PdfiumSharp for PDF rendering
- Linux-compatible, CPU-only

## Features

- **Image preprocessing pipeline**: Grayscale → CLAHE → Noise Reduction → Deskew → Adaptive Threshold
- **Multilingual OCR**: Supports language combinations (e.g., `eng+vie`, `eng+fra`)
- **PDF support**: Converts PDF pages to images automatically
- **Processing profiles**: `scan`, `photo`, `fast`
- **Stateless**: No database, no session state
- **Error resilient**: Failures don't crash the service

## API Endpoint

### POST /api/ocr

**Content-Type**: `multipart/form-data`

**Parameters**:
- `file` (required): PDF, PNG, or JPG file
- `language` (required): Tesseract language code(s), e.g., `eng`, `eng+fra`, `eng+vie`
- `profile` (optional): Preprocessing profile (`scan`, `photo`, `fast`). Default: `scan`

**Response**:
```json
{
  "engine": "tesseract-5.x",
  "language": "eng+vie",
  "profile": "scan",
  "confidence": 0.92,
  "pages": [
    {
      "page": 1,
      "text": "INVOICE\nSố hóa đơn: 00123\n..."
    }
  ],
  "processingMs": 640
}
```

## Linux Dependencies

### Ubuntu/Debian

```bash
# Install Tesseract OCR
sudo apt-get update
sudo apt-get install -y tesseract-ocr tesseract-ocr-eng tesseract-ocr-fra tesseract-ocr-vie

# Install OpenCV dependencies
sudo apt-get install -y libopencv-dev libgdiplus

# Install Poppler (for PDF processing)
sudo apt-get install -y poppler-utils

# Install additional dependencies
sudo apt-get install -y libc6-dev
```

### CentOS/RHEL

```bash
# Install Tesseract OCR
sudo yum install -y tesseract tesseract-langpack-eng tesseract-langpack-fra

# Install OpenCV dependencies
sudo yum install -y opencv-devel libgdiplus

# Install Poppler (for PDF processing)
sudo yum install -y poppler-utils
```

### Tesseract Language Packs

Install language packs as needed:
- `tesseract-ocr-eng` - English
- `tesseract-ocr-fra` - French
- `tesseract-ocr-vie` - Vietnamese
- `tesseract-ocr-spa` - Spanish
- `tesseract-ocr-deu` - German
- See [Tesseract language packs](https://github.com/tesseract-ocr/tessdata) for full list

**Tessdata Location**: `/usr/share/tesseract-ocr/5/tessdata` (configure in `appsettings.json`)

## Building

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build --configuration Release

# Run
dotnet run
```

## Docker Deployment

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Install Tesseract and dependencies
RUN apt-get update && \
    apt-get install -y tesseract-ocr tesseract-ocr-eng tesseract-ocr-fra tesseract-ocr-vie libgdiplus && \
    rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["OCRServer.csproj", "./"]
RUN dotnet restore "OCRServer.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "OCRServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OCRServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OCRServer.dll"]
```

### Build and Run

```bash
docker build -t ocr-server .
docker run -p 8080:80 ocr-server
```

## Systemd Service

Create `/etc/systemd/system/ocr-server.service`:

```ini
[Unit]
Description=OCR Server
After=network.target

[Service]
Type=notify
ExecStart=/usr/bin/dotnet /opt/ocr-server/OCRServer.dll
Restart=always
RestartSec=10
SyslogIdentifier=ocr-server
User=ocr
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl enable ocr-server
sudo systemctl start ocr-server
sudo systemctl status ocr-server
```

## Configuration

Edit `appsettings.json`:

```json
{
  "Ocr": {
    "TesseractDataPath": "/usr/share/tesseract-ocr/5/tessdata",
    "DefaultLanguage": "eng"
  }
}
```

## Example Usage

### cURL

```bash
curl -X POST http://localhost:5000/api/ocr \
  -F "file=@invoice.png" \
  -F "language=eng+vie" \
  -F "profile=scan"
```

### PowerShell

```powershell
$uri = "http://localhost:5000/api/ocr"
$form = @{
    file = Get-Item "invoice.png"
    language = "eng+vie"
    profile = "scan"
}
Invoke-RestMethod -Uri $uri -Method Post -Form $form
```

### C# Client

```csharp
using var client = new HttpClient();
using var formData = new MultipartFormDataContent();
formData.Add(new ByteArrayContent(File.ReadAllBytes("invoice.png")), "file", "invoice.png");
formData.Add(new StringContent("eng+vie"), "language");
formData.Add(new StringContent("scan"), "profile");

var response = await client.PostAsync("http://localhost:5000/api/ocr", formData);
var result = await response.Content.ReadFromJsonAsync<OcrResponse>();
```

## Processing Profiles

- **scan**: Full preprocessing pipeline (CLAHE, noise reduction, deskew, adaptive threshold)
- **photo**: Stronger contrast normalization + deskew (for camera-captured images)
- **fast**: Minimal preprocessing (grayscale + basic threshold)

## Architecture

```
/Controllers
  OcrController.cs          # API endpoint
/Services
  IOcrService.cs            # Service interface
  OcrService.cs             # Main OCR orchestration
/Processing
  ImagePreprocessor.cs      # Preprocessing pipeline
  DeskewHelper.cs           # Skew detection/correction
/Ocr
  TesseractRunner.cs        # Tesseract wrapper
/Models
  OcrRequest.cs             # Request model
  OcrResponse.cs            # Response model
  PageResult.cs             # Page result model
/Middleware
  GlobalExceptionHandler.cs # Error handling
```

## Logging

The service logs:
- Processing time per request
- File names and page counts
- Confidence scores
- Errors (without crashing)

View logs:
```bash
# systemd
journalctl -u ocr-server -f

# Docker
docker logs -f ocr-server
```

## Limitations

- CPU-only (no GPU acceleration)
- Offline only (no cloud APIs)
- No document classification
- No business logic
- Stateless (no session management)

## Troubleshooting

### Tesseract not found
- Verify `TesseractDataPath` in `appsettings.json`
- Check language packs are installed
- Ensure tessdata files exist at configured path

### OpenCV errors
- Install `libopencv-dev` and runtime packages
- Verify OpenCvSharp NuGet package matches system OpenCV version

### PDF processing fails
- Ensure `poppler-utils` package is installed (`pdftoppm` command must be available)
- Verify `pdftoppm` is in PATH: `which pdftoppm`
- Check PDF file is not corrupted
- Verify sufficient memory for large PDFs
- Check temporary directory permissions (PDF conversion uses temp files)

## License

Infrastructure service - no license restrictions specified.
