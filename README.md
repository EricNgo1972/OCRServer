# OCR Server

Production-grade OCR microservice for ERP systems. Built with .NET 8 and Tesseract OCR.

## Design Philosophy

- **Deterministic**: Same input produces same output
- **Auditable**: Clear logging and processing metrics
- **ERP-safe**: Stateless, thread-safe, error-resilient
- **Replaceable**: Clean separation allows swapping OCR engines

## Technology Stack

- .NET 8 Web API
- **Linux**: Poppler (`pdftoppm`) + Tesseract CLI — no OpenCV, no PDFium
- **Windows**: PDFium + OpenCvSharp preprocessing + Tesseract
- CPU-only, offline

## Features

- **Multilingual OCR**: Language combinations (e.g. `eng+vie`, `eng+fra`)
- **PDF support**: Renders PDF pages to images (pdftoppm on Linux, PDFium on Windows)
- **Processing profiles** (Windows only): `scan`, `photo`, `fast`
- **Internal security**: API key auth, per-key rate and concurrency limits, size/page validation
- **Operations dashboard**: Public `/dashboard` page with live in-memory request totals
- **Swagger UI**: Public `/swagger` page with `X-API-Key` header support for testing
- **Stateless**: No database, no session state

## API Endpoint

Public utility routes:
- `GET /` redirects to `/dashboard`
- `GET /dashboard` shows request counters
- `GET /dashboard/stats` returns the dashboard totals as JSON
- `GET /swagger` opens the Swagger UI

### POST /api/Ocr

**Headers**: `X-API-Key: <key>` (required)

**Content-Type**: `multipart/form-data`

**Form fields**:
- `file` (required): PDF, PNG, or JPG file
- `language` (optional): Tesseract code(s), e.g. `eng`, `eng+fra`, `eng+vie`. Defaults to `eng+fra` when omitted
- `profile` (optional): Preprocessing profile — `scan`, `photo`, `fast` (Windows only; default `scan`)

**Response**:
```json
{
  "engine": "tesseract-5.x",
  "language": "eng+vie",
  "profile": "scan",
  "confidence": 0.92,
  "pages": [
    { "page": 1, "text": "INVOICE\nSố hóa đơn: 00123\n..." }
  ],
  "processingMs": 640
}
```

### POST /api/Ocr/pdf

Creates a searchable PDF from an uploaded PDF.

**Headers**: `X-API-Key: <key>` (required)

**Content-Type**: `multipart/form-data`

**Form fields**:
- `file` (required): PDF file
- `language` (optional): Tesseract code(s), e.g. `eng`, `eng+fra`, `eng+vie`. Defaults to `eng+fra` when omitted
- `profile` (optional): accepted for compatibility; ignored by searchable PDF generation

**Response**:
- `Content-Type: application/pdf`
- body: binary PDF stream
- filename: returned via `Content-Disposition`

## Internal Security (API Keys, Rate Limiting, Concurrency)

Request pipeline order:

**Request → API Key → Rate limit (per key) → Concurrency limit (per key) → Validation (size/pages) → OCR**

- **X-API-Key** header required. Missing → 401; Invalid → 403
- **429 Too Many Requests**: rate or concurrency limit exceeded (no queuing)
- **413 Payload Too Large**: file size or PDF page count exceeds limits

Configure in `appsettings.json` (loaded once at startup):

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
    }
  }
}
```

Every OCR request is logged: `OCR | client=warehouse-app | pages=4 | size=2.3MB | ms=1820 | status=200 | ok`

## Deployment

This service has different runtime dependencies on Windows and Linux.

- **Windows**:
  - `/api/ocr` uses PDFium + OpenCvSharp + the .NET Tesseract wrapper
  - `/api/ocr/pdf` uses `tesseract.exe` CLI to generate searchable PDFs
- **Linux**:
  - `/api/ocr` and `/api/ocr/pdf` use Poppler tools + Tesseract CLI

### Windows Deployment

Required components:

- .NET 8 runtime or SDK
- Tesseract language data (`tessdata/`) available either:
  - bundled with the app output, or
  - installed system-wide and configured via `Ocr:TesseractDataPath`
- For `POST /api/ocr/pdf`: `tesseract.exe` must be installed and launchable by the app

Recommended Tesseract install path:

```text
C:\Program Files\Tesseract-OCR\tesseract.exe
```

Build and run:

```powershell
dotnet restore
dotnet build OCRServer.csproj -c Release
dotnet run --project OCRServer.csproj
```

Quick verification:

```powershell
where.exe tesseract
```

If `where.exe tesseract` returns no path, `/api/ocr/pdf` will fail even if `/api/ocr` works.

### Linux Deployment

Required components:

- .NET 8 runtime or SDK
- `tesseract-ocr`
- `poppler-utils`
- `libtesseract-dev`
- `libleptonica-dev`
- optional system language packs if you are not using the bundled `tessdata/`

Install on Ubuntu/Debian:

```bash
sudo apt-get update
sudo apt-get install -y poppler-utils tesseract-ocr libtesseract-dev libleptonica-dev
```

Or from project root:

```bash
bash scripts/setup-wsl-deps.sh
```

What these provide:

- `poppler-utils`: `pdftoppm`, `pdftotext`, `pdfinfo`
- `tesseract-ocr`: Tesseract CLI used for OCR and searchable PDF generation
- `libtesseract-dev` / `libleptonica-dev`: native runtime libraries required by the app

Build and run:

```bash
dotnet restore
dotnet build OCRServer.csproj -c Release -r linux-x64
dotnet run --project OCRServer.csproj
```

Quick verification:

```bash
which tesseract
which pdftoppm
which pdftotext
which pdfinfo
```

### WSL Development

On Linux/WSL the runtime pipeline is **pdftoppm → Tesseract CLI**.

Run from Windows:

```powershell
.\scripts\run-wsl.ps1
```

Run from inside WSL:

```bash
cd "/mnt/c/Business Solutions/OCR Server"
dotnet run
```

Visual Studio 2022:

1. Build for Linux: `dotnet build OCRServer.csproj -r linux-x64`
2. Set launch profile to **WSL**, then F5 (or Ctrl+F5 to avoid rebuild)

## Configuration

`appsettings.json`:

```json
{
  "Ocr": {
    "TesseractDataPath": "/usr/share/tesseract-ocr/5/tessdata",
    "DefaultLanguage": "eng"
  },
  "OcrLimits": { "MaxUploadBytes": 20971520, "MaxPdfPages": 30 },
  "ApiKeys": { ... }
}
```

Use bundled tessdata by placing `.traineddata` files in the project `tessdata/` folder (copied to output).

## Example Usage

### cURL

```bash
curl -X POST http://localhost:5000/api/Ocr \
  -H "X-API-Key: your-key" \
  -F "file=@invoice.png" \
  -F "language=eng+vie" \
  -F "profile=scan"
