using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Spectre.Console;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// About screen: application identity, capability summary, the destructive test
/// warning, runtime/system information, the active database location and provider
/// and the language switch. Terminal counterpart of the desktop about section.
/// </summary>
public sealed class AboutScreen : ITuiScreen
{
    private static readonly string[] CapabilityKeys =
    {
        "Tui.About.Capability.Detection",
        "Tui.About.Capability.Smart",
        "Tui.About.Capability.Surface",
        "Tui.About.Capability.Seek",
        "Tui.About.Capability.Destructive",
        "Tui.About.Capability.Certificates",
        "Tui.About.Capability.History",
        "Tui.About.Capability.Backup",
    };

    private static readonly string[] CredentialKeys =
    {
        "password",
        "pwd",
        "pass",
        "user id",
        "userid",
        "username",
        "user",
    };

    private string _status = string.Empty;

    /// <inheritdoc />
    public string Id => ScreenIds.About;

    /// <inheritdoc />
    public string TitleKey => "Nav.About";

    /// <inheritdoc />
    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Render(context);

            string? action = context.Io.Choose(context.L("Tui.About.Actions"), BuildActions(context), pageSize: 8);
            switch (action)
            {
                case "language":
                    await SwitchLanguageAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case "refresh":
                    _status = context.L("Tui.About.Status.Refreshed");
                    break;
                case TuiKeys.Exit:
                    return ScreenResult.Exit();
                case TuiKeys.MainMenu:
                    return ScreenResult.Menu();
                default:
                    return ScreenResult.Back();
            }
        }
    }

    private IReadOnlyList<TuiChoice> BuildActions(TuiContext context) => new List<TuiChoice>
    {
        new("language", "🌐 " + context.L("Tui.About.Action.Language")),
        new("refresh", "🔄 " + context.L("Common.Refresh")),
        new(TuiKeys.Back, context.L("Common.Back")),
        new(TuiKeys.MainMenu, "☰ " + context.L("Tui.Common.Menu")),
    };

    // ========== Actions ==========

    private async Task SwitchLanguageAsync(TuiContext context, CancellationToken cancellationToken)
    {
        var locales = context.Locale.AvailableLocales;
        if (locales.Count == 0)
        {
            _status = context.L("Tui.About.Language.Unavailable");
            return;
        }

        var choices = locales
            .Select(locale => new TuiChoice(
                "language-" + locale,
                LanguageLabel(context, locale) +
                (string.Equals(locale, context.Locale.CurrentLocale, StringComparison.OrdinalIgnoreCase) ? " ✔" : string.Empty)))
            .ToList();

        string? choice = context.Io.Choose(context.L("Tui.About.Language.Title"), choices, pageSize: 10);
        if (string.IsNullOrEmpty(choice) || choice == TuiKeys.Back || !choice.StartsWith("language-", StringComparison.Ordinal))
        {
            return;
        }

        string selected = choice["language-".Length..];
        try
        {
            await context.GetRequiredService<ISettingsService>().SetLanguageAsync(selected).ConfigureAwait(false);
            context.Locale.SetLocale(selected);
            _status = context.L("Tui.About.Language.Changed", LanguageLabel(context, selected));
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            _status = context.L("Tui.About.Language.Failed", ex.Message);
        }
    }

    // ========== Rendering ==========

    private void Render(TuiContext context)
    {
        context.Io.Clear();
        context.Io.Write(new Rule($"[bold]{Markup.Escape(context.L("Nav.About"))}[/] " +
                                 $"[grey]{Markup.Escape(context.L("App.Subtitle"))}[/]")
        {
            Justification = Justify.Left,
            Style = new Style(foreground: TuiTheme.Primary),
        });

        if (!string.IsNullOrWhiteSpace(_status))
        {
            context.Io.Line($"[grey]{Markup.Escape(_status)}[/]");
        }

        RenderIdentity(context);
        RenderCapabilities(context);
        RenderWarning(context);
        RenderSystem(context);
        RenderDatabase(context);
        RenderPlatforms(context);
    }

    private static void RenderIdentity(TuiContext context)
    {
        context.Io.Line($"[bold {TuiTheme.Primary.ToMarkup()}]{Markup.Escape(context.L("App.Name"))}[/] " +
                        $"[grey]{Markup.Escape(context.L("Settings.AboutVersion"))}[/]");
        context.Io.Line($"[grey]{Markup.Escape(context.L("Settings.AboutDesc"))}[/]");
        context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.About.Version", context.SystemInfo.AppVersion))}[/]");
        context.Io.Line($"[grey]{Markup.Escape(context.L("Settings.AboutCopyright"))}[/]");
    }

    private static void RenderCapabilities(TuiContext context)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Title($"[bold]{Markup.Escape(context.L("Tui.About.Capabilities"))}[/]")
            .AddColumn(new TableColumn(context.L("Tui.About.Column.Capability")));

        foreach (string key in CapabilityKeys)
        {
            table.AddRow("• " + Markup.Escape(context.L(key)));
        }

        context.Io.Write(table);
    }

    private static void RenderWarning(TuiContext context)
    {
        context.Io.Write(new Panel(new Markup($"[bold]⚠ {Markup.Escape(context.L("Tui.About.DestructiveWarningTitle"))}[/]\n" +
                                              Markup.Escape(context.L("Tui.About.DestructiveWarning"))))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: TuiTheme.Danger),
            Padding = new Padding(1, 0, 1, 0),
        });
    }

    private static void RenderSystem(TuiContext context)
    {
        var info = context.SystemInfo;
        var rows = new List<(string Label, string Value)>
        {
            (context.L("Tui.About.Label.Os"), info.OsName),
            (context.L("Tui.About.Label.Kernel"), info.KernelVersion),
            (context.L("Tui.About.Label.Cpu"), info.CpuInfo),
            (context.L("Tui.About.Label.Ram"), info.RamInfo),
            (context.L("Tui.About.Label.Runtime"), info.RuntimeInfo),
            (context.L("Tui.About.Label.Version"), info.AppVersion),
        };

        context.Io.Write(BuildKeyValueTable(context.L("Tui.About.System"), rows));
    }

    private static void RenderDatabase(TuiContext context)
    {
        var settings = context.GetRequiredService<SettingsService>();
        // The active database may have been overridden on the command line (--db);
        // show both what this process uses and what the settings file stores.
        var active = context.GetService<DatabaseStorageSettings>();
        var configured = settings.StartupDatabaseStorageSettings;

        var rows = new List<(string Label, string Value)>
        {
            (context.L("Tui.About.Label.Provider"), DescribeProvider((active ?? configured).Provider)),
            (context.L("Tui.About.Label.Location"), DescribeLocation(context, active ?? configured)),
            (context.L("Tui.About.Label.SettingsFile"), settings.SettingsFilePath),
        };

        if (active is not null && !IsSameDatabase(active, configured))
        {
            rows.Add((context.L("Tui.About.Label.Configured"),
                DescribeProvider(configured.Provider) + " · " + DescribeLocation(context, configured)));
        }

        context.Io.Write(BuildKeyValueTable(context.L("Tui.About.Database"), rows));

        var database = active ?? configured;
        if (database.Provider != DatabaseProviderKind.Sqlite && !string.IsNullOrWhiteSpace(database.ConnectionString))
        {
            context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.About.Label.Connection"))}: " +
                            $"{Markup.Escape(MaskCredentials(database.ConnectionString!))}[/]");
        }

        if (configured.Provider != DatabaseProviderKind.Sqlite && !string.IsNullOrWhiteSpace(configured.ConnectionString))
        {
            context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.About.Label.Configured"))}: " +
                            $"{Markup.Escape(MaskCredentials(configured.ConnectionString!))}[/]");
        }
    }

    private static bool IsSameDatabase(DatabaseStorageSettings left, DatabaseStorageSettings right) =>
        left.Provider == right.Provider &&
        string.Equals(left.ConnectionString ?? string.Empty, right.ConnectionString ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static void RenderPlatforms(TuiContext context) =>
        context.Io.Line($"[grey]{Markup.Escape(context.L("Tui.About.Platforms"))}[/]");

    private static Table BuildKeyValueTable(string title, IEnumerable<(string Label, string Value)> rows)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(TuiTheme.Muted)
            .Title($"[bold]{Markup.Escape(title)}[/]")
            .AddColumn(new TableColumn(string.Empty).NoWrap())
            .AddColumn(new TableColumn(string.Empty));

        foreach (var (label, value) in rows)
        {
            table.AddRow(Markup.Escape(label), Markup.Escape(value));
        }

        return table;
    }

    // ========== Text helpers ==========

    private static string LanguageLabel(TuiContext context, string locale)
    {
        string key = "Nav.Language." + locale;
        string label = context.L(key);
        return label.StartsWith("[[", StringComparison.Ordinal) ? locale.ToUpperInvariant() : label;
    }

    private static string DescribeProvider(DatabaseProviderKind provider) => provider switch
    {
        DatabaseProviderKind.Sqlite => "SQLite",
        DatabaseProviderKind.PostgreSql => "PostgreSQL",
        DatabaseProviderKind.SqlServer => "SQL Server",
        _ => provider.ToString(),
    };

    private static string DescribeLocation(TuiContext context, DatabaseStorageSettings database)
    {
        string? connection = database.ConnectionString;
        if (string.IsNullOrWhiteSpace(connection))
        {
            return context.L("Common.NA");
        }

        if (database.Provider == DatabaseProviderKind.Sqlite)
        {
            foreach (string part in connection.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separator = part.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    continue;
                }

                string key = part[..separator].Trim();
                if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("DataSource", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("Filename", StringComparison.OrdinalIgnoreCase))
                {
                    return part[(separator + 1)..].Trim();
                }
            }

            return context.L("Common.NA");
        }

        return MaskCredentials(connection);
    }

    private static string MaskCredentials(string connection)
    {
        var parts = connection.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var visible = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            int separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                visible.Add(part);
                continue;
            }

            string key = part[..separator].Trim();
            visible.Add(IsCredentialKey(key) ? key + "=***" : part);
        }

        return string.Join("; ", visible);
    }

    private static bool IsCredentialKey(string key) =>
        CredentialKeys.Any(candidate => string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase));

    private static bool IsServiceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or ArgumentException or FormatException or TimeoutException;
}