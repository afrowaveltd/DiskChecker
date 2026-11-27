using System.Text;
using DiskChecker.Core.Interfaces;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Optional disk preparation offered only after a destructive test and its
/// certificate have been persisted. Never part of the raw testing pass.
/// </summary>
public static class PostTestPartitionPreparation
{
    public static async Task<SanitizationResult?> OfferAsync(
        IDialogService dialogs,
        IDiskSanitizationService sanitizer,
        string devicePath,
        string diskName,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task>? verifyTarget = null)
    {
        // No is the default safe action; a completed test is valid without a filesystem.
        if (!await dialogs.ShowConfirmationAsync(
                "Příprava testovaného disku",
                $"Test a certifikát jsou uložené. Chcete na disku {diskName} ({devicePath}) vytvořit nový GPT oddíl a souborový systém?"))
            return null;

        var formats = await sanitizer.GetSupportedFileSystemsAsync(cancellationToken);
        if (formats.Count == 0)
        {
            await dialogs.ShowWarningAsync("Formátování není dostupné",
                "V této instalaci nejsou dostupné nástroje pro vytvoření podporovaného souborového systému.");
            return new SanitizationResult { ErrorMessage = "Chybí nástroje pro formátování." };
        }

        string selected = formats[0];
        if (formats.Count > 1)
        {
            var menu = new StringBuilder("Zvolte číslo souborového systému, který tato instalace podporuje:");
            for (int i = 0; i < formats.Count; i++)
                menu.AppendLine().Append(i + 1).Append(". ").Append(formats[i]);

            var answer = await dialogs.ShowPromptAsync("Souborový systém", menu.ToString(), "1");
            if (answer == null)
                return null;

            if (!int.TryParse(answer, out int number) || number < 1 || number > formats.Count)
            {
                await dialogs.ShowWarningAsync("Neplatná volba", "Vyberte číslo některé z nabízených možností.");
                return null;
            }
            selected = formats[number - 1];
        }

        if (!await dialogs.ShowDangerConfirmationAsync(
                "Potvrzení formátování",
                $"Na disku {diskName}\n{devicePath}\nse vytvoří nový GPT oddíl přes celý disk a naformátuje jako {selected} (jméno SCCM).\n\nDosavadní tabulka oddílů a jejich obsah budou zničeny. Pokračovat?"))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        if (verifyTarget != null)
            await verifyTarget(devicePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await sanitizer.CreatePartitionWithFileSystemAsync(
            devicePath, selected, "SCCM", cancellationToken: cancellationToken);
    }
}
