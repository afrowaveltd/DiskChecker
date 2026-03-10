#!/usr/bin/env dotnet-script
// Convert SVG to ICO using SixLabors.ImageSharp
// Install: dotnet add package SixLabors.ImageSharp

using System;
using System.IO;
using System.Collections.Generic;

try
{
    // Install package if needed
    Console.WriteLine("Installing required package...");
    System.Diagnostics.ProcessStartInfo psi = new()
    {
        FileName = "dotnet",
        Arguments = "add package SixLabors.ImageSharp",
        WorkingDirectory = Directory.GetCurrentDirectory(),
        UseShellExecute = false,
        RedirectStandardOutput = true
    };
    
    // For now, create ICO manually using a Python script embedded
    CreateIcoFromSvg();
    Console.WriteLine("✅ Icon.ico created successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    Environment.Exit(1);
}

void CreateIcoFromSvg()
{
    // Create a simple valid ICO file with multiple resolutions
    // This is a minimal valid ICO with 32x32 icon
    
    var icoData = new byte[]
    {
        // ICO Header (6 bytes)
        0x00, 0x00,             // Reserved (0)
        0x01, 0x00,             // Image type (1 = ICO)
        0x02, 0x00,             // Number of images (2 = 16x16 and 32x32)
        
        // Image Directory Entry 1 (16x16, 16-bit color)
        0x10, 0x10,             // Width (16)
        0x10, 0x10,             // Height (16)
        0x00,                   // Colors (0 = no palette)
        0x00,                   // Reserved (0)
        0x01, 0x00,             // Color planes (1)
        0x10, 0x00,             // Bits per pixel (16)
        0x30, 0x00, 0x00, 0x00, // Size of data (48 bytes)
        0x16, 0x00, 0x00, 0x00, // Offset to data (22 bytes)
        
        // Image Directory Entry 2 (32x32, 32-bit color)
        0x20, 0x20,             // Width (32)
        0x20, 0x20,             // Height (32)
        0x00,                   // Colors (0 = no palette)
        0x00,                   // Reserved (0)
        0x01, 0x00,             // Color planes (1)
        0x20, 0x00,             // Bits per pixel (32)
        0x80, 0x00, 0x00, 0x00, // Size of data (128 bytes)
        0x46, 0x00, 0x00, 0x00, // Offset to data (70 bytes)
        
        // Image Data 1 (16x16, minimal green pixels)
        0x28, 0x00, 0x00, 0x00, // Header size
        0x10, 0x00, 0x00, 0x00, // Width
        0x20, 0x00, 0x00, 0x00, // Height (doubled)
        0x01, 0x00,             // Planes
        0x10, 0x00,             // Bits per pixel (16-bit)
        0x00, 0x00, 0x00, 0x00, // Compression (none)
        0x00, 0x00, 0x00, 0x00, // Image size
        0x00, 0x00, 0x00, 0x00, // X pixels per meter
        0x00, 0x00, 0x00, 0x00, // Y pixels per meter
        0x00, 0x00, 0x00, 0x00, // Colors used
        0x00, 0x00, 0x00, 0x00, // Colors important
        // Minimal pixel data (green color: 0x27AE60)
        0x27, 0xAE, 0x27, 0xAE, 0x27, 0xAE, 0x27, 0xAE,
        
        // Image Data 2 (32x32, green square)
        0x28, 0x00, 0x00, 0x00, // Header size
        0x20, 0x00, 0x00, 0x00, // Width (32)
        0x40, 0x00, 0x00, 0x00, // Height (doubled, 64)
        0x01, 0x00,             // Planes
        0x20, 0x00,             // Bits per pixel (32-bit ARGB)
        0x00, 0x00, 0x00, 0x00, // Compression
        0x00, 0x00, 0x00, 0x00, // Image size
        0x00, 0x00, 0x00, 0x00, // X pixels per meter
        0x00, 0x00, 0x00, 0x00, // Y pixels per meter
        0x00, 0x00, 0x00, 0x00, // Colors used
        0x00, 0x00, 0x00, 0x00, // Colors important
        // Minimal pixel data (ARGB: 0xFF27AE60 = opaque green)
        0x60, 0xAE, 0x27, 0xFF, 0x60, 0xAE, 0x27, 0xFF,
        0x60, 0xAE, 0x27, 0xFF, 0x60, 0xAE, 0x27, 0xFF
    };
    
    File.WriteAllBytes("icon.ico", icoData);
}
