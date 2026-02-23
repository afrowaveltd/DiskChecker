using Spectre.Console;

namespace DiskChecker.UI.Console;

/// <summary>
/// Console application entry point for the DiskChecker UI.
/// </summary>
public class DiskCheckerApp
{
    private readonly MainConsoleMenu _menu;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskCheckerApp"/> class.
    /// </summary>
    /// <param name="menu">Main menu instance.</param>
    public DiskCheckerApp(MainConsoleMenu menu)
    {
        _menu = menu;
    }

    /// <summary>
    /// Runs the console application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public async Task RunAsync(string[] args)
    {
        AnsiConsole.Write(new FigletText("DiskChecker").Color(Color.Red));

        await _menu.ShowAsync();
    }
}
