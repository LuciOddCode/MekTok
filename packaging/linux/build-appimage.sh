#!/usr/bin/env bash
set -euo pipefail

PUBLISH_DIR="${1:-publish/linux-x64}"
VERSION="${2:-2.0.0}"
OUTPUT_APPIMAGE="${3:-publish/MekTok-${VERSION}-linux-x64.AppImage}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ICON_SRC="${REPO_ROOT}/src/MekTok.Desktop/Assets/mektok-app.png"

if [[ ! -f "${PUBLISH_DIR}/MekTok" ]]; then
  echo "Error: Expected published binary at '${PUBLISH_DIR}/MekTok'." >&2
  exit 1
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

APPDIR="${WORK_DIR}/MekTok.AppDir"
mkdir -p "${APPDIR}/usr/lib/mektok"
mkdir -p "${APPDIR}/usr/share/applications"
mkdir -p "${APPDIR}/usr/share/icons/hicolor/256x256/apps"

cp -a "${PUBLISH_DIR}/." "${APPDIR}/usr/lib/mektok/"
chmod 0755 "${APPDIR}/usr/lib/mektok/MekTok"

cat > "${APPDIR}/AppRun" << 'EOF'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "$0")")"
exec "${HERE}/usr/lib/mektok/MekTok" "$@"
EOF
chmod 0755 "${APPDIR}/AppRun"

cat > "${APPDIR}/mektok.desktop" << 'EOF'
[Desktop Entry]
Type=Application
Name=MekTok
Comment=Mechanical keyboard sound simulator
Exec=AppRun
Icon=mektok
Terminal=false
Categories=AudioVideo;Utility;
StartupWMClass=MekTok
EOF

cp "${APPDIR}/mektok.desktop" "${APPDIR}/usr/share/applications/mektok.desktop"
cp "${ICON_SRC}" "${APPDIR}/mektok.png"
cp "${ICON_SRC}" "${APPDIR}/.DirIcon"
cp "${ICON_SRC}" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/mektok.png"

APPIMAGETOOL="${APPIMAGETOOL_BIN:-$(command -v appimagetool || true)}"
if [[ -z "${APPIMAGETOOL}" ]]; then
  APPIMAGETOOL="${WORK_DIR}/appimagetool-x86_64.AppImage"
  curl -fsSL "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage" -o "${APPIMAGETOOL}"
  chmod +x "${APPIMAGETOOL}"
fi

mkdir -p "$(dirname "${OUTPUT_APPIMAGE}")"
ARCH=x86_64 "${APPIMAGETOOL}" --appimage-extract-and-run "${APPDIR}" "${OUTPUT_APPIMAGE}"
chmod 0755 "${OUTPUT_APPIMAGE}"
echo "Built AppImage: ${OUTPUT_APPIMAGE}"

