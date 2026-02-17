FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Install Tesseract OCR, Poppler (for PDF processing), and dependencies
RUN apt-get update && \
    apt-get install -y \
    tesseract-ocr \
    tesseract-ocr-eng \
    tesseract-ocr-fra \
    tesseract-ocr-vie \
    poppler-utils \
    libgdiplus \
    libc6-dev \
    && rm -rf /var/lib/apt/lists/*

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
