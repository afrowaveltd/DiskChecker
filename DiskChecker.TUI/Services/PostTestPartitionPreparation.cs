using System.Text;
using DiskChecker.Core.Interfaces;

namespace DiskChecker.TUI.Services;

/// <summary>Interactive, explicitly confirmed GPT preparation after persisted testing.</summary>
internal static class PostTestPartitionPreparation
{
    public static async Task<SanitizationResult?> OfferAsync(
        TuiContext context,
        IDiskSanitizationService sanitizer,
        string devicePath,
        string diskName,
        CancellationToken cancellationToken)
    {
        if (!context.Dialogs.Confirm(
                "Příprava disku po testu",
                $"Výsledky a certifikát jsou uložené. Vytvořit na {diskName} ({devicePath}) nový GPT oddíl a souborový systém?",
                danger: false))
            return null;

        var formats = await sanitizer.GetSupportedFileSystemsAsync(cancellationToken);
        if (formats.Count == 0)
        {
            context.Dialogs.Warning("Formátování není dostupné", "Na tomto systému nejsou dostupné nástroje mkfs.");
            return new SanitizationResult { ErrorMessage = "Chybí formátovací nástroje." };
        }

        string selected = formats[0];
        if (formats.Count > 1)
        {
            var menu = new StringBuilder("Zvolte číslo souborového systému (prázdné = zrušit):");
            for (int i = 0; i < formats.Count; i++)
                menu.Append('\n').Append(i + 1).Append(". ").Append(formats[i]);

            var answer = context.Dialogs.Ask(menu.ToString(), "1");
            if (answer == null)
                return null;
            if (!int.TryParse(answer, out int number) || number < 1 || number > formats.Count)
            {
                context.Dialogs.Warning("Neplatná volba", "Zadejte číslo uvedeného souborového systému.");
                return null;
            }
            selected = formats[number - 1];
        }

        if (!context.Dialogs.Confirm(
                "NEVRATNÉ FORMÁTOVÁNÍ",
                $"Disk: {diskName}\nZařízení: {devicePath}\nGPT, celý disk, {selected}, název SCCM.\n\nPůvodní oddíly budou zničeny. Opravdu pokračovat?",
                danger: true))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        return await sanitizer.CreatePartitionWithFileSystemAsync(
            devicePath, selected, "SCCM", cancellationToken: cancellationToken);
    }
}
