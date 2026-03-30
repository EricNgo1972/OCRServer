# API Examples

## Example Request

### cURL

```bash
curl -X POST http://localhost:5000/api/ocr \
  -F "file=@invoice.png" \
  -F "language=eng+vie" \
  -F "profile=scan"
```

If `language` is omitted from the multipart form body, the API defaults to `eng+fra`.

### PowerShell

```powershell
$uri = "http://localhost:5000/api/ocr"
$form = @{
    file = Get-Item "invoice.png"
    language = "eng+vie"
    profile = "scan"
}
$response = Invoke-RestMethod -Uri $uri -Method Post -Form $form
$response | ConvertTo-Json -Depth 10
```

### C# Client

```csharp
using System.Net.Http.Json;
using OCRServer.Models;

var client = new HttpClient();
var formData = new MultipartFormDataContent();

// Add file
var fileContent = new ByteArrayContent(File.ReadAllBytes("invoice.png"));
fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
formData.Add(fileContent, "file", "invoice.png");

// Add language
formData.Add(new StringContent("eng+vie"), "language");

// Add profile (optional)
formData.Add(new StringContent("scan"), "profile");

var response = await client.PostAsync("http://localhost:5000/api/ocr", formData);
var result = await response.Content.ReadFromJsonAsync<OcrResponse>();

Console.WriteLine($"Confidence: {result.Confidence}");
Console.WriteLine($"Processing time: {result.ProcessingMs}ms");
foreach (var page in result.Pages)
{
    Console.WriteLine($"Page {page.Page}: {page.Text.Substring(0, Math.Min(100, page.Text.Length))}...");
}
```

### Python (requests)

```python
import requests

url = "http://localhost:5000/api/ocr"
files = {"file": open("invoice.png", "rb")}
data = {
    "language": "eng+vie",
    "profile": "scan"
}

response = requests.post(url, files=files, data=data)
result = response.json()

print(f"Confidence: {result['confidence']}")
print(f"Processing time: {result['processingMs']}ms")
for page in result['pages']:
    print(f"Page {page['page']}: {page['text'][:100]}...")
```

## Example Response

### Success Response (200 OK)

```json
{
  "engine": "tesseract-5.x",
  "language": "eng+vie",
  "profile": "scan",
  "confidence": 0.92,
  "pages": [
    {
      "page": 1,
      "text": "INVOICE\nSố hóa đơn: 00123\nDate: 2024-01-15\nCustomer: ABC Company\n\nItem\tQuantity\tPrice\nProduct A\t10\t$100.00\nProduct B\t5\t$50.00\n\nTotal: $1,250.00"
    }
  ],
  "processingMs": 640
}
```

### Multi-page PDF Response

```json
{
  "engine": "tesseract-5.x",
  "language": "eng",
  "profile": "scan",
  "confidence": 0.88,
  "pages": [
    {
      "page": 1,
      "text": "Page 1 content..."
    },
    {
      "page": 2,
      "text": "Page 2 content..."
    },
    {
      "page": 3,
      "text": "Page 3 content..."
    }
  ],
  "processingMs": 2150
}
```

### Error Response (400 Bad Request)

```json
{
  "error": "Invalid language format: eng/fra. Expected format: 'eng', 'eng+fra', etc.",
  "statusCode": 400
}
```

### Error Response (500 Internal Server Error)

```json
{
  "error": "OCR processing failed. Please check the logs for details.",
  "statusCode": 500
}
```

## Language Codes

Common Tesseract language codes:
- `eng` - English
- `fra` - French
- `vie` - Vietnamese
- `spa` - Spanish
- `deu` - German
- `jpn` - Japanese
- `chi_sim` - Chinese (Simplified)
- `chi_tra` - Chinese (Traditional)

Multiple languages can be combined with `+`:
- `eng+fra` - English and French
- `eng+vie` - English and Vietnamese
- `eng+fra+vie` - English, French, and Vietnamese

## Processing Profiles

- **scan**: Full preprocessing pipeline (best for scanned documents)
  - CLAHE contrast normalization
  - Noise reduction
  - Deskew correction
  - Adaptive threshold

- **photo**: Stronger preprocessing (best for camera-captured images)
  - Enhanced CLAHE (clipLimit: 3.0)
  - Noise reduction
  - Deskew correction
  - Adaptive threshold

- **fast**: Minimal preprocessing (fastest, lower quality)
  - Basic CLAHE
  - Simple threshold
