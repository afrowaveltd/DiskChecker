using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Fáze export/import procesu.
/// </summary>
public enum ExportImportPhase
{
    Idle,
    Exporting,
    Importing,
    Completed,
    Failed
}

/// <summary>
/// ViewModel pro modul exportu a importu databázových dat.
/// </summary>
public partial class ExportImportViewModel : ViewModelBase, INavigableViewModel, IDisposable
{
    private readonly IDataExportImportService _exportImportService;
    private readonly IDiskCardRepository _repository;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly IBackupService _backupService;

    private CancellationTokenSource? _operationCts;
    private bool _disposed;

    [ObservableProperty] private ExportImportPhase _phase = ExportImportPhase.Idle;
    [ObservableProperty] private string _statusMessage = "Připraven k exportu/importu";

    // Export
    [ObservableProperty] private ExportScope _exportScope = ExportScope.All;
    [ObservableProperty] private bool _isExportAll = true;
    [ObservableProperty] private bool _isExportMeasurements;
    [ObservableProperty] private bool _isExportSelected;
    [ObservableProperty] private string _exportFilePath = string.Empty;
    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private string _exportLogText = string.Empty;

    // Import
    [ObservableProperty] private ImportMode _importMode = ImportMode.Add;
    [ObservableProperty] private bool _isImportAdd = true;
    [ObservableProperty] private bool _isImportReplace;
    [ObservableProperty] private string _importFilePath = string.Empty;
    [ObservableProperty] private double _importProgress;
    [ObservableProperty] private string _importLogText = string.Empty;
    [ObservableProperty] private string _importPreviewText = string.Empty;

    // Lokální záloha
    [ObservableProperty] private string _backupPath = string.Empty;
    [ObservableProperty] private string _backupLogText = string.Empty;
    [ObservableProperty] private bool _isLoadingBackups;
    [ObservableProperty] private IBackupService.BackupInfo? _selectedLocalBackup;
    public ObservableCollection<IBackupService.BackupInfo> AvailableLocalBackups { get; } = new();

    // Výsledek
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _isResultSuccess;

    public ObservableCollection<SelectableDiskCard> AvailableDisks { get; } = new();
    public ObservableCollection<SelectableDiskCard> SelectedDisks => new(AvailableDisks.Where(d => d.IsSelected));

    private readonly List<string> _log = new();

    public IAsyncRelayCommand ExportCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand GoBackCommand { get; }
    public IAsyncRelayCommand BrowseExportPathCommand { get; }
    public IAsyncRelayCommand BrowseImportPathCommand { get; }
    public IAsyncRelayCommand PreviewImportCommand { get; }
    public IAsyncRelayCommand LoadDisksCommand { get; }
    public IAsyncRelayCommand CreateLocalBackupCommand { get; }
    public IAsyncRelayCommand RestoreLocalBackupCommand { get; }
    public IAsyncRelayCommand RefreshLocalBackupsCommand { get; }
    public IAsyncRelayCommand BrowseBackupPathCommand { get; }

    public ExportImportViewModel(
        IDataExportImportService exportImportService,
        IDiskCardRepository repository,
        IDialogService dialogService,
        INavigationService navigationService,
        IBackupService backupService)
    {
        _exportImportService = exportImportService;
        _repository = repository;
        _dialogService = dialogService;
        _navigationService = navigationService;
        _backupService = backupService;

        ExportCommand = new AsyncRelayCommand(ExecuteExportAsync, () => Phase == ExportImportPhase.Idle);
        ImportCommand = new AsyncRelayCommand(ExecuteImportAsync, () => Phase == ExportImportPhase.Idle);
        CancelCommand = new RelayCommand(CancelOperation);
        GoBackCommand = new RelayCommand(GoBack);
        BrowseExportPathCommand = new AsyncRelayCommand(BrowseExportPathAsync);
        BrowseImportPathCommand = new AsyncRelayCommand(BrowseImportPathAsync);
        PreviewImportCommand = new AsyncRelayCommand(PreviewImportAsync);
        LoadDisksCommand = new AsyncRelayCommand(LoadDisksAsync);
        CreateLocalBackupCommand = new AsyncRelayCommand(CreateLocalBackupAsync, () => Phase == ExportImportPhase.Idle);
        RestoreLocalBackupCommand = new AsyncRelayCommand(RestoreLocalBackupAsync, () => Phase == ExportImportPhase.Idle && SelectedLocalBackup != null);
        RefreshLocalBackupsCommand = new AsyncRelayCommand(RefreshLocalBackupsAsync);
        BrowseBackupPathCommand = new AsyncRelayCommand(BrowseBackupPathAsync);
    }

