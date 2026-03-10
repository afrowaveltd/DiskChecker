using System;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        var smartaPath = "E:/C#/DiskChecker/DiskChecker.Core/Models/SmartaData.cs";
        var lines = File.ReadAllLines(smartaPath);
        
        // Find lines around line 220-240 to see the structure
        Console.WriteLine("=== Lines 215-245 ===");
        for (int i = 214; i < 245 && i < lines.Length; i++)
        {
            Console.WriteLine($"{i+1}: {lines[i]}");
        }
    }
}
