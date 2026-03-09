using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.Application.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly HistoryService _historyService;
        private readonly IDialogService _dialogService;
        private readonly IBackupService _backupService;
        
        private bool _isSaving;
        private string _statusMessage = string.Empty;
        private bool _isLoadingBackups;
        private ObservableCollection<IBackupService.BackupInfo> _availableBackups = new();
        
        // Nastavení aplikace
        private bool _autoCheckForUpdates;
        private bool _runAtStartup;
        private bool _minimizeToTray;
        private int _autoSaveInterval;
        private string _defaultExportPath = string.Empty;
        private string _language = string.Empty;
        private bool _enableLogging;
        private string _logLevel = "Information";

        public SettingsViewModel(HistoryService historyService, IDialogService dialogService, IBackupService backupService)
        {
            _historyService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            
            SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsSaving);
            ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync);
            BrowseExportPathCommand = new AsyncRelayCommand(BrowseExportPathAsync);
            CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => !IsSaving);
            RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => !IsSaving && SelectedBackup != null);
            RefreshBackupsCommand = new AsyncRelayCommand(RefreshBackupsAsync);
            
            LoadSettings();
            _ = RefreshBackupsAsync();
        }

        public bool AutoCheckForUpdates
        {
            get => _autoCheckForUpdates;
            set => SetProperty(ref _autoCheckForUpdates, value);
        }

        public bool RunAtStartup
        {
            get => _runAtStartup;
            set => SetProperty(ref _runAtStartup, value);
        }

        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set => SetProperty(ref _minimizeToTray, value);
        }

        public int AutoSaveInterval
        {
            get => _autoSaveInterval;
            set => SetProperty(ref _autoSaveInterval, value);
        }

        public string DefaultExportPath
        {
            get => _defaultExportPath;
            set => SetProperty(ref _defaultExportPath, value);
        }

        public string Language
        {
            get => _language;
            set => SetProperty(ref _language, value);
        }

        public bool EnableLogging
        {
            get => _enableLogging;
            set => SetProperty(ref _enableLogging, value);
        }

        public string LogLevel
        {
            get => _logLevel;
            set => SetProperty(ref _logLevel, value);
        }

        public bool IsSaving
        {
            get => _isSaving;
            set
            {
                if (SetProperty(ref _isSaving, value))
                {
                    SaveSettingsCommand.NotifyCanExecuteChanged();
                    CreateBackupCommand.NotifyCanExecuteChanged();
                    RestoreBackupCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsLoadingBackups
        {
            get => _isLoadingBackups;
            set => SetProperty(ref _isLoadingBackups, value);
        }

        public ObservableCollection<IBackupService.BackupInfo> AvailableBackups
        {
            get => _availableBackups;
            set => SetProperty(ref _availableBackups, value);
        }

        private IBackupService.BackupInfo? _selectedBackup;
        public IBackupService.BackupInfo? SelectedBackup
        {
            get => _selectedBackup;
            set
            {
                if (SetProperty(ref _selectedBackup, value))
                {
                    RestoreBackupCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string DefaultBackupDirectory => _backupService.GetDefaultBackupDirectory();

        public IAsyncRelayCommand SaveSettingsCommand { get; }
        public IAsyncRelayCommand ResetSettingsCommand { get; }
        public IAsyncRelayCommand BrowseExportPathCommand { get; }
        public IAsyncRelayCommand CreateBackupCommand { get; }
        public IAsyncRelayCommand RestoreBackupCommand { get; }
        public IAsyncRelayCommand RefreshBackupsCommand { get; }

        private async void LoadSettings()
        {
            try
            {
                StatusMessage = "Načítám nastavení...";
                
                // Načteme nastavení ze služby
                AutoCheckForUpdates = await _historyService.GetAutoCheckForUpdatesAsync();
                RunAtStartup = await _historyService.GetRunAtStartupAsync();
                MinimizeToTray = await _historyService.GetMinimizeToTrayAsync();
                AutoSaveInterval = await _historyService.GetAutoSaveIntervalAsync();
                DefaultExportPath = await _historyService.GetDefaultExportPathAsync();
                Language = await _historyService.GetLanguageAsync();
                EnableLogging = await _historyService.GetEnableLoggingAsync();
                LogLevel = await _historyService.GetLogLevelAsync();
                
                StatusMessage = "Nastavení načteno";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při načítání nastavení: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst nastavení: {ex.Message}");
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Ukládám nastavení...";
                
                // Uložíme nastavení do služby
                await _historyService.SetAutoCheckForUpdatesAsync(AutoCheckForUpdates);
                await _historyService.SetRunAtStartupAsync(RunAtStartup);
                await _historyService.SetMinimizeToTrayAsync(MinimizeToTray);
                await _historyService.SetAutoSaveIntervalAsync(AutoSaveInterval);
                await _historyService.SetDefaultExportPathAsync(DefaultExportPath);
                await _historyService.SetLanguageAsync(Language);
                await _historyService.SetEnableLoggingAsync(EnableLogging);
                await _historyService.SetLogLevelAsync(LogLevel);
                
                StatusMessage = "Nastavení úspěšně uloženo";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při ukládání nastavení: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se uložit nastavení: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task ResetSettingsAsync()
        {
            try
            {
                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "Potvrzení", 
                    "Opravdu chcete resetovat všechna nastavení na výchozí hodnoty?");
                
                if (confirmation)
                {
                    StatusMessage = "Resetuji nastavení...";
                    
                    // Resetujeme nastavení ve službě
                    await _historyService.ResetToDefaultsAsync();
                    
                    // Znovu načteme nastavení
                    LoadSettings();
                    
                    StatusMessage = "Nastavení resetováno na výchozí hodnoty";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při resetování nastavení: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se resetovat nastavení: {ex.Message}");
            }
        }

        private async Task BrowseExportPathAsync()
        {
            try
            {
                StatusMessage = "Vyberte složku pro export...";
                // Zde bychom normálně otevřeli dialog pro výběr složky
                // Pro demo účely použijeme placeholder
                var selectedPath = "/home/user/Documents/DiskCheckerExports"; // Placeholder hodnota
                DefaultExportPath = selectedPath;
                StatusMessage = "Složka pro export vybrána";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při výběru složky: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vybrat složku: {ex.Message}");
            }
        }

        private async Task CreateBackupAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Vytvářím zálohu...";
                
                // Show save file dialog - in Avalonia this would use a file dialog
                // For now, we'll save to the default backup directory
                var backupPath = await _backupService.CreateBackupAsync(string.Empty);
                
                StatusMessage = $"Záloha vytvořena: {Path.GetFileName(backupPath)}";
                await _dialogService.ShowMessageAsync("Záloha vytvořena", 
                    $"Záloha byla úspěšně vytvořena.\n\nUmístění: {backupPath}");
                
                // Refresh the backup list
                await RefreshBackupsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při vytváření zálohy: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vytvořit zálohu: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task RestoreBackupAsync()
        {
            if (SelectedBackup == null) return;

            try
            {
                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "Obnovení ze zálohy",
                    $"Opravdu chcete obnovit data ze zálohy?\n\n" +
                    $"Záloha: {SelectedBackup.FileName}\n" +
                    $"Datum: {SelectedBackup.CreatedAt:dd.MM.yyyy HH:mm}\n\n" +
                    $"VAROVÁNÍ: Aktuální data budou přepsána!");
                
                if (confirmation)
                {
                    IsSaving = true;
                    StatusMessage = "Obnovuji ze zálohy...";
                    
                    await _backupService.RestoreBackupAsync(SelectedBackup.FilePath);
                    
                    StatusMessage = "Data byla úspěšně obnovena";
                    await _dialogService.ShowMessageAsync("Obnovení dokončeno", 
                        "Data byla úspěšně obnovena ze zálohy.\n\n restartujte aplikaci pro načtení obnovených dat.");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při obnovování zálohy: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se obnovit zálohu: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task RefreshBackupsAsync()
        {
            try
            {
                IsLoadingBackups = true;
                var backups = await _backupService.GetAvailableBackupsAsync();
                AvailableBackups = new ObservableCollection<IBackupService.BackupInfo>(backups);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při načítání seznamu záloh: {ex.Message}";
            }
            finally
            {
                IsLoadingBackups = false;
            }
        }
    }
}
