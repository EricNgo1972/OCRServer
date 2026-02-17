# Build for Linux (linux-x64) then run the app inside WSL.
# Use this when you want to run/debug in WSL from a Windows project folder.
# The app will load Linux native libs (libpdfium.so, OpenCvSharp) correctly.
#
# If you see "OpenCvSharpExtern.so" or "libtesseract.so.4" not found, install WSL deps once:
#   wsl -e bash -c "cd '/mnt/c/Business Solutions/OCR Server' && bash scripts/setup-wsl-deps.sh"
$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot | Split-Path -Parent
Push-Location $projectRoot

try {
    Write-Host "Building for linux-x64..." -ForegroundColor Cyan
    dotnet build .\OCRServer.csproj -r linux-x64 --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }

    $outDir = Join-Path $projectRoot "bin\Debug\net8.0\linux-x64"

    # Linux pipeline no longer relies on OpenCvSharp; no extra native copying is required here.

    # Convert Windows path to WSL path (e.g. C:\foo\bar -> /mnt/c/foo/bar)
    $winPath = (Get-Location).Path
    $drive = $winPath.Substring(0, 1).ToLower()
    $rest = $winPath.Substring(2).Replace("\", "/")
    $wslPath = "/mnt/$drive$rest"
    $dllPath = "$wslPath/bin/Debug/net8.0/linux-x64/OCRServer.dll"

    Write-Host "Starting app in WSL: $dllPath" -ForegroundColor Cyan
    wsl -e bash -c "export ASPNETCORE_URLS='https://localhost:5001;http://localhost:5000' && export ASPNETCORE_ENVIRONMENT=Development && dotnet '$dllPath'"
} finally {
    Pop-Location
}
