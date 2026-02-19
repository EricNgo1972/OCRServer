#!/usr/bin/env bash
# Deploy OCR Server to a Linux host via rsync and restart the service.
#
# Target server must have installed:
#   poppler-utils (pdftoppm, pdftotext)
#   tesseract-ocr
#   libtesseract-dev
#   libleptonica-dev
#
# Example (Debian/Ubuntu):
#   sudo apt-get install -y poppler-utils tesseract-ocr libtesseract-dev libleptonica-dev

set -euo pipefail

SOURCE="/mnt/c/Business Solutions/OCR Server/bin/Release/net8.0/linux/publish/"
TARGET_USER="eric"
TARGET_HOST="192.168.2.4"
TARGET_PATH="/var/www/ocr/app"
SERVICE_NAME="ocr.service"   # <-- change if your service name differs

echo "🚀 Deploying OCR Server to ${TARGET_HOST}..."

rsync -rz --delete \
  --no-perms \
  --no-times \
  --no-group \
  --omit-dir-times \
  "$SOURCE" \
  "${TARGET_USER}@${TARGET_HOST}:${TARGET_PATH}"

echo "♻️ Restarting service: ${SERVICE_NAME}..."

ssh "${TARGET_USER}@${TARGET_HOST}" <<EOF
  set -e
  sudo systemctl daemon-reload
  sudo systemctl restart ${SERVICE_NAME}
  sudo systemctl --no-pager --full status ${SERVICE_NAME}
EOF

echo "✅ Deployment complete."
