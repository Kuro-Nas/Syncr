#!/bin/bash

# Syncr Edge Deployment Script (v2.6.2)
# This script automates the installation and home-screen shortcut setup on Pi OS

APP_NAME="KuroSyncr"
INSTALL_DIR="/home/indraedge/SyncrEdge"
# Default zip package for Pi ARM64
ZIP_FILE="Syncr_Pi_arm64.zip"
if [ ! -f "$ZIP_FILE" ]; then
    ZIP_FILE=$(ls *.zip 2>/dev/null | head -n 1)
fi

echo "Starting Syncr v2.6 Deployment..."

# 1. Stop existing service
echo "⏹ Stopping existing service..."
sudo systemctl stop syncr.service 2>/dev/null

# 2. Extract Files
if [ -n "$ZIP_FILE" ] && [ -f "$ZIP_FILE" ]; then
    echo "Extracting new build ($ZIP_FILE)..."
    mkdir -p "$INSTALL_DIR"
    unzip -o "$ZIP_FILE" -d "$INSTALL_DIR"
else
    echo "Error: No ZIP file found in current directory!"
    exit 1
fi

# 3. Set Executable & Serial Device Permissions
echo "Setting permissions & serial port dialout access..."
chmod +x "$INSTALL_DIR/$EXECUTABLE"
sudo usermod -a -G dialout $USER 2>/dev/null || true
sudo usermod -a -G dialout indraedge 2>/dev/null || true
sudo chmod 666 /dev/ttyAMA0 2>/dev/null || true
sudo chmod 666 /dev/ttyS0 2>/dev/null || true
sudo chmod 666 /dev/ttyUSB* 2>/dev/null || true

# 4. Create Desktop Shortcut (Home Screen)
echo "Creating Desktop shortcut..."
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

# 5. Create Root Directory Shortcut (Symbolic Link)
echo "Creating Root Directory shortcut..."
sudo ln -sf "$INSTALL_DIR/$EXECUTABLE" /Syncr
sudo chmod +x /Syncr

# 6. Update System Service (v2.6.2)
echo "Configuring system service for v2.6.2..."

sudo systemctl stop syncr.service 2>/dev/null
sudo systemctl disable syncr.service 2>/dev/null

sudo bash -c "cat <<EOF > /etc/systemd/system/syncr.service
[Unit]
Description=Syncr Edge v2.6.2 Application
After=graphical.target

[Service]
Environment=DISPLAY=:0
Environment=XAUTHORITY=/home/indraedge/.Xauthority
Environment=DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus
ExecStart=$INSTALL_DIR/$EXECUTABLE
WorkingDirectory=$INSTALL_DIR
Restart=always
User=indraedge

[Install]
WantedBy=graphical.target
EOF"

# 7. Finalize
echo "Reloading and Starting v2.6.2..."
sudo systemctl daemon-reload
sudo systemctl enable syncr.service
sudo systemctl restart syncr.service

echo "--------------------------------------------------------"
echo "Syncr v2.6.2 Installed Successfully!"
echo "--------------------------------------------------------"
echo "Fixes Included (v2.6.2):"
echo "   - Multi-machine Simulation (Limit Removed)"
echo "   - Diverse Industrial Data Generation"
echo "   - Instant Live Metrics Visibility (Pre-populated)"
echo "--------------------------------------------------------"
echo "Press any key to finish..."
read -n 1
