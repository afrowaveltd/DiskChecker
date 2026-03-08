#!/bin/bash

# DiskChecker installation script for Linux

echo "Installing DiskChecker..."

# Determine the distribution
if [ -f /etc/os-release ]; then
    . /etc/os-release
    DISTRO=$NAME
    VERSION=$VERSION_ID
else
    echo "Cannot determine the Linux distribution."
    exit 1
fi

echo "Detected distribution: $DISTRO $VERSION"

# Create installation directory
INSTALL_DIR="/opt/diskchecker"
sudo mkdir -p $INSTALL_DIR

# Copy files
sudo cp -r ./publish/linux-x64/* $INSTALL_DIR/

# Set permissions
sudo chown -R root:root $INSTALL_DIR
sudo chmod +x $INSTALL_DIR/DiskChecker.UI.Avalonia

# Create symbolic link
sudo ln -sf $INSTALL_DIR/DiskChecker.UI.Avalonia /usr/local/bin/diskchecker

# Create desktop entry for GUI environments
cat > diskchecker.desktop << EOF
[Desktop Entry]
Name=DiskChecker
Comment=Professional Disk Diagnosis Tool
Exec=/usr/local/bin/diskchecker
Icon=$INSTALL_DIR/Assets/avalonia-logo.ico
Terminal=false
Type=Application
Categories=Utility;System;
EOF

# Install desktop entry
if [ "$XDG_CURRENT_DESKTOP" != "" ]; then
    sudo cp diskchecker.desktop /usr/share/applications/
    echo "Desktop entry installed."
fi

echo "DiskChecker installed successfully!"
echo "You can now run the application by typing 'diskchecker' in the terminal"
echo "or by searching for 'DiskChecker' in your applications menu."