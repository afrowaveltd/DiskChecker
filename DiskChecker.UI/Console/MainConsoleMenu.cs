using Spectre.Console;
using static System.Console;

namespace DiskChecker.UI.Console;

public class MainConsoleMenu
{
    private static readonly string[] MenuChoices = new[]
    {
        "[green]1. Kontrola disku (SMART)[/]",
        "[green]2. Úplný test (zápis/nula + kontrola)[/]",
        "[yellow]3. Historie testů[/]",
        "[yellow]4. Porovnání disků[/]",
        "[red]5. Nastavení (jazyk)[/]",
        "[dim]6. Konec[/]"
    };

    public async Task ShowAsync()
    {
        while (true)
        {
            try
            {
                Clear();
            }
            catch
            {
                // No console available, skip clear
            }
            
            AnsiConsole.Write(new FigletText("DiskChecker").Color(Color.Red));
            
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("DiskChecker - hlavní menu [v0.1]")
                    .PageSize(10)
                    .MoreChoicesText("[i](použijte šipky pro výběr)[/]")
                    .AddChoices(MenuChoices));

            switch (choice)
            {
                case var _ when choice.Contains("Kontrola disku"):
                    await CheckDiskMenuAsync();
                    break;
                case var _ when choice.Contains("Úplný test"):
                    await FullTestMenuAsync();
                    break;
                case var _ when choice.Contains("Historie"):
                    await HistoryMenuAsync();
                    break;
                case var _ when choice.Contains("Porovnání"):
                    await CompareMenuAsync();
                    break;
                case var _ when choice.Contains("Nastavení"):
                    await SettingsMenuAsync();
                    break;
                case var _ when choice.Contains("Konec"):
                    return;
            }
        }
    }

    private async Task CheckDiskMenuAsync()
    {
        AnsiConsole.WriteLine("[yellow]Získávám seznam disků...[/]");
        await Task.Delay(500);
        AnsiConsole.WriteLine("[red]TODO: Zobrazit výběr disku a SMART data[/]");
        WriteLine("[dim]Stiskněte libovolnou klávesu pro návrat...[/]");
        ReadKey();
    }

    private async Task FullTestMenuAsync()
    {
        AnsiConsole.WriteLine("[yellow]Získávám seznam disků...[/]");
        await Task.Delay(500);
        AnsiConsole.WriteLine("[red]TODO: Zobrazit výběr disku a test[/]");
        WriteLine("[dim]Stiskněte libovolnou klávesu pro návrat...[/]");
        ReadKey();
    }

    private async Task HistoryMenuAsync()
    {
        AnsiConsole.WriteLine("[yellow]Načítám historii...[/]");
        await Task.Delay(500);
        AnsiConsole.WriteLine("[red]TODO: Zobrazit historii testů[/]");
        WriteLine("[dim]Stiskněte libovolnou klávesu pro návrat...[/]");
        ReadKey();
    }

    private async Task CompareMenuAsync()
    {
        AnsiConsole.WriteLine("[yellow]Načítám data pro porovnání...[/]");
        await Task.Delay(500);
        AnsiConsole.WriteLine("[red]TODO: Zobrazit porovnání disků[/]");
        WriteLine("[dim]Stiskněte libovolnou klávesu pro návrat...[/]");
        ReadKey();
    }

    private async Task SettingsMenuAsync()
    {
        var language = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Vyberte jazyk / Select Language")
                .PageSize(5)
                .AddChoices(new[] { "Čeština", "English" }));

        AnsiConsole.WriteLine($"[green]Jazyk nastaven na: {language}[/]");
        await Task.Delay(1000);
    }
}
