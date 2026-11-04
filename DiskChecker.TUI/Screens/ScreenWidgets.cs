using Spectre.Console;
using Spectre.Console.Rendering;

namespace DiskChecker.TUI.Screens;

/// <summary>Small reusable renderables shared by the terminal screens.</summary>
public static class ScreenWidgets
{
    /// <summary>Two column label/value table.</summary>
    public static Table KeyValue(string title, IEnumerable<(string Label, string Value)> rows, bool expand = false)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Title($"[bold]{Markup.Escape(title)}[/]")
            .AddColumn(new TableColumn("[grey]Položka[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Hodnota[/]"));
        table.Expand = expand;

        foreach (var (label, value) in rows)
        {
            table.AddRow(Markup.Escape(label), Markup.Escape(value));
        }

        return table;
    }

    /// <summary>Coloured notice panel.</summary>
    public static IRenderable Notice(string title, string body, Color color, string icon = "•")
    {
        var markup = new Markup($"{icon} [bold]{Markup.Escape(title)}[/]\n{Markup.Escape(body)}");
        return new Panel(markup)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: color),
            Padding = new Padding(1, 0, 1, 0),
        };
    }

    /// <summary>Bullet list rendered as markup lines.</summary>
    public static string Bullets(IEnumerable<string> items, string bullet = "•") =>
        string.Join(Environment.NewLine, items.Select(item => $"{bullet} {Markup.Escape(item)}"));

    /// <summary>Compact two-column statistics strip.</summary>
    public static Table Metrics(IEnumerable<(string Label, string Value)> metrics, int columns = 4)
    {
        var list = metrics.ToList();
        var table = new Table().Border(TableBorder.None).HideHeaders();
        for (int i = 0; i < columns; i++)
        {
            table.AddColumn(new TableColumn(string.Empty));
        }

        for (int index = 0; index < list.Count; index += columns)
        {
            var cells = new List<string>();
            for (int column = 0; column < columns; column++)
            {
                int position = index + column;
                cells.Add(position < list.Count
                    ? $"[grey]{Markup.Escape(list[position].Label)}[/] [bold]{Markup.Escape(list[position].Value)}[/]"
                    : string.Empty);
            }

            table.AddRow(cells.Select(cell => cell).ToArray());
        }

        return table;
    }
}
