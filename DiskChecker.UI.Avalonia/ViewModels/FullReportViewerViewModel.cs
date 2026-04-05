using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// ViewModel pro interní zobrazení a tisk plného JSON reportu.
/// </summary>
public sealed class FullReportViewerViewModel : ViewModelBase, INavigableViewModel
{
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly ReportDocumentState _reportDocumentState;

    private string _statusMessage = "Připraven";
    private string _reportPath = string.Empty;
    private string _reportContent = string.Empty;
    private bool _isLoading;

    /// <summary>
    /// Inicializuje novou instanci třídy <see cref="FullReportViewerViewModel"/>.
    /// </summary>
    public FullReportViewerViewModel(
        INavigationService navigationService,
        IDialogService dialogService,
        ReportDocumentState reportDocumentState)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _reportDocumentState = reportDocumentState ?? throw new ArgumentNullException(nameof(reportDocumentState));

        ReloadCommand = new AsyncRelayCommand(LoadReportAsync);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => !string.IsNullOrWhiteSpace(ReportPath));
        OpenExternalCommand = new AsyncRelayCommand(OpenExternalAsync, () => !string.IsNullOrWhiteSpace(ReportPath));
        BackCommand = new RelayCommand(GoBack);
    }

    /// <summary>
    /// Stavová zpráva zobrazená v UI.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Cesta k otevřenému reportu.
    /// </summary>
    public string ReportPath
    {
        get => _reportPath;
        set
        {
            if (SetProperty(ref _reportPath, value))
            {
                PrintCommand.NotifyCanExecuteChanged();
                OpenExternalCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Obsah reportu zobrazený v textovém náhledu.
    /// </summary>
    public string ReportContent
    {
        get => _reportContent;
        set => SetProperty(ref _reportContent, value);
    }

    /// <summary>
    /// Indikuje probíhající načítání.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// Příkaz pro opětovné načtení reportu.
    /// </summary>
    public IAsyncRelayCommand ReloadCommand { get; }

    /// <summary>
    /// Příkaz pro tisk reportu.
    /// </summary>
    public IAsyncRelayCommand PrintCommand { get; }

    /// <summary>
    /// Příkaz pro otevření reportu externí aplikací.
    /// </summary>
    public IAsyncRelayCommand OpenExternalCommand { get; }

    /// <summary>
    /// Příkaz pro návrat na seznam reportů.
    /// </summary>
    public IRelayCommand BackCommand { get; }

    /// <inheritdoc />
    public void OnNavigatedTo()
    {
        _ = LoadReportAsync();
    }

    private async Task LoadReportAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Načítám report...";

            if (!_reportDocumentState.HasReport || string.IsNullOrWhiteSpace(_reportDocumentState.LastReportPath))
            {
                ReportPath = string.Empty;
                ReportContent = "";
                StatusMessage = "Nebyl nalezen žádný report pro zobrazení.";
                return;
            }

            ReportPath = _reportDocumentState.LastReportPath;
            ReportContent = await File.ReadAllTextAsync(ReportPath);
            StatusMessage = $"Report načten: {Path.GetFileName(ReportPath)}";
        }
        catch (IOException ex)
        {
            StatusMessage = $"Chyba při načítání reportu: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst report: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = $"Chyba při načítání reportu: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst report: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PrintAsync()
    {
        if (string.IsNullOrWhiteSpace(ReportPath) || !File.Exists(ReportPath))
        {
            await _dialogService.ShowWarningAsync("Tisk", "Report není dostupný pro tisk.");
            return;
        }

        try
        {
            var htmlPath = await CreatePrintableHtmlAsync(ReportPath, ReportContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = htmlPath,
                UseShellExecute = true
            });

            StatusMessage = "Report byl otevřen pro tisk v externí aplikaci";
            await _dialogService.ShowInfoAsync(
                "Tisk",
                "Report byl otevřen ve výchozí aplikaci. Pro bezpečný tisk použijte tisk přímo v otevřeném okně (Ctrl+P). Automatický shell tisk byl vypnut kvůli přetížení systému.");
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Chyba tisku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se otevřít report pro tisk: {ex.Message}");
        }
        catch (Win32Exception ex)
        {
            StatusMessage = $"Chyba tisku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se otevřít report pro tisk: {ex.Message}");
        }
        catch (IOException ex)
        {
            StatusMessage = $"Chyba tisku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se otevřít report pro tisk: {ex.Message}");
        }
    }

    private async Task OpenExternalAsync()
    {
        if (string.IsNullOrWhiteSpace(ReportPath) || !File.Exists(ReportPath))
        {
            await _dialogService.ShowWarningAsync("Report", "Report není dostupný.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ReportPath,
                UseShellExecute = true
            });

            StatusMessage = "Report otevřen externě";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Chyba při otevírání reportu: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se otevřít report: {ex.Message}");
        }
        catch (Win32Exception ex)
        {
            StatusMessage = $"Chyba při otevírání reportu: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se otevřít report: {ex.Message}");
        }
    }

    private void GoBack()
    {
        _navigationService.NavigateTo<ReportViewModel>();
    }

    private static async Task<string> CreatePrintableHtmlAsync(string reportPath, string reportContent)
    {
        var printDirectory = Path.Combine(Path.GetTempPath(), "DiskChecker", "Print");
        Directory.CreateDirectory(printDirectory);

        var htmlPath = Path.Combine(
            printDirectory,
            $"report_print_{DateTime.UtcNow:yyyyMMddHHmmss}.html");

        var safeFileName = WebUtility.HtmlEncode(Path.GetFileName(reportPath));
        var safeContent = WebUtility.HtmlEncode(reportContent);

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html><head><meta charset=\"utf-8\"><title>DiskChecker Report</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:Consolas,Menlo,monospace;background:#fff;color:#111;margin:0;padding:24px;}");
        html.AppendLine("h1{font-family:Segoe UI,Arial,sans-serif;font-size:22px;margin:0 0 12px 0;}");
        html.AppendLine(".meta{font-family:Segoe UI,Arial,sans-serif;color:#555;margin-bottom:16px;}");
        html.AppendLine("pre{white-space:pre-wrap;word-break:break-word;font-size:12px;line-height:1.45;border:1px solid #ddd;padding:12px;border-radius:6px;}");
        html.AppendLine("@media print{body{padding:0;}pre{border:none;padding:0;}}");
        html.AppendLine("</style></head><body>");
        html.AppendLine($"<h1>DiskChecker - Plný report</h1><div class=\"meta\">Soubor: {safeFileName}</div>");
        html.AppendLine($"<pre>{safeContent}</pre>");
        html.AppendLine("</body></html>");

        await File.WriteAllTextAsync(htmlPath, html.ToString(), Encoding.UTF8);
        return htmlPath;
    }
}