    public void OnNavigatedTo()
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadDisksAsync();
        StatusMessage = $"Připraveno. {AvailableDisks.Count} disků v databázi.";
    }

    private async Task LoadDisksAsync()
    {
        try
        {
            var disks = await _repository.GetAllAsync();
            AvailableDisks.Clear();
            foreach (var disk in disks.OrderBy(d => d.ModelName))
                AvailableDisks.Add(new SelectableDiskCard(disk));
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst disky: {ex.Message}");
        }
    }

    partial void OnIsExportAllChanged(bool value)
    {
        if (value) ExportScope = ExportScope.All;
    }

    partial void OnIsExportMeasurementsChanged(bool value)
    {
        if (value) ExportScope = ExportScope.MeasurementsAndDisks;
    }

    partial void OnIsExportSelectedChanged(bool value)
    {
        if (value) ExportScope = ExportScope.SelectedDisks;
    }

    partial void OnIsImportAddChanged(bool value)
    {
        if (value) ImportMode = ImportMode.Add;
    }

    partial void OnIsImportReplaceChanged(bool value)
    {
        if (value) ImportMode = ImportMode.Replace;
    }

    partial void OnSelectedLocalBackupChanged(IBackupService.BackupInfo? value)
    {
        RestoreLocalBackupCommand.NotifyCanExecuteChanged();
    }

    private async Task BrowseExportPathAsync()
    {
        try
        {
            var topLevel = global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel == null) return;

            var defaultDir = _exportImportService.GetDefaultExportDirectory();
            var defaultFile = $"DiskChecker_Export_{DateTime.Now:yyyyMMdd_HHmmss}.json";

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Uložit export dat",
                SuggestedFileName = defaultFile,
                DefaultExtension = ".json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON soubor") { Patterns = new[] { "*.json" } }
                },
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(defaultDir)
            });

            if (file != null)
            {
                ExportFilePath = file.Path.AbsolutePath;
                // Oprava formátu cesty z file:/// na normální cestu
                if (ExportFilePath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    ExportFilePath = Uri.UnescapeDataString(ExportFilePath[8..]);
                else if (ExportFilePath.StartsWith("file:\\", StringComparison.OrdinalIgnoreCase))
                    ExportFilePath = Uri.UnescapeDataString(ExportFilePath[7..]);
            }
        }
        catch (Exception ex)
        {
            _log.Add($"[CHYBA] Výběr cesty exportu: {ex.Message}");
        }
    }

    private async Task BrowseImportPathAsync()
    {
        try
        {
            var topLevel = global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Vybrat soubor pro import",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON soubor") { Patterns = new[] { "*.json" } }
                }
            });

            if (files.Count > 0)
            {
                var path = files[0].Path.AbsolutePath;
                if (path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    path = Uri.UnescapeDataString(path[8..]);
                else if (path.StartsWith("file:\\", StringComparison.OrdinalIgnoreCase))
                    path = Uri.UnescapeDataString(path[7..]);

                ImportFilePath = path;
                await PreviewImportAsync();
            }
        }
        catch (Exception ex)
        {
            _log.Add($"[CHYBA] Výběr souboru pro import: {ex.Message}");
        }
    }

    private async Task PreviewImportAsync()
    {
        if (string.IsNullOrEmpty(ImportFilePath) || !File.Exists(ImportFilePath))
        {
            ImportPreviewText = "Soubor neexistuje.";
            return;
        }

        try
        {
            var meta = await _exportImportService.PeekMetadataAsync(ImportFilePath);
            if (meta == null)
            {
                ImportPreviewText = "❌ Neplatný exportní soubor nebo chybí metadata.";
                return;
            }

            ImportPreviewText =
                $"📦 Verze: {meta.Version}\n" +
                $"📅 Exportováno: {meta.ExportedAt.ToLocalTime():dd.MM.yyyy HH:mm}\n" +
                $"💿 Disky: {meta.DiskCount}\n" +
                $"🧪 Test session: {meta.TestSessionCount}\n" +
                $"📜 Certifikáty: {meta.CertificateCount}\n" +
                $"📏 Velikost: {FormatBytes(meta.TotalSizeBytes)}\n" +
                $"🎯 Rozsah: {meta.Scope}";
        }
        catch (Exception ex)
        {
            ImportPreviewText = $"❌ Chyba při čtení: {ex.Message}";
        }
    }

    private async Task ExecuteExportAsync()
    {
        if (Phase != ExportImportPhase.Idle) return;

        if (string.IsNullOrEmpty(ExportFilePath))
        {
            await _dialogService.ShowErrorAsync("Chyba", "Nejprve vyberte cílový soubor.");
            return;
        }

        if (ExportScope == ExportScope.SelectedDisks && SelectedDisks.Count == 0)
        {
            await _dialogService.ShowErrorAsync("Chyba", "Vyberte alespoň jeden disk k exportu.");
            return;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Potvrzení exportu",
            $"Exportovat data do:\n{ExportFilePath}\n\n" +
            $"Rozsah: {ExportScope switch { ExportScope.All => "Celá databáze", ExportScope.MeasurementsAndDisks => "Pouze měření a disky", ExportScope.SelectedDisks => $"Vybrané disky ({SelectedDisks.Count})", _ => "?" }}\n\n" +
            "Pokračovat?");

        if (!confirmed) return;

        _operationCts = new CancellationTokenSource();
        Phase = ExportImportPhase.Exporting;
        StatusMessage = "Probíhá export...";
        ExportProgress = 0;
        _log.Clear();
        _log.Add($"[{DateTime.Now:HH:mm:ss}] Zahajuji export...");

        try
        {
            var selectedIds = ExportScope == ExportScope.SelectedDisks
                ? SelectedDisks.Select(d => d.Id).ToList()
                : null;

            var result = await Task.Run(() =>
                _exportImportService.ExportAsync(ExportFilePath, ExportScope, selectedIds, _operationCts.Token));

            ExportProgress = 100;
            _log.Add($"[{DateTime.Now:HH:mm:ss}] Export dokončen: {result}");
            _log.Add($"[{DateTime.Now:HH:mm:ss}] Velikost souboru: {FormatBytes(new FileInfo(result).Length)}");

            Phase = ExportImportPhase.Completed;
            StatusMessage = "✅ Export dokončen!";
            ResultText = $"Export úspěšně dokončen.\nSoubor: {result}\nVelikost: {FormatBytes(new FileInfo(result).Length)}";
            IsResultSuccess = true;
        }
        catch (OperationCanceledException)
        {
            Phase = ExportImportPhase.Idle;
            StatusMessage = "Export zrušen.";
            _log.Add($"[{DateTime.Now:HH:mm:ss}] Export zrušen.");
        }
        catch (Exception ex)
        {
            Phase = ExportImportPhase.Failed;
            StatusMessage = $"❌ Export selhal: {ex.Message}";
            _log.Add($"[{DateTime.Now:HH:mm:ss}] CHYBA: {ex.Message}");
            ResultText = $"Export selhal: {ex.Message}";
            IsResultSuccess = false;
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            ExportLogText = string.Join("\n", _log);
        }
    }

    private async Task ExecuteImportAsync()
    {
        if (Phase != ExportImportPhase.Idle) return;

        if (string.IsNullOrEmpty(ImportFilePath) || !File.Exists(ImportFilePath))
        {
            await _dialogService.ShowErrorAsync("Chyba", "Nejprve vyberte platný soubor pro import.");
            return;
        }

        var modeText = ImportMode == ImportMode.Replace ? "nahradit existující záznamy" : "přidat nová data (zachovat existující)";
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Potvrzení importu",
            $"Importovat data z:\n{ImportFilePath}\n\n" +
            $"Režim: {modeText}\n\n" +
            "⚠️ Import může přepsat existující data!\n\n" +
            "Doporučujeme před importem provést zálohu databáze.\n\n" +
            "Pokračovat?");

        if (!confirmed) return;

        _operationCts = new CancellationTokenSource();
        Phase = ExportImportPhase.Importing;
        StatusMessage = "Probíhá import...";
        ImportProgress = 0;
        _log.Clear();
        _log.Add($"[{DateTime.Now:HH:mm:ss}] Zahajuji import...");
        _log.Add($"[{DateTime.Now:HH:mm:ss}] Režim: {modeText}");

        try
        {
            var result = await Task.Run(() =>
                _exportImportService.ImportAsync(ImportFilePath, ImportMode, _operationCts.Token));

            ImportProgress = 100;

            if (result.Success)
            {
                _log.Add($"[{DateTime.Now:HH:mm:ss}] Import dokončen.");
                _log.Add($"  Disky importováno: {result.DisksImported}");
                _log.Add($"  Disky přeskočeno: {result.DisksSkipped}");
                _log.Add($"  Test session importováno: {result.TestSessionsImported}");
                _log.Add($"  Test session přeskočeno: {result.TestSessionsSkipped}");
                _log.Add($"  Certifikáty importováno: {result.CertificatesImported}");
                _log.Add($"  Certifikáty přeskočeno: {result.CertificatesSkipped}");

                if (result.Warnings.Count > 0)
                {
                    _log.Add("  Varování:");
                    foreach (var w in result.Warnings)
                        _log.Add($"    ⚠ {w}");
                }

                Phase = ExportImportPhase.Completed;
                StatusMessage = "✅ Import dokončen!";
                ResultText =
                    $"✅ Import úspěšně dokončen.\n\n" +
                    $"💿 Disky: {result.DisksImported} importováno, {result.DisksSkipped} přeskočeno\n" +
                    $"🧪 Testy: {result.TestSessionsImported} importováno, {result.TestSessionsSkipped} přeskočeno\n" +
                    $"📜 Certifikáty: {result.CertificatesImported} importováno, {result.CertificatesSkipped} přeskočeno";
                IsResultSuccess = true;

                // Znovu načíst seznam disků
                await LoadDisksAsync();
            }
            else
            {
                _log.Add($"[{DateTime.Now:HH:mm:ss}] Import selhal: {result.ErrorMessage}");
                Phase = ExportImportPhase.Failed;
                StatusMessage = $"❌ Import selhal";
                ResultText = $"Import selhal: {result.ErrorMessage}";
                IsResultSuccess = false;
            }
        }
        catch (OperationCanceledException)
        {
            Phase = ExportImportPhase.Idle;
            StatusMessage = "Import zrušen.";
            _log.Add($"[{DateTime.Now:HH:mm:ss}] Import zrušen.");
        }
        catch (Exception ex)
        {
            Phase = ExportImportPhase.Failed;
            StatusMessage = $"❌ Import selhal: {ex.Message}";
            _log.Add($"[{DateTime.Now:HH:mm:ss}] CHYBA: {ex.Message}");
            ResultText = $"Import selhal: {ex.Message}";
            IsResultSuccess = false;
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            ImportLogText = string.Join("\n", _log);
        }
    }

    private void CancelOperation()
    {
        _operationCts?.Cancel();
        StatusMessage = "Přerušuji operaci...";
    }

    private void GoBack()
    {
        if (Phase == ExportImportPhase.Exporting || Phase == ExportImportPhase.Importing)
        {
            _ = _dialogService.ShowErrorAsync("Operace běží", "Nelze opustit stránku během probíhající operace.");
            return;
        }
        _navigationService.NavigateTo<SettingsViewModel>();
    }

    private async Task BrowseBackupPathAsync()
    {
        try
        {
            var topLevel = global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel == null) return;

            var dir = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Vybrat složku pro zálohu",
                AllowMultiple = false
            });

            if (dir.Count > 0)
            {
                var path = dir[0].Path.AbsolutePath;
                if (path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    path = Uri.UnescapeDataString(path[8..]);
                else if (path.StartsWith("file:\\", StringComparison.OrdinalIgnoreCase))
                    path = Uri.UnescapeDataString(path[7..]);

                BackupPath = path;
            }
        }
        catch (Exception ex)
        {
            _log.Add($"[CHYBA] Výběr cesty zálohy: {ex.Message}");
        }
    }

    private async Task CreateLocalBackupAsync()
    {
        if (Phase != ExportImportPhase.Idle) return;

        var destPath = string.IsNullOrEmpty(BackupPath)
            ? string.Empty
            : BackupPath;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Potvrzení zálohy",
            "Vytvořit lokální zálohu databáze?\n\n" +
            (string.IsNullOrEmpty(destPath)
                ? "Použije se výchozí adresář záloh."
                : $"Cíl: {destPath}") +
            "\n\nZáloha obsahuje databázi a nastavení aplikace.");

        if (!confirmed) return;

        _operationCts = new CancellationTokenSource();
        Phase = ExportImportPhase.Exporting;
        StatusMessage = "Vytvářím zálohu...";
        _log.Clear();
        _log.Add($"[{DateTime.Now:HH:mm:ss}] Vytvářím lokální zálohu...");

        try
        {
            var result = await Task.Run(() => _backupService.CreateBackupAsync(destPath), _operationCts.Token);

            _log.Add($"[{DateTime.Now:HH:mm:ss}] Záloha vytvořena: {result}");
            _log.Add($"[{DateTime.Now:HH:mm:ss}] Velikost: {FormatBytes(new FileInfo(result).Length)}");

            Phase = ExportImportPhase.Completed;
            StatusMessage = "✅ Záloha vytvořena!";
            ResultText = $"Záloha úspěšně vytvořena.\nSoubor: {result}\nVelikost: {FormatBytes(new FileInfo(result).Length)}";
            IsResultSuccess = true;

            await RefreshLocalBackupsAsync();
        }
        catch (OperationCanceledException)
        {
            Phase = ExportImportPhase.Idle;
            StatusMessage = "Záloha zrušena.";
        }
        catch (Exception ex)
        {
            Phase = ExportImportPhase.Failed;
            StatusMessage = $"❌ Záloha selhala: {ex.Message}";
            _log.Add($"[{DateTime.Now:HH:mm:ss}] CHYBA: {ex.Message}");
            ResultText = $"Záloha selhala: {ex.Message}";
            IsResultSuccess = false;
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            BackupLogText = string.Join("\n", _log);
        }
    }

    private async Task RestoreLocalBackupAsync()
    {
        if (Phase != ExportImportPhase.Idle || SelectedLocalBackup == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Potvrzení obnovy",
            $"Obnovit databázi ze zálohy?\n\n" +
            $"Soubor: {SelectedLocalBackup.FileName}\n" +
            $"Datum: {SelectedLocalBackup.CreatedAt:dd.MM.yyyy HH:mm}\n" +
            $"Velikost: {FormatBytes(SelectedLocalBackup.SizeBytes)}\n\n" +
            "⚠️ VAROVÁNÍ: Aktuální data budou přepsána!\n\n" +
            "Doporučujeme před obnovou vytvořit aktuální zálohu.\n\n" +
            "Pokračovat?");

        if (!confirmed) return;

        _operationCts = new CancellationTokenSource();
        Phase = ExportImportPhase.Importing;
        StatusMessage = "Obnovuji ze zálohy...";
        _log.Clear();
        _log.Add($"[{DateTime.Now:HH:mm:ss}] Obnovuji ze zálohy: {SelectedLocalBackup.FilePath}");

        try
        {
            await Task.Run(() => _backupService.RestoreBackupAsync(SelectedLocalBackup.FilePath), _operationCts.Token);

            _log.Add($"[{DateTime.Now:HH:mm:ss}] Obnova dokončena.");

            Phase = ExportImportPhase.Completed;
            StatusMessage = "✅ Obnova dokončena!";
            ResultText = "Data byla úspěšně obnovena.\n\nRestartujte aplikaci pro načtení obnovených dat.";
            IsResultSuccess = true;
        }
        catch (OperationCanceledException)
        {
            Phase = ExportImportPhase.Idle;
            StatusMessage = "Obnova zrušena.";
        }
        catch (Exception ex)
        {
            Phase = ExportImportPhase.Failed;
            StatusMessage = $"❌ Obnova selhala: {ex.Message}";
            _log.Add($"[{DateTime.Now:HH:mm:ss}] CHYBA: {ex.Message}");
            ResultText = $"Obnova selhala: {ex.Message}";
            IsResultSuccess = false;
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            BackupLogText = string.Join("\n", _log);
        }
    }

    private async Task RefreshLocalBackupsAsync()
    {
        try
        {
            IsLoadingBackups = true;
            var backups = await _backupService.GetAvailableBackupsAsync();
            AvailableLocalBackups.Clear();
            foreach (var b in backups)
                AvailableLocalBackups.Add(b);
        }
        catch (Exception ex)
        {
            _log.Add($"[CHYBA] Načtení seznamu záloh: {ex.Message}");
        }
        finally
        {
            IsLoadingBackups = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
