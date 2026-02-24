using Spectre.Console;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using System.Runtime.InteropServices;

namespace DiskChecker.UI.Console;

/// <summary>
/// Diagnostics application to help troubleshoot SMART data loading issues.
/// </summary>
public class DiagnosticsApp
{
    private readonly ISmartaProvider _smartaProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsApp"/> class.
    /// </summary>
    /// <param name="smartaProvider">SMART data provider instance.</param>
    public DiagnosticsApp(ISmartaProvider smartaProvider)
    {
        _smartaProvider = smartaProvider;
    }

    /// <summary>
    /// Runs diagnostics checks for SMART data loading.
    /// </summary>
    public async Task RunAsync()
    {
        AnsiConsole.Write(new FigletText("Diagnostika").Color(Color.Blue));
        AnsiConsole.MarkupLine("[bold white]DiskChecker - Diagnostika SMART[/]\n");

        // 1. Show OS info
        AnsiConsole.MarkupLine("[yellow]1. Operační systém:[/]");
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Neznámý";
        AnsiConsole.MarkupLine($"   → {os}");
        AnsiConsole.WriteLine();

        // 2. Try to list drives
        AnsiConsole.MarkupLine("[yellow]2. Zjišťování disků:[/]");
        try
        {
            var drives = await _smartaProvider.ListDrivesAsync();
            if (drives.Count > 0)
            {
                AnsiConsole.MarkupLine($"   [green]✓ Nalezeno {drives.Count} disků:[/]");
                foreach (var drive in drives)
                {
                    AnsiConsole.MarkupLine($"     • {Markup.Escape(drive.Name)} - {Markup.Escape(drive.Path)} ({FormatBytes(drive.TotalSize)})");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[red]   ✗ Žádné disky nebyly nalezeny![/]");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    AnsiConsole.MarkupLine("   → Zkuste spustit program jako Správce (Administrator)");
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]   ✗ Chyba: {Markup.Escape(ex.Message)}[/]");
        }
        AnsiConsole.WriteLine();

        // 3. Check dependencies
        AnsiConsole.MarkupLine("[yellow]3. Kontrola závislostí:[/]");
        try
        {
            var depInstructions = await _smartaProvider.GetDependencyInstructionsAsync();
            if (depInstructions == null)
            {
                AnsiConsole.MarkupLine("[green]   ✓ Všechny závislosti jsou dostupné[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]   ⚠ Chybí závislosti:[/]");
                AnsiConsole.MarkupLine($"   {depInstructions}");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]   ✗ Chyba: {Markup.Escape(ex.Message)}[/]");
        }
        AnsiConsole.WriteLine();

        // 4. Try to read SMART from first drive
        AnsiConsole.MarkupLine("[yellow]4. Test čtení SMART dat:[/]");
        try
        {
            var drives = await _smartaProvider.ListDrivesAsync();
            if (drives.Count > 0)
            {
                var firstDrive = drives[0];
                AnsiConsole.MarkupLine($"   Čtení dat z: {Markup.Escape(firstDrive.Name)}...");
                
                var smartData = await _smartaProvider.GetSmartaDataAsync(firstDrive.Path);
                if (smartData != null)
                {
                    AnsiConsole.MarkupLine("[green]   ✓ SMART data byla úspěšně načtena:[/]");
                    AnsiConsole.MarkupLine($"     • Model: {Markup.Escape(smartData.DeviceModel ?? "---")}");
                    AnsiConsole.MarkupLine($"     • Sériové číslo: {Markup.Escape(smartData.SerialNumber ?? "---")}");
                    AnsiConsole.MarkupLine($"     • Teplota: {smartData.Temperature:F1} °C");
                    AnsiConsole.MarkupLine($"     • Naběhané hodiny: {smartData.PowerOnHours}");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]   ⚠ Nepodařilo se načíst SMART data[/]");
                    AnsiConsole.MarkupLine("     Možné příčiny:");
                    AnsiConsole.MarkupLine("     • Disk nemusí podporovat SMART");
                    AnsiConsole.MarkupLine("     • Systém nemusí mít přístup k datům disku");
                    AnsiConsole.MarkupLine("     • smartmontools nejsou instalovány (Windows)");
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]   ✗ Chyba: {Markup.Escape(ex.Message)}[/]");
        }
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Stiskněte libovolnou klávesu pro ukončení diagnostiky...[/]");
        try
        {
            System.Console.ReadKey(true);
        }
        catch { }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double b = bytes;
        while (b >= 1024 && i < sizes.Length - 1)
        {
            b /= 1024;
            i++;
        }
        return $"{b:F1} {sizes[i]}";
    }
}
