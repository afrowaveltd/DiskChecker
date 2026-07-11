using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IDialogService _dialogService;
        private readonly IBackupService _backupService;
        private readonly LocaleService _localizationService;
        
        private bool _isSaving;
        private string _statusMessage = string.Empty;
        private bool _isLoadingBackups;
        private ObservableCollection<IBackupService.BackupInfo> _availableBackups = new();
        private string _reportRecipientEmail = string.Empty;
        private string _databaseProvider = DatabaseProviderKind.Sqlite.ToString();
        private string _databaseConnectionString = DatabaseStorageSettings.Default.ConnectionString!;
        
        // Nastavení aplikace
        private bool _autoCheckForUpdates;
        private bool _runAtStartup;
        private bool _minimizeToTray;
        private int _autoSaveInterval;
        private string _defaultExportPath = string.Empty;
        private string _language = string.Empty;
        private bool _enableLogging;
        private string _logLevel = "Information";
        // Exposed SMART probe settings for UI
        private int _smartCacheTtlMinutes;
        private int _smartProbeTimeoutSeconds;
        private int _smartProbeParallelismValue;

        public SettingsViewModel(ISettingsService settingsService, IDialogService dialogService, IBackupService backupService, LocaleService localizationService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
            
            SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsSaving);
            ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync);
            BrowseExportPathCommand = new AsyncRelayCommand(BrowseExportPathAsync);
            CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => !IsSaving);
            RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => !IsSaving && SelectedBackup != null);
            RefreshBackupsCommand = new AsyncRelayCommand(RefreshBackupsAsync);
            SetLanguageCommand = new RelayCommand<string>(SetLanguage);
            TestDatabaseConnectionCommand = new AsyncRelayCommand(TestDatabaseConnectionAsync, () => !IsSaving);

            // Populate available languages
            foreach (var loc in _localizationService.GetAvailableLocales())
                AvailableLanguages.Add(loc);
            if (AvailableLanguages.Count == 0)
            {
                AvailableLanguages.Add("cs");
                AvailableLanguages.Add("en");
            }
            
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

        public int SmartCacheTtlMinutes
        {
            get => _smartCacheTtlMinutes;
            set => SetProperty(ref _smartCacheTtlMinutes, value);
        }

        public int SmartProbeTimeoutSeconds
        {
            get => _smartProbeTimeoutSeconds;
            set => SetProperty(ref _smartProbeTimeoutSeconds, value);
        }

        public int SmartProbeParallelism
        {
            get => _smartProbeParallelismValue;
            set => SetProperty(ref _smartProbeParallelismValue, value);
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
        public IRelayCommand<string> SetLanguageCommand { get; }
        public IAsyncRelayCommand TestDatabaseConnectionCommand { get; }

        public ObservableCollection<string> AvailableLanguages { get; } = new();
        public ObservableCollection<string> AvailableDatabaseProviders { get; } = new(Enum.GetNames<DatabaseProviderKind>());

        public string ReportRecipientEmail
        {
            get => _reportRecipientEmail;
            set => SetProperty(ref _reportRecipientEmail, value);
        }

        public string DatabaseProvider
        {
            get => _databaseProvider;
            set => SetProperty(ref _databaseProvider, value);
        }

        public string DatabaseConnectionString
        {
            get => _databaseConnectionString;
            set => SetProperty(ref _databaseConnectionString, value);
        }

        private async void LoadSettings()
        {
            try
            {
                StatusMessage = "Načítám nastavení...";
                
                // Načteme nastavení ze služby
                AutoCheckForUpdates = await _settingsService.GetAutoCheckForUpdatesAsync();
                RunAtStartup = await _settingsService.GetRunAtStartupAsync();
                MinimizeToTray = await _settingsService.GetMinimizeToTrayAsync();
                AutoSaveInterval = await _settingsService.GetAutoSaveIntervalAsync();
                DefaultExportPath = await _settingsService.GetDefaultExportPathAsync();
                Language = await _settingsService.GetLanguageAsync();
                EnableLogging = await _settingsService.GetEnableLoggingAsync();
                LogLevel = await _settingsService.GetLogLevelAsync();
                ReportRecipientEmail = await _settingsService.GetReportRecipientEmailAsync();
                var dbSettings = await _settingsService.GetDatabaseStorageSettingsAsync();
                DatabaseProvider = dbSettings.Provider.ToString();
                DatabaseConnectionString = dbSettings.ConnectionString ?? DatabaseStorageSettings.Default.ConnectionString!;
                // SMART probe persisted settings
                var ttl = await _settingsService.GetSmartCacheTtlMinutesAsync();
                var timeout = await _settingsService.GetSmartProbeTimeoutSecondsAsync();
                var parallel = await _settingsService.GetSmartProbeParallelismAsync();

                SmartCacheTtlMinutes = ttl;
                SmartProbeTimeoutSeconds = timeout;
                SmartProbeParallelism = parallel;
                
                StatusMessage = "Nastavení načteno";
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = $"Chyba při načítání nastavení: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.SettingsLoadFailed"), ex.Message));
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Ukládám nastavení...";
                
                // Uložíme nastavení do služby
                await _settingsService.SetAutoCheckForUpdatesAsync(AutoCheckForUpdates);
                await _settingsService.SetRunAtStartupAsync(RunAtStartup);
                await _settingsService.SetMinimizeToTrayAsync(MinimizeToTray);
                await _settingsService.SetAutoSaveIntervalAsync(AutoSaveInterval);
                await _settingsService.SetDefaultExportPathAsync(DefaultExportPath);
                await _settingsService.SetLanguageAsync(Language);
                await _settingsService.SetEnableLoggingAsync(EnableLogging);
                await _settingsService.SetLogLevelAsync(LogLevel);
                await _settingsService.SetReportRecipientEmailAsync(ReportRecipientEmail);
                var providerKind = Enum.TryParse<DatabaseProviderKind>(DatabaseProvider, ignoreCase: true, out var parsedProvider)
                    ? parsedProvider
                    : DatabaseProviderKind.Sqlite;
                await _settingsService.SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
                {
                    Provider = providerKind,
                    ConnectionString = DatabaseConnectionString
                });
                // Persist SMART probe settings
                await _settingsService.SetSmartCacheTtlMinutesAsyncPersistent(SmartCacheTtlMinutes);
                await _settingsService.SetSmartProbeTimeoutSecondsAsync(SmartProbeTimeoutSeconds);
                await _settingsService.SetSmartProbeParallelismAsync(SmartProbeParallelism);
                
                StatusMessage = "Nastavení úspěšně uloženo";
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = $"Chyba při ukládání nastavení: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.SettingsSaveFailed"), ex.Message));
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
                    L.Get("Common.Confirmation"), 
                    L.Get("Settings.ResetConfirmMessage"));
                
                if (confirmation)
                {
                    StatusMessage = "Resetuji nastavení...";
                    
                    // Resetujeme nastavení ve službě
                    await _settingsService.ResetToDefaultsAsync();
                    
                    // Znovu načteme nastavení
                    LoadSettings();
                    
                    StatusMessage = "Nastavení resetováno na výchozí hodnoty";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při resetování nastavení: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), $"Nepodařilo se resetovat nastavení: {ex.Message}");
            }
        }

        private async Task BrowseExportPathAsync()
        {
            try
            {
                StatusMessage = "Vyberte složku pro export...";
                // Zde bychom normálně otevřeli dialog pro výběr složky
                // Pro demo účely použijeme placeholder
                var selectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                DefaultExportPath = selectedPath;
                StatusMessage = "Složka pro export vybrána";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při výběru složky: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), $"Nepodařilo se vybrat složku: {ex.Message}");
            }
        }

        private async Task TestDatabaseConnectionAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Testuji připojení k databázi...";

                var providerKind = Enum.TryParse<DatabaseProviderKind>(DatabaseProvider, ignoreCase: true, out var parsedProvider)
                    ? parsedProvider
                    : DatabaseProviderKind.Sqlite;

                var optionsBuilder = new DbContextOptionsBuilder<DiskCheckerDbContext>();
                DatabaseProviderConfiguration.Configure(optionsBuilder, new DatabaseStorageSettings
                {
                    Provider = providerKind,
                    ConnectionString = DatabaseConnectionString
                });

                await using var context = new DiskCheckerDbContext(optionsBuilder.Options);
                var canConnect = await context.Database.CanConnectAsync();
                StatusMessage = canConnect
                    ? "Připojení k databázi je v pořádku. Změna backendu se plně projeví po restartu aplikace."
                    : "Připojení k databázi selhalo.";
            }
            catch(Exception ex)
            {
                StatusMessage = $"Test DB selhal: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Database"), StatusMessage);
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task CreateBackupAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Vytvářím zálohu...";
                
                var backupPath = await _backupService.CreateBackupAsync(string.Empty);
                
                StatusMessage = $"Záloha vytvořena: {Path.GetFileName(backupPath)}";
                await _dialogService.ShowMessageAsync(L.Get("Common.BackupCreated"), 
                    $"Záloha byla úspěšně vytvořena.\n\nUmístění: {backupPath}");
                
                await RefreshBackupsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při vytváření zálohy: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), $"Nepodařilo se vytvořit zálohu: {ex.Message}");
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
                    L.Get("Settings.RestoreFromBackup"),
                    string.Format(L.Get("Settings.RestoreConfirmMessage"), SelectedBackup.FileName, SelectedBackup.CreatedAt.ToString("dd.MM.yyyy HH:mm")));
                    $"Záloha: {SelectedBackup.FileName}\n" +
                    $"Datum: {SelectedBackup.CreatedAt:dd.MM.yyyy HH:mm}\n\n" +
                    $"VAROVÁNÍ: Aktuální data budou přepsána!");
                
                if (confirmation)
                {
                    IsSaving = true;
                    StatusMessage = "Obnovuji ze zálohy...";
                    
                    await _backupService.RestoreBackupAsync(SelectedBackup.FilePath);
                    
                    StatusMessage = "Data byla úspěšně obnovena";
                    await _dialogService.ShowMessageAsync(L.Get("Common.RestoreCompleted"), 
                        "Data byla úspěšně obnovena ze zálohy.\n\nRestartujte aplikaci pro načtení obnovených dat.");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při obnovování zálohy: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), $"Nepodařilo se obnovit zálohu: {ex.Message}");
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

        private void SetLanguage(string? locale)
        {
            if (string.IsNullOrWhiteSpace(locale)) return;
            _localizationService.SetLocale(locale);
            Language = locale;
            StatusMessage = $"Jazyk změněn na: {locale}";
        }
    }
}
