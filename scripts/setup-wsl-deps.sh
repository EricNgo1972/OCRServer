#!/usr/bin/env bash
# Run this once in WSL (Ubuntu/Debian) for OCR Server: pdftotext, pdftoppm, Tesseract.
# Usage: from project root in WSL: bash scripts/setup-wsl-deps.sh
# Or: wsl -e bash -c "cd '/mnt/c/Business Solutions/OCR Server' && bash scripts/setup-wsl-deps.sh"

set -e
echo "Installing WSL system dependencies for OCR Server (Ubuntu/Debian)..."
if ! command -v apt-get >/dev/null 2>&1; then
  echo "This script expects apt-get (Ubuntu/Debian). For other distros, install: poppler-utils, tesseract, leptonica."
  exit 1
fi
sudo apt-get update
# poppler-utils: pdftotext (text-layer extraction), pdftoppm (PDF → images for OCR)
# tesseract-ocr + libtesseract-dev/libleptonica-dev: Tesseract CLI and libs
sudo apt-get install -y poppler-utils tesseract-ocr libtesseract-dev libleptonica-dev

# Ubuntu 24.04+ ships Tesseract 5 (libtesseract.so.5). Tesseract .NET may look for libtesseract.so.4.
TESS_LIB=$(ldconfig -p 2>/dev/null | awk '/libtesseract\.so/ { print $NF; exit }')
if [ -n "$TESS_LIB" ] && [ -e "$TESS_LIB" ]; then
  TESS_DIR=$(dirname "$TESS_LIB")
  TESS_NAME=$(basename "$TESS_LIB")
  if [ "$TESS_NAME" != "libtesseract.so.4" ] && [ ! -e "$TESS_DIR/libtesseract.so.4" ]; then
    echo "Creating libtesseract.so.4 -> $TESS_NAME for compatibility."
    sudo ln -sf "$TESS_NAME" "$TESS_DIR/libtesseract.so.4"
  fi
fi

echo "Done. You can run the app in WSL now."
