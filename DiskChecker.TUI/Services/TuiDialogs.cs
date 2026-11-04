using Spectre.Console;

namespace DiskChecker.TUI.Services;

/// <summary>Terminal counterparts of the desktop dialog service.</summary>
public sealed class TuiDialogs
{
    private readonly ITuiIo _io;

    public TuiDialogs(ITuiIo io) => _io = io ?? throw new ArgumentNullException(nameof(io));

    public void Info(string title, string message) => Show(title, message, TuiTheme.Primary);

    public void Success(string title, string message) => Show(title, message, TuiTheme.Success);

    public void Warning(string title, string message) => Show(title, message, TuiTheme.Warning);

    public void Error(string title, string message) => Show(title, message, TuiTheme.Danger);

    /// <summary>Shows a message panel and asks for confirmation.</summary>
    public bool Confirm(string title, string message, bool danger = false)
    {
        Show(title, message, danger ? TuiTheme.Danger : TuiTheme.Primary);
        return _io.Confirm(danger ? "Opravdu chcete pokračovat?" : "Pokračovat?", false);
    }

    /// <summary>Shows an informational panel and lets the user dismiss it.</summary>
    public void Notice(string title, string message)
    {
        Show(title, message, TuiTheme.Primary);
        _io.Pause();
    }

    public string? Ask(string question, string? defaultValue = null) => _io.Ask(question, defaultValue);

    private void Show(string title, string message, Color color)
    {
        string body = Markup.Escape(message ?? string.Empty);
        var panel = new Panel(new Markup(body))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: color),
            Header = new PanelHeader($"[bold]{Markup.Escape(title ?? string.Empty)}[/]", Justify.Left),
            Padding = new Padding(1, 0, 1, 0),
        };
        _io.Write(panel);
    }
}
