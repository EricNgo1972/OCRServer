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

## Running in WSL

On Linux/WSL, the OCR pipeline is intentionally **boring and stable**:

- PDF → `pdftoppm` (Poppler) at **300 DPI**, grayscale
- Image → Tesseract directly

No OpenCV and no PDFium are used on the Linux OCR path.

**Note:** Building a *solution* with `-r linux-x64` is not supported (error NETSDK1134). Build the *project* instead: run **`.\scripts\build-wsl.ps1`** or **`dotnet build OCRServer.csproj -r linux-x64`**.

### WSL: install system libraries first

In WSL (Ubuntu/Debian), install these **before** running the app so `pdftoppm` and `libtesseract.so.4` can load. From the project root in WSL:

```bash
bash scripts/setup-wsl-deps.sh
```

Or install manually:

```bash
sudo apt-get update
sudo apt-get install -y \
  poppler-utils \
  libtesseract-dev \
  libleptonica-dev \
  tesseract-ocr
```

- **poppler-utils**: provides `pdftoppm` (PDF → per-page images).
- **libtesseract-dev** / **libleptonica-dev**: required by the Tesseract .NET library. On Ubuntu 24.04+ (Tesseract 5.x), the setup script creates a `libtesseract.so.4` symlink for compatibility.
- Language packs (`tesseract-ocr-eng`, etc.) are optional if you use bundled tessdata in the app folder.

### Option A: Run from Windows (Cursor / VS Code)

1. **Build for Linux and run in WSL** using the script (recommended):
   ```powershell
   .\scripts\run-wsl.ps1
   ```
2. Or use **Run and Debug** → select **"OCR Server (WSL)"** → F5. This builds for `linux-x64` and starts the app inside WSL.
3. Or run the task: **Terminal → Run Task → "run in WSL"**.

### Option B: Run from inside WSL

1. Open a **WSL terminal** and go to the project folder (e.g. `cd "/mnt/c/Business Solutions/OCR Server"`).
2. Restore and run (the build will target `linux-x64` automatically):
   ```bash
   dotnet restore
   dotnet run
   ```

### Option C: Open project in WSL (best for debugging in WSL)

1. In Cursor/VS Code: **Remote: Open Folder in WSL** and open this repo (e.g. `\\wsl$\Ubuntu\home\...` or the path under `/mnt/c/...`).
2. Then **Run → Start Debugging** or `dotnet run` from the terminal. Build and run both happen in WSL with the correct Linux binaries.

### Option D: Visual Studio 2022

The solution only has **Debug** and **Release** (SDK-style projects don't support extra configurations). To run in WSL from VS 2022:

1. **Build for Linux first**: Open **Developer PowerShell** or a terminal in the solution folder and run:
   ```powershell
   dotnet build OCRServer.csproj -r linux-x64
   ```
2. In **Visual Studio 2022**, set the **launch profile** to **WSL** (dropdown next to the green Run button).
3. Press **F5**. Use **Run Without Debugging** (Ctrl+F5) so VS doesn't rebuild; it will use the existing `bin\Debug\net8.0\linux-x64\` output and run it in WSL with the correct native libraries.

Alternatively, run **`.\scripts\run-wsl.ps1`** from the project folder to build for linux-x64 and start the app in WSL in one step.

## Troubleshooting

## Internal security (API keys, rate limiting, concurrency)

This is an internal service, but OCR is expensive. The request pipeline is protected in this order:

Request → **API Key** → **Rate limit (per key)** → **Concurrency limit (per key)** → **Validation (size/pages)** → OCR

### API Key header (mandatory)

Every OCR request must include:

- `X-API-Key: <key>`

Missing key → **401**  
Invalid key → **403**

### Configuration

Configure API keys and limits in `appsettings.json` (loaded once at startup, stored in-memory):

```json
{
  "OcrLimits": {
    "MaxUploadBytes": 20971520,
    "MaxPdfPages": 30
  },
  "ApiKeys": {
    "warehouse-app": {
      "Key": "abc123",
      "RequestsPerMinute": 30,
      "MaxConcurrent": 2
    },
    "finance-app": {
      "Key": "xyz789",
      "RequestsPerMinute": 10,
      "MaxConcurrent": 1
    }
  }
}
```

### Rejection behavior

- **429 Too Many Requests**: rate limit exceeded OR concurrency limit exceeded (no queuing)
- **413 Payload Too Large**: file too large OR PDF has too many pages

### Logging

Every OCR request produces one log line like:

`OCR | client=warehouse-app | pages=4 | size=2.3MB | ms=1820 | status=200 | ok`

### "pdftoppm: command not found" or "libtesseract.so.4: cannot open shared object file" (WSL / Linux)

You are using the **linux-x64** build but the **system libraries** are missing. In WSL (or your Linux distro), install them (see **WSL: install system libraries first** under Running in WSL):

```bash
sudo apt-get install -y poppler-utils libtesseract-dev libleptonica-dev
```

Then rebuild if needed and run again. On Ubuntu 24.04+, run **`bash scripts/setup-wsl-deps.sh`** once so it can create a `libtesseract.so.4` symlink (the system ships `libtesseract.so.5`).

### "The type initializer for 'OpenCvSharp.Internal.NativeMethods' threw an exception" or "Unable to load shared library 'pdfium'"

You are running the **wrong build** for the current environment:

- **If you're in WSL (or using the WSL launch profile):** The process must use the **Linux** build (`linux-x64`). Do **not** run the contents of `bin/Debug/net8.0/win-x64/` in WSL. Instead:
  1. Build for Linux: `dotnet build OCRServer.csproj -r linux-x64`
  2. Run from a WSL shell: `dotnet bin/Debug/net8.0/linux-x64/OCRServer.dll` (from the project folder), or use **Run Without Debugging** (Ctrl+F5) with the WSL profile after building with the command above so VS doesn't rebuild.
- **If you're on Windows:** Use the **Windows** build. The project is set up to build `win-x64` by default on Windows, so output is in `bin\Debug\net8.0\win-x64\`. Do a clean rebuild: `dotnet clean` then `dotnet build`, and run from Visual Studio (http/https profile) or `dotnet run`. Ensure you're not pointing the run at a `linux-x64` folder.

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
