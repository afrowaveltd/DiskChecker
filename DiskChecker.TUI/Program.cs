using System.Text;
using DiskChecker.TUI.App;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DiskChecker.TUI;

/// <summary>
/// DiskChecker terminal front end. It shares the domain, application and
/// infrastructure layers with the desktop build, so detection, SMART analysis,
/// surface/seek tests, destructive tests, certificates and history behave the
/// same way in both front ends.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = TuiOptions.Parse(args);
        if (options.ShowHelp)
        {
            Console.WriteLine(TuiOptions.HelpText);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine($"DiskChecker {typeof(Program).Assembly.GetName().Version}");
            return 0;
        }

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Terminals without a configurable encoding still render ASCII fine.
        }

        try
        {
            Console.Title = "DiskChecker";
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or InvalidOperationException)
        {
            // Window title is cosmetic and not available everywhere.
        }

        var locale = new TuiLocaleService();
        // The terminal console must capture the real stdout before the platform
        // providers start writing their own diagnostics to Console.Out.
        var io = new TuiIo();
        SilenceLibraryConsoleOutput();
        using var provider = TuiHost.BuildServices(options, io, locale);

        var settingsLanguage = ResolvePersistedLanguage(provider);
        locale.SetLocale(options.Language ?? settingsLanguage ?? "cs");

        var cancellation = provider.GetRequiredService<TuiCancellation>();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.HandleInterrupt();
        };

        try
        {
            TuiHost.InitializeDatabase(provider);
        }
        catch (Exception ex)
        {
            io.Line($"[red]Databázi se nepodařilo připravit: {Spectre.Console.Markup.Escape(ex.Message)}[/]");
            return 2;
        }

        var context = provider.GetRequiredService<TuiContext>();
        var shell = new TuiShell(provider, context, cancellation);
        string startScreen = options.StartScreen ?? ScreenIds.DiskSelection;
        return await shell.RunAsync(startScreen, cancellation.AppToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The infrastructure providers (for example the Linux SMART provider) write
    /// diagnostic lines with <c>Console.WriteLine</c>. In a full screen terminal UI
    /// those writes would corrupt the rendered frames, so the global output is
    /// redirected after the console captured the real stream.
    /// </summary>
    private static void SilenceLibraryConsoleOutput()
    {
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
    }

    /// <summary>Reads the persisted UI language so the terminal starts in the user's language.</summary>
    private static string? ResolvePersistedLanguage(IServiceProvider provider)
    {
        try
        {
            var settings = provider.GetRequiredService<DiskChecker.Core.Interfaces.ISettingsService>();
            string language = settings.GetLanguageAsync().GetAwaiter().GetResult();
            return string.IsNullOrWhiteSpace(language) ? null : language;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
