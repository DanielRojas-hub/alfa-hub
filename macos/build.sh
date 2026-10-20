#!/bin/zsh
# Compila "Alfa Hub.app" junto a este script.
set -e
cd "$(dirname "$0")"
APP="Alfa Hub.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
cp ../assets/character.png "$APP/Contents/Resources/character.png"
cp ../assets/outfits.json "$APP/Contents/Resources/outfits.json"
swiftc -O -swift-version 5 -o "$APP/Contents/MacOS/AlfaHub" main.swift Mirrors.swift Sounds.swift Audio.swift Outfit.swift OutfitPage.swift
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Alfa Hub</string>
  <key>CFBundleDisplayName</key><string>Alfa Hub</string>
  <key>CFBundleIdentifier</key><string>local.alfahub</string>
  <key>CFBundleExecutable</key><string>AlfaHub</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>LSMinimumSystemVersion</key><string>15.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
# Firma local con un requisito fijo (el identificador): así macOS conserva el permiso de
# Grabación de pantalla entre compilaciones (con la firma local por defecto se pierde en cada una).
codesign --force --sign - -r='designated => identifier "local.alfahub"' "$APP" >/dev/null
echo "Listo: $(pwd)/$APP"
