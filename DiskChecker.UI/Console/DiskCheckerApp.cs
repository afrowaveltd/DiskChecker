using Spectre.Console;

namespace DiskChecker.UI.Console;

public class DiskCheckerApp
{
    public async Task RunAsync(string[] args)
    {
        AnsiConsole.Write(new FigletText("DiskChecker").Color(Color.Red));
        
        var menu = new MainConsoleMenu();
        await menu.ShowAsync();
    }
}
