using System.Globalization;
using System.Text;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DiskChecker.TUI.Screens;

/// <summary>
/// Disk card browser ("medical record" of every known disk) with search and filter
/// actions that mirror the desktop <c>DiskCardsViewModel</c>.
/// </summary>
public sealed class DiskCardsScreen : ITuiScreen
{
    internal const string ActionRefresh = "refresh";
    internal const string ActionSearch = "search";
    internal const string ActionGrade = "grade";
    internal const string ActionLocked = "locked";
    internal const string ActionArchived = "archived";
    internal const string CardKeyPrefix = "card:";
    internal const string CardActionDetail = "detail";
    internal const string CardActionArchive = "archive";
    internal const string CardActionRestore = "restore";
    internal const string CardActionCertificate = "certificate";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] Grades = { "A", "B", "C", "D", "E", "F" };

    public string Id => ScreenIds.DiskCards;

    public string TitleKey => "Nav.DiskCards";

    public async Task<ScreenResult> ShowAsync(TuiContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = new DiskCardsState();
        await LoadAsync(context, state, includeMaintenance: false).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyFilters(context, state);
            Render(context, state);

            string? choice = context.Io.Choose(context.L("Tui.DiskCards.Actions"), BuildChoices(context, state));
            if (choice is null || choice == TuiKeys.Back)
            {
                return ScreenResult.Back();
            }

            if (choice == TuiKeys.Exit)
            {
                return ScreenResult.Exit();
            }

            if (choice == TuiKeys.MainMenu)
            {
                return ScreenResult.Menu();
            }

            switch (choice)
            {
                case ActionRefresh:
                    await LoadAsync(context, state, includeMaintenance: true).ConfigureAwait(false);
                    continue;

                case ActionSearch:
                    PromptSearch(context, state);
                    continue;

                case ActionGrade:
                    PromptGradeFilter(context, state);
                    continue;

                case ActionLocked:
                    state.LockedOnly = !state.LockedOnly;
                    state.Notice = context.L("Tui.DiskCards.LockedFilter", state.LockedOnly ? context.L("Common.On") : context.L("Common.Off"));
                    continue;

                case ActionArchived:
                    state.ArchivedOnly = !state.ArchivedOnly;
                    state.Notice = context.L("Tui.DiskCards.ArchivedFilter", state.ArchivedOnly ? context.L("Common.On") : context.L("Common.Off"));
                    continue;
            }

            if (!TryResolveCard(choice, state, out DiskCard card))
            {
                continue;
            }

            ScreenResult? navigation = await RunCardActionsAsync(context, state, card).ConfigureAwait(false);
            if (navigation is { } result)
            {
                return result;
            }
        }

        return ScreenResult.Back();
    }

    // ---------------------------------------------------------------- loading

    private static async Task LoadAsync(TuiContext context, DiskCardsState state, bool includeMaintenance)
    {
        int merged = 0;
        List<DiskCard> cards;
        try
        {
            cards = await context.UseScopedAsync(async services =>
            {
                var repository = services.GetRequiredService<IDiskCardRepository>();
                if (includeMaintenance)
                {
                    merged = await repository.MergeDuplicateCardsAsync().ConfigureAwait(false);
                }

                return await repository.GetAllAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state.Cards = new List<DiskCard>();
            state.LoadError = ex.Message;
            context.StatusMessage = context.L("DiskCards.Status.Error", ex.Message);
            return;
        }

        state.LoadError = null;
        state.Cards = cards
            .OrderByDescending(card => card.LastTestedAt)
            .ToList();
        state.MergedCount = merged;
        context.StatusMessage = merged > 0
            ? context.L("DiskCards.Status.Merged", merged.ToString(Invariant))
            : context.L("DiskCards.Status.Loading");
    }

    /// <summary>Applies the desktop filter rules (search text, grade, archived, locked).</summary>
    internal static List<DiskCard> FilterCards(IEnumerable<DiskCard> cards, string? searchText, string? grade, bool lockedOnly, bool archivedOnly)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var result = new List<DiskCard>();
        foreach (DiskCard card in cards)
        {
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string needle = searchText.Trim().ToUpperInvariant();
                bool matches = (card.ModelName ?? string.Empty).ToUpperInvariant().Contains(needle, StringComparison.Ordinal)
                    || (card.SerialNumber ?? string.Empty).ToUpperInvariant().Contains(needle, StringComparison.Ordinal)
                    || (card.Notes ?? string.Empty).ToUpperInvariant().Contains(needle, StringComparison.Ordinal);
                if (!matches)
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(grade)
                && !string.Equals(card.OverallGrade, grade, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (archivedOnly && !card.IsArchived)
            {
                continue;
            }

            if (lockedOnly && !card.IsLocked)
            {
                continue;
            }

            result.Add(card);
        }

        return result;
    }

    private static void ApplyFilters(TuiContext context, DiskCardsState state)
    {
        state.Filtered = FilterCards(state.Cards, state.SearchText, state.Grade, state.LockedOnly, state.ArchivedOnly);

        int archivedCount = state.Cards.Count(card => card.IsArchived);
        int activeCount = state.Cards.Count - archivedCount;
        string total = context.L("DiskCards.Status.Total", state.Cards.Count, activeCount, archivedCount);

        string status;
        if (state.Cards.Count > 0 && state.Filtered.Count == 0)
        {
            status = context.L("DiskCards.Status.NoMatch", state.Cards.Count, activeCount, archivedCount);
        }
        else if (state.Filtered.Count < state.Cards.Count)
        {
            status = context.L("DiskCards.Status.Filtered", state.Filtered.Count, state.Cards.Count, activeCount, archivedCount);
        }
        else
        {
            status = total;
        }

        // A refresh that merged duplicates keeps its report until the next refresh.
        if (state.MergedCount > 0)
        {
            status = context.L("DiskCards.Status.Merged", state.MergedCount.ToString(Invariant)) + " · " + status;
        }

        // Feedback about the last filter change is shown once, next to the summary.
        if (!string.IsNullOrEmpty(state.Notice))
        {
            status = state.Notice + " · " + status;
            state.Notice = null;
        }

        context.StatusMessage = status;
    }

    // --------------------------------------------------------------- rendering

    private static void Render(TuiContext context, DiskCardsState state)
    {
        ITuiIo io = context.Io;
        io.Line();
        io.Line($"[bold]{Markup.Escape(context.L("DiskCardDetailView.Title"))}[/]");
        io.Line($"[grey]{Markup.Escape(context.L("DiskCard.Description"))}[/]");
        io.Line($"[grey]{Markup.Escape(BuildFilterLine(context, state))}[/]");

        if (state.Filtered.Count == 0)
        {
            io.Line($"[yellow]{Markup.Escape(state.Cards.Count == 0 ? context.L("Tui.DiskCards.NoCards") : context.L("DiskCards.Status.NoMatch", state.Cards.Count, state.Cards.Count - state.Cards.Count(card => card.IsArchived), state.Cards.Count(card => card.IsArchived)))}[/]");
        }
        else
        {
            RenderTable(context, state);
        }

        if (!string.IsNullOrWhiteSpace(state.LoadError))
        {
            io.Line($"[{TuiTheme.Warning.ToMarkup()}]⚠ {Markup.Escape(context.L("Common.LoadFailed"))} {Markup.Escape(state.LoadError)}[/]");
        }

        io.Line($"[grey]{Markup.Escape(DiskSelectionScreen.BuildSystemInfoLine(context))}[/]");
        context.RenderStatusBar();
    }

    internal static string BuildFilterLine(TuiContext context, DiskCardsState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);

        var parts = new List<string>
        {
            context.L("Tui.DiskCards.Search") + ": " + (string.IsNullOrWhiteSpace(state.SearchText) ? context.L("Common.All") : state.SearchText),
            context.L("DiskCardDetailView.Grade") + ": " + (string.IsNullOrWhiteSpace(state.Grade) ? context.L("Common.All") : state.Grade),
            context.L("DiskCard.LockedOnly") + ": " + (state.LockedOnly ? context.L("Common.Yes") : context.L("Common.No")),
            context.L("DiskCard.OnlyArchived") + ": " + (state.ArchivedOnly ? context.L("Common.Yes") : context.L("Common.No")),
        };

        return string.Join(" · ", parts);
    }

    private static void RenderTable(TuiContext context, DiskCardsState state)
    {
        int width = Math.Max(80, context.Io.Width);
        int budget = Math.Max(24, width - DiskSelectionScreen.BordersAndPadding);
        bool compact = width < 108;

        // The widths are allocated so the rendered table fits the terminal: the
        // narrow layout drops the statistics columns and keeps the identity ones.
        int[] sizes = compact
            ? DiskSelectionScreen.AllocateWidths(budget - CompactFixedWidth, new[] { 5, 3 }, new[] { 0.6, 0.4 })
            : DiskSelectionScreen.AllocateWidths(budget - FullFixedWidth, new[] { 5, 7, 3 }, new[] { 0.45, 0.25, 0.30 });

        var table = new Table
        {
            Border = TableBorder.Rounded,
            BorderStyle = new Style(foreground: TuiTheme.Muted),
        };

        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Model")) { NoWrap = true });
        if (!compact)
        {
            table.AddColumn(new TableColumn(Header(context, "Tui.Column.Manufacturer")) { NoWrap = true });
        }

        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Serial")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Capacity")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Grade")) { NoWrap = true });
        table.AddColumn(new TableColumn(Header(context, "Tui.Column.Score")) { NoWrap = true });

        if (!compact)
        {
            table.AddColumn(new TableColumn(Header(context, "Tui.Column.PowerOnHours")) { NoWrap = true });
            table.AddColumn(new TableColumn(Header(context, "Tui.Column.Cycles")) { NoWrap = true });
            table.AddColumn(new TableColumn(Header(context, "Tui.Column.Tests")) { NoWrap = true });
        }

        table.AddColumn(new TableColumn(Header(context, "Tui.Column.LastTested")) { NoWrap = true });

        int modelWidth = sizes[0];
        int manufacturerWidth = compact ? 0 : sizes[1];
        int serialWidth = compact ? sizes[1] : sizes[2];

        foreach (DiskCard card in state.Filtered)
        {
            var cells = new List<IRenderable>
            {
                new Markup(Cell(card.ModelName, modelWidth)),
            };

            if (!compact)
            {
                cells.Add(new Markup(Cell(card.ManufacturerDisplay, manufacturerWidth)));
            }

            cells.Add(new Markup(Cell(card.SerialNumber, serialWidth)));
            cells.Add(new Markup(Markup.Escape(TuiFormats.Capacity(card.Capacity))));
            cells.Add(new Markup(TuiTheme.GradeMarkup(card.OverallGrade)));
            cells.Add(new Markup(TuiTheme.ScoreMarkup(card.OverallScore)));

            if (!compact)
            {
                cells.Add(new Markup(Markup.Escape(FormatOptional(card.PowerOnHours))));
                cells.Add(new Markup(Markup.Escape(FormatOptional(card.PowerCycleCount))));
                cells.Add(new Markup(Markup.Escape(card.TestCount.ToString(Invariant))));
            }

            cells.Add(new Markup(Cell(FormatDate(card.LastTestedAt), LastTestedWidth)));
            table.AddRow(cells);
        }

        context.Io.Write(table);
    }

    private const int CapacityWidth = 8;
    private const int GradeWidth = 6;
    private const int ScoreWidth = 5;
    private const int LastTestedWidth = 13;

    private const int FullFixedWidth = CapacityWidth + GradeWidth + ScoreWidth + LastTestedWidth + 6 + 5 + 5;
    private const int CompactFixedWidth = CapacityWidth + GradeWidth + ScoreWidth + LastTestedWidth;

    private static string Header(TuiContext context, string key) =>
        "[grey]" + Markup.Escape(context.L(key)) + "[/]";

    private static string Cell(string? value, int width) =>
        string.IsNullOrWhiteSpace(value) ? "[grey]–[/]" : Markup.Escape(TuiFormats.Ellipsis(value, width));

    private static string FormatOptional(int? value) =>
        value is null ? "N/A" : value.Value.ToString(Invariant);

    /// <summary>Compact date used by the card table; the detail screen shows full timestamps.</summary>
    private static string FormatDate(DateTime value) =>
        value == default ? "N/A" : value.ToString("dd.MM.yyyy", Invariant);

    // ---------------------------------------------------------------- actions

    private static List<TuiChoice> BuildChoices(TuiContext context, DiskCardsState state)
    {
        var choices = new List<TuiChoice>
        {
            new(ActionRefresh, "🔄 " + context.L("Common.Refresh")),
            new(ActionSearch, "🔎 " + context.L("Common.Search")),
            new(ActionGrade, "🎚 " + context.L("Tui.DiskCards.GradeFilter")),
            new(ActionLocked, "🔒 " + context.L("DiskCard.OnlyLocked")),
            new(ActionArchived, "🗄 " + context.L("DiskCard.OnlyArchived")),
        };

        for (int index = 0; index < state.Filtered.Count; index++)
        {
            DiskCard card = state.Filtered[index];
            var label = new StringBuilder();
            label.Append(index + 1).Append(". ").Append(TuiFormats.Ellipsis(card.ModelName, 32));
            label.Append(" · ").Append(card.OverallGrade ?? "?");
            label.Append(" · ").Append(TuiFormats.Ellipsis(card.SerialNumber, 18));
            if (card.IsArchived)
            {
                label.Append(" · ").Append(context.L("Common.Archived"));
            }

            choices.Add(new TuiChoice(
                CardKeyPrefix + index.ToString(Invariant),
                label.ToString(),
                TuiFormats.Capacity(card.Capacity)));
        }

        choices.Add(new TuiChoice(TuiKeys.MainMenu, "☰ " + context.L("Tui.Common.Menu")));
        choices.Add(new TuiChoice(TuiKeys.Back, context.L("Common.Back")));
        return choices;
    }

    private static bool TryResolveCard(string choice, DiskCardsState state, out DiskCard card)
    {
        card = null!;
        if (!choice.StartsWith(CardKeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(choice[CardKeyPrefix.Length..], NumberStyles.Integer, Invariant, out int index))
        {
            return false;
        }

        if (index < 0 || index >= state.Filtered.Count)
        {
            return false;
        }

        card = state.Filtered[index];
        return true;
    }

    private static void PromptSearch(TuiContext context, DiskCardsState state)
    {
        string? answer = context.Dialogs.Ask(context.L("Common.Search"), state.SearchText ?? string.Empty);
        if (answer is null)
        {
            return;
        }

        state.SearchText = string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
        state.Notice = context.L("Tui.DiskCards.SearchApplied", state.SearchText ?? context.L("Common.All"));
    }

    private static void PromptGradeFilter(TuiContext context, DiskCardsState state)
    {
        var choices = new List<TuiChoice> { new("grade:all", context.L("Common.All")) };
        foreach (string grade in Grades)
        {
            choices.Add(new TuiChoice("grade:" + grade, grade));
        }

        string? answer = context.Io.Choose(context.L("Tui.DiskCards.GradeFilter"), choices);
        if (answer is null || answer == TuiKeys.Back || answer == "grade:all")
        {
            state.Grade = null;
            return;
        }

        state.Grade = answer.StartsWith("grade:", StringComparison.Ordinal) ? answer["grade:".Length..] : null;
        state.Notice = context.L("DiskCardDetailView.Grade") + ": " + (state.Grade ?? context.L("Common.All"));
    }

    private static async Task<ScreenResult?> RunCardActionsAsync(TuiContext context, DiskCardsState state, DiskCard card)
    {
        var choices = new List<TuiChoice>
        {
            new(CardActionDetail, "📋 " + context.L("DiskCard.Title")),
            card.IsArchived
                ? new TuiChoice(CardActionRestore, "♻ " + context.L("DiskCards.Restore.Title"))
                : new TuiChoice(CardActionArchive, "🗄 " + context.L("DiskCards.Archive.Title")),
            new(CardActionCertificate, "🏅 " + context.L("Nav.Certificates")),
            new(TuiKeys.Back, context.L("Common.Back")),
        };

        string? choice = context.Io.Choose(
            context.L("Tui.DiskCards.CardActions", card.ModelName),
            choices);

        if (choice is null || choice == TuiKeys.Back)
        {
            return null;
        }

        switch (choice)
        {
            case CardActionDetail:
                OpenDetail(context, card);
                return ScreenResult.Navigate(ScreenIds.DiskCardDetail);

            case CardActionArchive:
                await ArchiveAsync(context, state, card).ConfigureAwait(false);
                return null;

            case CardActionRestore:
                await RestoreAsync(context, state, card).ConfigureAwait(false);
                return null;

            case CardActionCertificate:
                return await OpenCertificateAsync(context, card).ConfigureAwait(false);

            default:
                return null;
        }
    }

    private static void OpenDetail(TuiContext context, DiskCard card)
    {
        context.Selection.SelectedDisk = new CoreDriveInfo
        {
            Path = card.DevicePath,
            Name = card.ModelName,
            TotalSize = card.Capacity,
            SerialNumber = card.SerialNumber,
            FirmwareVersion = card.FirmwareVersion,
        };
        context.Selection.SelectedDiskDisplayName = card.ModelName;
        context.Selection.IsSelectedDiskLocked = card.IsLocked;
        // The card identity is the stable key; it must be written after the disk.
        context.Selection.SelectedDiskCardId = card.Id;
        context.StatusMessage = context.L("DiskCards.Status.DiskSelected", card.ModelName, card.SerialNumber);
    }

    private static async Task ArchiveAsync(TuiContext context, DiskCardsState state, DiskCard card)
    {
        if (card.IsArchived)
        {
            return;
        }

        bool confirmed = context.Dialogs.Confirm(
            context.L("DiskCards.Archive.Title"),
            context.L("DiskCards.Archive.ConfirmMessage") + Environment.NewLine + card.ModelName + " · " + card.SerialNumber);
        if (!confirmed)
        {
            return;
        }

        string? reasonText = context.Dialogs.Ask(
            context.L("DiskCards.Archive.ReasonPrompt"),
            context.L("DiskCards.Archive.DefaultReason"));
        ArchiveReason reason = MapArchiveReason(context, reasonText);

        try
        {
            using var navigationLock = context.LockNavigation(context.L("DiskCards.Archive.Title"));
            await context.UseScopedAsync(async services =>
            {
                var repository = services.GetRequiredService<IDiskCardRepository>();
                await repository.ArchiveAsync(card.Id, reason, reasonText).ConfigureAwait(false);
            }).ConfigureAwait(false);

            context.StatusMessage = context.L("DiskCards.Status.Archived");
            context.Dialogs.Success(context.L("DiskCards.Archive.Title"), context.L("DiskCards.Status.Archived"));
            await LoadAsync(context, state, includeMaintenance: false).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Dialogs.Error(context.L("Common.Error"), context.L("DiskCards.Error.ArchiveFailed", ex.Message));
        }
    }

    private static async Task RestoreAsync(TuiContext context, DiskCardsState state, DiskCard card)
    {
        if (!card.IsArchived)
        {
            return;
        }

        bool confirmed = context.Dialogs.Confirm(
            context.L("DiskCards.Restore.Title"),
            context.L("DiskCards.Restore.ConfirmMessage") + Environment.NewLine + card.ModelName + " · " + card.SerialNumber);
        if (!confirmed)
        {
            return;
        }

        try
        {
            using var navigationLock = context.LockNavigation(context.L("DiskCards.Restore.Title"));
            await context.UseScopedAsync(async services =>
            {
                var repository = services.GetRequiredService<IDiskCardRepository>();
                await repository.RestoreAsync(card.Id).ConfigureAwait(false);
            }).ConfigureAwait(false);

            context.StatusMessage = context.L("DiskCards.Status.Restored");
            context.Dialogs.Success(context.L("DiskCards.Restore.Title"), context.L("DiskCards.Status.Restored"));
            await LoadAsync(context, state, includeMaintenance: false).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Dialogs.Error(context.L("Common.Error"), context.L("DiskCards.Error.RestoreFailed", ex.Message));
        }
    }

    private static async Task<ScreenResult?> OpenCertificateAsync(TuiContext context, DiskCard card)
    {
        if (card.TestCount == 0)
        {
            context.Dialogs.Warning(context.L("Nav.Certificates"), context.L("Common.DiskNotTested"));
            return null;
        }

        try
        {
            (int? sessionId, int? certificateId) identity = await context.UseScopedAsync(async services =>
            {
                var repository = services.GetRequiredService<IDiskCardRepository>();
                List<TestSession> sessions = await repository.GetTestSessionsAsync(card.Id).ConfigureAwait(false);
                int? latestSessionId = sessions
                    .OrderByDescending(session => session.StartedAt)
                    .Select(session => (int?)session.Id)
                    .FirstOrDefault();
                DiskCertificate? certificate = await repository.GetLatestCertificateAsync(card.Id).ConfigureAwait(false);
                return (latestSessionId, certificate?.Id);
            }).ConfigureAwait(false);

            context.Selection.SelectedDisk = new CoreDriveInfo
            {
                Path = card.DevicePath,
                Name = card.ModelName,
                TotalSize = card.Capacity,
                SerialNumber = card.SerialNumber,
                FirmwareVersion = card.FirmwareVersion,
            };
            context.Selection.SelectedDiskDisplayName = card.ModelName;
            context.Selection.IsSelectedDiskLocked = card.IsLocked;
            context.Selection.SelectedDiskCardId = card.Id;
            context.Selection.SelectedTestSessionId = identity.sessionId;
            context.Selection.SelectedCertificateId = identity.certificateId;
            return ScreenResult.Navigate(ScreenIds.Certificate);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Dialogs.Error(context.L("Common.Error"), context.L("Common.LoadFailed") + " " + ex.Message);
            return null;
        }
    }

    /// <summary>Maps the localized archive reason onto the persisted enumeration.</summary>
    internal static ArchiveReason MapArchiveReason(TuiContext context, string? reasonText)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(reasonText))
        {
            return ArchiveReason.Other;
        }

        string value = reasonText.Trim();
        if (Matches(context, value, "DiskCards.Archive.Reason.Failed"))
        {
            return ArchiveReason.Failed;
        }

        if (Matches(context, value, "DiskCards.Archive.Reason.Sold"))
        {
            return ArchiveReason.Sold;
        }

        if (Matches(context, value, "DiskCards.Archive.Reason.Donated"))
        {
            return ArchiveReason.Donated;
        }

        if (Matches(context, value, "DiskCards.Archive.Reason.Recycled"))
        {
            return ArchiveReason.Recycled;
        }

        if (Matches(context, value, "DiskCards.Archive.Reason.Replaced"))
        {
            return ArchiveReason.Replaced;
        }

        return ArchiveReason.Other;
    }

    private static bool Matches(TuiContext context, string value, string key) =>
        string.Equals(value, context.L(key), StringComparison.OrdinalIgnoreCase);

    /// <summary>Mutable state of one screen run.</summary>
    internal sealed class DiskCardsState
    {
        public List<DiskCard> Cards { get; set; } = new();

        public List<DiskCard> Filtered { get; set; } = new();

        public string? SearchText { get; set; }

        public string? Grade { get; set; }

        public bool LockedOnly { get; set; }

        public bool ArchivedOnly { get; set; }

        public int MergedCount { get; set; }

        /// <summary>One-shot feedback about the last filter change.</summary>
        public string? Notice { get; set; }

        public string? LoadError { get; set; }
    }
}
