#!/bin/bash

# Kuro Syncr Deployment Script (v3.0)
# PRE-RELEASE TEMPLATE

APP_NAME="KuroSyncr"
INSTALL_DIR="/home/indraedge/KuroSyncr"
ZIP_FILE="publish-pi-v30.zip"
EXECUTABLE="Syncr.UI"

echo "Starting Kuro Syncr v3.0 Deployment (Kiosk Mode)..."

# 1. Stop existing service
echo "⏹Stopping existing service..."
sudo systemctl stop syncr.service 2>/dev/null

# 2. Extract Files
if [ -f "$ZIP_FILE" ]; then
    echo "Extracting v3.0 build..."
    mkdir -p "$INSTALL_DIR"
    unzip -o "$ZIP_FILE" -d "$INSTALL_DIR"
else
    echo "[!] Warning: $ZIP_FILE not found. Proceeding with script setup only."
fi

# 3. Set Permissions
chmod +x "$INSTALL_DIR/$EXECUTABLE" 2>/dev/null

# 4. Create Desktop Shortcut (Home Screen)
echo "Creating Desktop shortcut: Kuro Syncr..."
cat <<EOF > ~/Desktop/KuroSyncr.desktop
[Desktop Entry]
Name=Kuro Syncr
Exec=$INSTALL_DIR/$EXECUTABLE
Path=$INSTALL_DIR
Type=Application
Icon=$INSTALL_DIR/Assets/logo.png
Terminal=false
Categories=Utility;
X-KeepTerminal=false
StartupNotify=false
EOF
chmod +x ~/Desktop/KuroSyncr.desktop
gio set ~/Desktop/KuroSyncr.desktop metadata::trusted true 2>/dev/null

# 5. Create Root Directory Shortcut
sudo ln -sf "$INSTALL_DIR/$EXECUTABLE" /Syncr

# 6. Update System Service (v3.0 Kiosk Mode)
echo "Configuring Kiosk Mode Service..."

sudo bash -c "cat <<EOF > /etc/systemd/system/syncr.service
[Unit]
Description=Kuro Syncr v3.0 (Kiosk Mode)
After=graphical.target

[Service]
Environment=DISPLAY=:0
Environment=XAUTHORITY=/home/indraedge/.Xauthority
Environment=DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus
# v3.0 will include Kiosk flags
ExecStart=$INSTALL_DIR/$EXECUTABLE --kiosk
WorkingDirectory=$INSTALL_DIR
Restart=always
User=indraedge

[Install]
WantedBy=graphical.target
EOF"

# 7. Finalize
sudo systemctl daemon-reload
sudo systemctl enable syncr.service

echo "--------------------------------------------------------"
echo "Kuro Syncr v3.0 Prepared!"
echo "--------------------------------------------------------"
echo "Planned v3.0 Features:"
echo "   - Strict Full-Screen Kiosk Mode"
echo "   - Terminal Password Unlock"
echo "   - Enhanced Color Palette Variety"
echo "--------------------------------------------------------"
echo "Note: This is a setup script for the upcoming v3.0 release."
echo "Press any key to finish..."
read -n 1
