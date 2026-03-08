#!/bin/bash

# DiskChecker build script for Linux

echo "Building DiskChecker for Linux..."

# Restore NuGet packages
dotnet restore

# Build the solution
dotnet build --configuration Release

# Publish the application
dotnet publish ./DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --output ./publish/linux-x64

echo "Build completed successfully!"
echo "Published application is located in ./publish/linux-x64"

# Make the executable runnable
chmod +x ./publish/linux-x64/DiskChecker.UI.Avalonia

echo "You can now run the application with:"
echo "./publish/linux-x64/DiskChecker.UI.Avalonia"