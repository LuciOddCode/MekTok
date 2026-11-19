#!/usr/bin/env bash
set -euo pipefail

PUBLISH_DIR="${1:-publish/linux-x64}"
VERSION="${2:-2.0.0}"
OUTPUT_DEB="${3:-publish/MekTok-${VERSION}-linux-x64.deb}"
ARCH="${4:-amd64}"

# Debian versions must start with a digit and exclude leading 'v'
DEB_VERSION="${VERSION#v}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ICON_SRC="${REPO_ROOT}/src/MekTok.Desktop/Assets/mektok-app.png"

if [[ ! -f "${PUBLISH_DIR}/MekTok" ]]; then
  echo "Error: Expected published binary at '${PUBLISH_DIR}/MekTok'." >&2
  exit 1
fi

STAGE_DIR="$(mktemp -d)"
trap 'rm -rf "${STAGE_DIR}"' EXIT

mkdir -p "${STAGE_DIR}/DEBIAN"
mkdir -p "${STAGE_DIR}/usr/lib/mektok"
mkdir -p "${STAGE_DIR}/usr/bin"
mkdir -p "${STAGE_DIR}/usr/share/applications"
mkdir -p "${STAGE_DIR}/usr/share/icons/hicolor/256x256/apps"

cp -a "${PUBLISH_DIR}/." "${STAGE_DIR}/usr/lib/mektok/"
find "${STAGE_DIR}" -type d -exec chmod 0755 {} +
chmod 0755 "${STAGE_DIR}/usr/lib/mektok/MekTok"

cat > "${STAGE_DIR}/usr/bin/mektok" << 'EOF'
#!/usr/bin/env bash
exec /usr/lib/mektok/MekTok "$@"
EOF
chmod 0755 "${STAGE_DIR}/usr/bin/mektok"

cat > "${STAGE_DIR}/usr/share/applications/mektok.desktop" << 'EOF'
[Desktop Entry]
Type=Application
Version=1.0
Name=MekTok
GenericName=Mechanical Keyboard Sound Simulator
Comment=Play realistic mechanical keyboard switch sounds as you type
Exec=/usr/bin/mektok
Icon=mektok
Terminal=false
Categories=AudioVideo;Utility;
Keywords=keyboard;mechanical;sound;switches;
StartupNotify=true
StartupWMClass=MekTok
EOF

cp "${ICON_SRC}" "${STAGE_DIR}/usr/share/icons/hicolor/256x256/apps/mektok.png"

INSTALLED_KB="$(du -sk "${STAGE_DIR}/usr" | awk '{print $1}')"

cat > "${STAGE_DIR}/DEBIAN/control" << EOF
Package: mektok
Version: ${DEB_VERSION}
Section: utils
Priority: optional
Architecture: ${ARCH}
Installed-Size: ${INSTALLED_KB}
Maintainer: MekTok Contributors <noreply@github.com>
Depends: libx11-6, libxi6, libxtst6, libxrandr2, libxt6, libxkbcommon0, libfontconfig1, libpulse0 | libpipewire-0.3-0 | libasound2t64 | libasound2
Homepage: https://github.com/LuciOddCode/MekTok
Description: Low-latency mechanical keyboard sound simulator
 MekTok plays physically-modeled mechanical switch sounds or imported
 Mechvibes packs in real time as you type, with per-key customization,
 stereo panning, custom chord sound effects, and smart app auto-pause.
EOF

mkdir -p "$(dirname "${OUTPUT_DEB}")"
chmod 0644 "${STAGE_DIR}/DEBIAN/control" "${STAGE_DIR}/usr/share/applications/mektok.desktop" "${STAGE_DIR}/usr/share/icons/hicolor/256x256/apps/mektok.png"
dpkg-deb --root-owner-group --build "${STAGE_DIR}" "${OUTPUT_DEB}"
echo "Built Debian package: ${OUTPUT_DEB}"
