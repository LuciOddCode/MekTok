#!/usr/bin/env bash
set -euo pipefail

PUBLISH_DIR="${1:-publish/osx-arm64}"
VERSION="${2:-2.0.0}"
OUTPUT_ZIP="${3:-publish/MekTok-${VERSION}-osx-arm64.zip}"

BUNDLE_VERSION="${VERSION#v}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ICON_SRC="${REPO_ROOT}/src/MekTok.Desktop/Assets/mektok-app.png"

if [[ ! -f "${PUBLISH_DIR}/MekTok" ]]; then
  echo "Error: Expected published binary at '${PUBLISH_DIR}/MekTok'." >&2
  exit 1
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

APP_BUNDLE="${WORK_DIR}/MekTok.app"
mkdir -p "${APP_BUNDLE}/Contents/MacOS"
mkdir -p "${APP_BUNDLE}/Contents/Resources"

cp -a "${PUBLISH_DIR}/." "${APP_BUNDLE}/Contents/MacOS/"
chmod 0755 "${APP_BUNDLE}/Contents/MacOS/MekTok"

ICON_KEY_XML=""
if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
  ICONSET="${WORK_DIR}/MekTok.iconset"
  mkdir -p "${ICONSET}"
  sips -z 16 16     "${ICON_SRC}" --out "${ICONSET}/icon_16x16.png" >/dev/null
  sips -z 32 32     "${ICON_SRC}" --out "${ICONSET}/icon_16x16@2x.png" >/dev/null
  sips -z 32 32     "${ICON_SRC}" --out "${ICONSET}/icon_32x32.png" >/dev/null
  sips -z 64 64     "${ICON_SRC}" --out "${ICONSET}/icon_32x32@2x.png" >/dev/null
  sips -z 128 128   "${ICON_SRC}" --out "${ICONSET}/icon_128x128.png" >/dev/null
  sips -z 256 256   "${ICON_SRC}" --out "${ICONSET}/icon_128x128@2x.png" >/dev/null
  sips -z 256 256   "${ICON_SRC}" --out "${ICONSET}/icon_256x256.png" >/dev/null
  cp "${ICON_SRC}"  "${ICONSET}/icon_256x256@2x.png"
  iconutil -c icns "${ICONSET}" -o "${APP_BUNDLE}/Contents/Resources/MekTok.icns"
  ICON_KEY_XML="    <key>CFBundleIconFile</key>
    <string>MekTok.icns</string>"
else
  cp "${ICON_SRC}" "${APP_BUNDLE}/Contents/Resources/mektok.png"
fi

cat > "${APP_BUNDLE}/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>MekTok</string>
    <key>CFBundleDisplayName</key>
    <string>MekTok</string>
    <key>CFBundleIdentifier</key>
    <string>com.mektok.app</string>
    <key>CFBundleVersion</key>
    <string>${BUNDLE_VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${BUNDLE_VERSION}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>MekTok</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSInputMonitoringUsageDescription</key>
    <string>MekTok listens for global key presses and releases strictly in memory to play mechanical keyboard sounds.</string>
${ICON_KEY_XML}
</dict>
</plist>
EOF

if command -v codesign >/dev/null 2>&1; then
  SIGN_ID="${APPLE_SIGNING_IDENTITY:--}"
  if [[ "${SIGN_ID}" == "-" ]]; then
    codesign --force --deep --sign - "${APP_BUNDLE}"
  else
    codesign --force --deep --options runtime --timestamp --sign "${SIGN_ID}" "${APP_BUNDLE}"
  fi
fi

mkdir -p "$(dirname "${OUTPUT_ZIP}")"
if command -v ditto >/dev/null 2>&1; then
  ditto -c -k --keepParent "${APP_BUNDLE}" "${OUTPUT_ZIP}"
else
  (cd "${WORK_DIR}" && zip -r -y "${OUTPUT_ZIP}" "MekTok.app")
fi

echo "Built macOS app bundle archive: ${OUTPUT_ZIP}"

