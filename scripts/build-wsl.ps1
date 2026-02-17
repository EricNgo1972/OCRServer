# Build the project for Linux (WSL). Use this instead of "dotnet build -r linux-x64"
# when you're in the solution folder (building the .sln with -r is not supported).
Set-Location $PSScriptRoot\..
dotnet build OCRServer.csproj -r linux-x64