```

### PowerShell

```powershell
$uri = "http://localhost:5000/api/Ocr"
$form = @{
    file = Get-Item "invoice.png"
    language = "eng+vie"
    profile = "scan"
}
$headers = @{ "X-API-Key" = "your-key" }
Invoke-RestMethod -Uri $uri -Method Post -Form $form -Headers $headers
```

### Searchable PDF Download

```powershell
$uri = "http://localhost:5000/api/ocr/pdf"
$form = @{
    file = Get-Item "document.pdf"
    language = "eng"
}
$headers = @{ "X-API-Key" = "your-key" }

Invoke-WebRequest -Uri $uri -Method Post -Form $form -Headers $headers -OutFile "searchable.pdf"
```

## Docker (Linux target)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

RUN apt-get update && \
    apt-get install -y poppler-utils tesseract-ocr tesseract-ocr-eng libtesseract-dev libleptonica-dev && \
    rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["OCRServer.csproj", "./"]
RUN dotnet restore "OCRServer.csproj"
COPY . .
RUN dotnet build "OCRServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OCRServer.csproj" -c Release -o /app/publish -r linux-x64 --self-contained false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OCRServer.dll"]
```

```bash
docker build -t ocr-server .
docker run -p 8080:80 -e ApiKeys__dev__Key=dev-key ocr-server
```

## Systemd Service

Example `/etc/systemd/system/ocr-server.service`:

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

```bash
sudo systemctl enable ocr-server
sudo systemctl start ocr-server
```

## Architecture

```
/Controllers
  OcrController.cs              # POST /api/Ocr
/Services
  IOcrService.cs                # Service interface
  WindowsOcrService.cs          # Windows: PDFium + OpenCV + Tesseract
  LinuxOcrService.cs           # Linux: pdftoppm + Tesseract CLI
  PdftoppmPdfRenderer.cs        # Linux PDF → images (pdftoppm)
  PdfInfoPageCounter.cs         # PDF page count (pdfinfo)
/Security
  ApiKeyStore.cs                # API keys loaded at startup
/Middleware
  ApiKeyAuthenticationMiddleware.cs
  OcrConcurrencyLimiterMiddleware.cs
  OcrRequestValidationMiddleware.cs
  OcrRequestAuditMiddleware.cs
  GlobalExceptionHandler.cs
/Ocr
  TesseractRunner.cs            # Tesseract (CLI on Linux, wrapper on Windows)
/Processing (Windows only)
  ImagePreprocessor.cs, DeskewHelper.cs
/Models
  OcrRequest, OcrResponse, PageResult
```

## Troubleshooting

### "pdftoppm: command not found" or "tesseract: command not found" (Linux)

Install Poppler and Tesseract: `sudo apt-get install -y poppler-utils tesseract-ocr libtesseract-dev libleptonica-dev`. On Ubuntu 24.04+, run `bash scripts/setup-wsl-deps.sh` to add the `libtesseract.so.4` symlink.

### "libtesseract.so.4: cannot open shared object file" (Linux)

Install `libtesseract-dev` and `libleptonica-dev`. On Ubuntu 24.04+ (Tesseract 5.x), the setup script creates `libtesseract.so.4` → `libtesseract.so.5`.

### Wrong build for environment

- **WSL/Linux**: Use the **linux-x64** build. Do not run `win-x64` output in WSL. Build with `dotnet build OCRServer.csproj -r linux-x64`.
- **Windows**: Use the default **win-x64** build (`dotnet build` / `dotnet run`).

### Tesseract / tessdata

- Set `Ocr:TesseractDataPath` in appsettings to a folder containing `.traineddata` files, or use the bundled `tessdata/` folder in the project.
- Ensure the requested language (e.g. `eng`, `fra`) has a corresponding `.traineddata` file.

### PDF processing (Linux)

- `pdftoppm` and `pdfinfo` come from **poppler-utils**. Verify: `which pdftoppm pdfinfo`.
- Ensure temp directory is writable (PDF conversion uses temp files).

## Limitations

- CPU-only (no GPU)
- Offline only (no cloud APIs)
- Stateless (no session management)

## License

Infrastructure service — no license restrictions specified.
