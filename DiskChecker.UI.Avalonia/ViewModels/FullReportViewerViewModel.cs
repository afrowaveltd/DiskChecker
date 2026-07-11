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

    private string _statusMessage = string.Empty;
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

        StatusMessage = L.Get("Common.Ready");

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
            StatusMessage = L.Get("FullReportViewer.Loading");

            if (!_reportDocumentState.HasReport || string.IsNullOrWhiteSpace(_reportDocumentState.LastReportPath))
            {
                ReportPath = string.Empty;
                ReportContent = "";
                StatusMessage = L.Get("FullReportViewer.NoReportFound");
                return;
            }

            ReportPath = _reportDocumentState.LastReportPath;
            ReportContent = await File.ReadAllTextAsync(ReportPath);
            StatusMessage = string.Format(L.Get("FullReportViewer.ReportLoaded"), Path.GetFileName(ReportPath));
        }
        catch (IOException ex)
        {
            StatusMessage = string.Format(L.Get("FullReportViewer.LoadError"), ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.OpenFailed"), ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = string.Format(L.Get("FullReportViewer.LoadError"), ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.OpenFailed"), ex.Message));
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
            await _dialogService.ShowWarningAsync(L.Get("Common.Print"), L.Get("Common.PrintNotAvailable"));
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

            StatusMessage = L.Get("FullReportViewer.PrintOpened");
            await _dialogService.ShowInfoAsync(
                L.Get("Common.Print"),
                L.Get("FullReportViewer.PrintMessage"));
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = string.Format(L.Get("FullReportViewer.PrintError"), ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.OpenFailed"), ex.Message));
        }
        catch (Win32Exception ex)
        {
            StatusMessage = string.Format(L.Get("FullReportViewer.PrintError"), ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.OpenFailed"), ex.Message));
        }
        catch (IOException ex)
        {
            StatusMessage = string.Format(L.Get("FullReportViewer.PrintError"), ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.OpenFailed"), ex.Message));
        }
    }

    private async Task OpenExternalAsync()
    {
        if (string.IsNullOrWhiteSpace(ReportPath) || !File.Exists(ReportPath))
        {
            await _dialogService.ShowWarningAsync(L.Get("Common.Report"), L.Get("Common.ReportNotAvailable"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ReportPath,
                UseShellExecute = true
            });

            StatusMessage = L.Get("FullReportViewer.ExternallyOpened");
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = string.Format(L.Get("FullReportViewer.OpenError"), ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.OpenFailed"), ex.Message));
        }
        catch (Win32Exception ex)
        {
            StatusMessage = string.Format(L.Get("FullReportViewer.OpenError"), ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.OpenFailed"), ex.Message));
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
