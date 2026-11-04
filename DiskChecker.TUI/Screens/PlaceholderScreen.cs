using DiskChecker.TUI.Services;

namespace DiskChecker.TUI.Screens;

/// <summary>Temporary screen used while the individual screens are implemented.</summary>
public sealed class PlaceholderScreen : ITuiScreen
{
    public string Id => "placeholder";

    public string TitleKey => "App.Name";

    public Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Io.Line("[yellow]Tato obrazovka je zatím ve výstavbě.[/]");
        var choice = context.Io.Choose("Co dál?", new[]
        {
            new TuiChoice(TuiKeys.MainMenu, "☰ Navigace"),
        }, allowCancel: true);
        return Task.FromResult(choice == TuiKeys.MainMenu ? ScreenResult.Menu() : ScreenResult.Back());
    }
}
