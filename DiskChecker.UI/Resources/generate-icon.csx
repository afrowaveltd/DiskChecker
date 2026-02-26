// Simple SVG to ICO converter
// Run: dotnet run

using System;
using System.IO;

Console.WriteLine("📦 DiskChecker Icon Generator");
Console.WriteLine("============================");
Console.WriteLine();
Console.WriteLine("✅ SVG ikonky vytvořeny:");
Console.WriteLine("   - icon.svg (light mode)");
Console.WriteLine("   - icon-dark.svg (dark mode)");
Console.WriteLine();
Console.WriteLine("📝 Pro vytvoření .ico souboru:");
Console.WriteLine("   1. Otevři https://convertio.co/svg-ico/");
Console.WriteLine("   2. Nahraj icon.svg");
Console.WriteLine("   3. Stáhni icon.ico");
Console.WriteLine("   4. Ulož do: DiskChecker.UI/Resources/");
Console.WriteLine();
Console.WriteLine("📝 Nebo použij ImageMagick:");
Console.WriteLine("   magick convert icon.svg -background none -define icon:auto-resize=256,128,64,48,32,16 icon.ico");
Console.WriteLine();
Console.WriteLine("💡 TIP: Pro Windows 11, doporučuji 256x256 PNG s transparentním pozadím.");

// Create a simple placeholder .ico (1x1 pixel) so build doesn't fail
var placeholder = new byte[] {
    0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x18, 0x00, 0x30, 0x00,
    0x00, 0x00, 0x16, 0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00,
    0x00, 0x00, 0x01, 0x00, 0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x27, 0xae,
    0x60, 0x00
};

File.WriteAllBytes("icon.ico", placeholder);
Console.WriteLine("✅ Placeholder icon.ico vytvořen (1x1 pixel)");
Console.WriteLine("   Nahraď ho později správnou ikonkou!");
