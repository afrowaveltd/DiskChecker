using Spectre.Console;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiskChecker.Core.Interfaces;
using DiskChecker.UI.Console.Pages;

namespace DiskChecker.UI.Console;

/// <summary>
/// Console application entry point for the DiskChecker UI.
/// </summary>
public class DiskCheckerApp
{
    private readonly MainConsoleMenu _menu;
    private readonly DiagnosticsApp _diagnostics;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskCheckerApp"/> class.
    /// </summary>
    /// <param name="menu">Main menu instance.</param>
    /// <param name="smartaProvider">SMART provider for diagnostics.</param>
    public DiskCheckerApp(MainConsoleMenu menu, ISmartaProvider smartaProvider)
    {
        _menu = menu;
        _diagnostics = new DiagnosticsApp(smartaProvider);
    }

    /// <summary>
    /// Runs the console application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public async Task RunAsync(string[] args)
    {
        // Check for diagnostics flag
        if (args.Contains("--diagnostics") || args.Contains("-d"))
        {
            await _diagnostics.RunAsync();
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !IsRunningAsAdmin())
        {
            AnsiConsole.MarkupLine("[bold red]UPOZORNĚNÍ: Program neběží s právy administrátora![/]");
            AnsiConsole.MarkupLine("[yellow]Většina funkcí pro čtení disků a SMART dat bude omezena nebo nefunkční.[/]");
            AnsiConsole.MarkupLine("[yellow]Spusťte prosím terminál (nebo aplikaci) jako 'Správce'.[/]");
            AnsiConsole.MarkupLine("[dim]Tip: Spusťte 'DiskChecker --diagnostics' pro diagnostiku problémů s SMART[/]");
            AnsiConsole.WriteLine();
        }

        try
        {
            await _menu.ShowAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            AnsiConsole.MarkupLine("[red]Aplikace havarovala. Stiskněte libovolnou klávesu pro ukončení...[/]");
            System.Console.ReadKey(true);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
