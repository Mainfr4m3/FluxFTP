#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
out="$root/dist/linux-preview-0.1.0-preview.3"
stage=$(mktemp -d "${TMPDIR:-/tmp}/fluxftp-package.XXXXXXXX")
mkdir -p "$stage/DEBIAN" "$stage/opt/fluxftp-linux-preview" "$stage/usr/bin" "$stage/usr/share/applications" "$stage/usr/share/icons/hicolor/256x256/apps"
cp -a "$out/publish/." "$stage/opt/fluxftp-linux-preview/"
chmod 755 "$stage/opt/fluxftp-linux-preview/FluxFTP.Linux"
cat > "$stage/usr/bin/fluxftp-linux-preview" <<'EOF'
#!/bin/sh
exec /opt/fluxftp-linux-preview/FluxFTP.Linux "$@"
EOF
chmod 755 "$stage/usr/bin/fluxftp-linux-preview"
cp "$root/src/IoFtp.Desktop/Assets/fluxftp-server-icon.png" "$stage/usr/share/icons/hicolor/256x256/apps/fluxftp-linux-preview.png"
cat > "$stage/usr/share/applications/fluxftp-linux-preview.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=FluxFTP Linux Preview
Comment=FTP, FTPS and FXP client preview
Exec=fluxftp-linux-preview
Icon=fluxftp-linux-preview
Terminal=false
Categories=Network;FileTransfer;
EOF
cat > "$stage/DEBIAN/control" <<'EOF'
Package: fluxftp-linux-preview
Version: 0.1.0~preview3
Section: net
Priority: optional
Architecture: amd64
Maintainer: FluxFTP contributors
Homepage: https://github.com/Mainfr4m3/FluxFTP
Depends: libc6 (>= 2.34), libgcc-s1, libstdc++6, zlib1g, libssl3 | libssl3t64, libicu76 | libicu74 | libicu72, libx11-6, libice6, libsm6, libfontconfig1, libgl1
Description: FluxFTP Linux desktop preview
 Two file panes with the shared FluxFTP transfer engine.
 Early preview; full Windows feature parity and Visi bridge are not available.
EOF
find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/opt/fluxftp-linux-preview/FluxFTP.Linux" "$stage/usr/bin/fluxftp-linux-preview"
chmod 644 "$stage/DEBIAN/control" "$stage/usr/share/applications/fluxftp-linux-preview.desktop" "$stage/usr/share/icons/hicolor/256x256/apps/fluxftp-linux-preview.png"
dpkg-deb --root-owner-group --build "$stage" "$out/fluxftp-linux-preview_0.1.0.preview3_amd64.deb"
tar -C "$stage/opt" -czf "$out/FluxFTP-linux-x64-0.1.0-preview.3.tar.gz" fluxftp-linux-preview
cd "$out"
sha256sum fluxftp-linux-preview_0.1.0.preview3_amd64.deb FluxFTP-linux-x64-0.1.0-preview.3.tar.gz > SHA256SUMS-linux-preview.3.txt
