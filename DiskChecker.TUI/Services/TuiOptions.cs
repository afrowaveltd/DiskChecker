using System.Globalization;

namespace DiskChecker.TUI.Services;

/// <summary>Command line options of the terminal front end.</summary>
public sealed class TuiOptions
{
    public bool ShowHelp { get; private set; }

    public bool ShowVersion { get; private set; }

    /// <summary>Explicit database file; overrides the persisted setting.</summary>
    public string? DatabasePath { get; private set; }

    /// <summary>Initial UI language (cs/en).</summary>
    public string? Language { get; private set; }

    /// <summary>Screen opened on start.</summary>
    public string? StartScreen { get; private set; }

    /// <summary>Verbose logging into the console (disabled during tests).</summary>
    public bool Verbose { get; private set; }

    public static TuiOptions Parse(string[] args)
    {
        var options = new TuiOptions();
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "-h":
                case "--help":
                case "/?":
                    options.ShowHelp = true;
                    break;
                case "-v":
                case "--version":
                    options.ShowVersion = true;
                    break;
                case "--db":
                    if (index + 1 < args.Length)
                    {
                        options.DatabasePath = args[++index];
                    }
                    break;
                case "--lang":
                case "--language":
                    if (index + 1 < args.Length)
                    {
                        options.Language = args[++index];
                    }
                    break;
                case "--screen":
                    if (index + 1 < args.Length)
                    {
                        options.StartScreen = args[++index];
                    }
                    break;
                case "--verbose":
                    options.Verbose = true;
                    break;
                default:
                    // Positional screen name: "dcheck smart" opens the SMART screen.
                    if (!arg.StartsWith('-'))
                    {
                        options.StartScreen ??= arg;
                    }
                    break;
            }
        }

        return options;
    }

    public static string HelpText
    {
        get
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("DiskChecker – terminálová verze");
            builder.AppendLine();
            builder.AppendLine("Použití: dcheck [volby] [obrazovka]");
            builder.AppendLine();
            builder.AppendLine("Obrazovky:");
            builder.AppendLine("  disks        výběr disků (výchozí)");
            builder.AppendLine("  cards        karty disků");
            builder.AppendLine("  surface      test povrchu");
            builder.AppendLine("  smart        SMART kontrola");
            builder.AppendLine("  seek         seek test");
            builder.AppendLine("  destructive  absolutní destruktivní test");
            builder.AppendLine("  safe         bezpečný destruktivní test");
            builder.AppendLine("  analysis     analytické pracoviště");
            builder.AppendLine("  comparison   porovnání disků");
            builder.AppendLine("  report       report");
            builder.AppendLine("  history      historie testů");
            builder.AppendLine("  certificates certifikáty");
            builder.AppendLine("  backup       záloha disku");
            builder.AppendLine("  restore      obnova ze zálohy");
            builder.AppendLine("  settings     nastavení");
            builder.AppendLine("  about        o aplikaci");
            builder.AppendLine();
            builder.AppendLine("Volby:");
            builder.AppendLine("  --db <cesta>       databázový soubor (přepíše nastavení)");
            builder.AppendLine("  --lang <cs|en>     jazyk rozhraní");
            builder.AppendLine("  --screen <id>      obrazovka otevřená po startu");
            builder.AppendLine("  --verbose          podrobné logování");
            builder.AppendLine("  -h, --help         tato nápověda");
            builder.AppendLine("  -v, --version      verze aplikace");
            builder.AppendLine();
            builder.AppendLine("Bez interaktivního terminálu aplikace čte volby z standardního vstupu,");
            builder.AppendLine($"takže ji lze skriptovat: printf '%s\\\n' 1 0 | dcheck --screen disks (Culture: {CultureInfo.CurrentCulture.Name})");
            return builder.ToString();
        }
    }
}
