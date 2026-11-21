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
using System.Linq;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase, INavigationActivity
    {
        private readonly ISettingsService _settingsService;
        private readonly IEmailSettingsService _emailSettingsService;
        private readonly IEmailSender _emailSender;
        private string _smtpHost = string.Empty;
        private int _smtpPort = 587;
        private bool _smtpUseTls = true;
        private bool _smtpAllowInsecure;
        private string _smtpUserName = string.Empty;
        private string _smtpPassword = string.Empty;
        private bool _clearStoredSmtpPassword;
        private bool _hasStoredSmtpPassword;
        private string _smtpFromName = "DiskChecker";
        private string _smtpFromAddress = string.Empty;
        private readonly IDialogService _dialogService;
        private readonly IBackupService _backupService;
        private readonly LocaleService _localizationService;
        private readonly IDiskCacheService _diskCacheService;
        private readonly INavigationService _navigationService;
        private bool _emailSendOnlyForLongRunningTests;
        private bool _emailIncludeCertificateAttachment;
        private int _usbRecoveryRetryCount;
        private string _databaseSize = "N/A";
        private string _lastBackupText = "—";
        private bool _isEmailAttachmentAvailable;
        
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

        public SettingsViewModel(ISettingsService settingsService, IDialogService dialogService, IBackupService backupService, LocaleService localizationService, IDiskCacheService diskCacheService, INavigationService navigationService, IEmailSettingsService emailSettingsService, IEmailSender emailSender)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
            _diskCacheService = diskCacheService ?? throw new ArgumentNullException(nameof(diskCacheService));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _emailSettingsService = emailSettingsService ?? throw new ArgumentNullException(nameof(emailSettingsService));
            _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
            
            SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsSaving);
            ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync, () => !IsSaving);
            BrowseExportPathCommand = new AsyncRelayCommand(BrowseExportPathAsync);
            CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => !IsSaving && CanUseSqliteBackup);
            RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => !IsSaving && CanUseSqliteBackup && SelectedBackup != null);
            RefreshBackupsCommand = new AsyncRelayCommand(RefreshBackupsAsync);
            SetLanguageCommand = new RelayCommand<string>(SetLanguage);
            TestDatabaseConnectionCommand = new AsyncRelayCommand(TestDatabaseConnectionAsync, () => !IsSaving);
            TestSmtpCommand = new AsyncRelayCommand(TestSmtpAsync, () => !IsSaving && IsEmailAttachmentAvailable);
            GoBackCommand = new RelayCommand(() => _navigationService.NavigateTo<DiskSelectionViewModel>());
            OpenDataFolderCommand = new AsyncRelayCommand(OpenDataFolderAsync);
            ClearCacheCommand = new RelayCommand(ClearCache);
        NavigateToExportImportCommand = new RelayCommand(NavigateToExportImport);

            // Populate available languages
            foreach (var loc in _localizationService.GetAvailableLocales())
                AvailableLanguages.Add(loc);
            if (AvailableLanguages.Count == 0)
            {
                AvailableLanguages.Add("cs");
                AvailableLanguages.Add("en");
            }
            
            _ = LoadSettingsAsync();
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

        /// <summary>Prevents leaving during database backup/restore and settings mutations.</summary>
        public bool IsNavigationBlocked => IsSaving;

        public bool IsSaving
        {
            get => _isSaving;
            set
            {
                if (SetProperty(ref _isSaving, value))
                {
                    SaveSettingsCommand.NotifyCanExecuteChanged();
                    ResetSettingsCommand.NotifyCanExecuteChanged();
                    TestDatabaseConnectionCommand.NotifyCanExecuteChanged();
                    CreateBackupCommand.NotifyCanExecuteChanged();
                    RestoreBackupCommand.NotifyCanExecuteChanged();
                    TestSmtpCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(IsNavigationBlocked));
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
        /// <summary>The ZIP backup service supports only the active SQLite database.</summary>
        public bool CanUseSqliteBackup => (_settingsService as DiskChecker.Application.Services.SettingsService)?
            .StartupDatabaseStorageSettings.Provider == DatabaseProviderKind.Sqlite;
        public bool EmailSendOnlyForLongRunningTests
        {
            get => _emailSendOnlyForLongRunningTests;
            set => SetProperty(ref _emailSendOnlyForLongRunningTests, value);
        }
        public bool IsEmailAttachmentAvailable
        {
            get => _isEmailAttachmentAvailable;
            private set
            {
                if (SetProperty(ref _isEmailAttachmentAvailable, value))
                    TestSmtpCommand.NotifyCanExecuteChanged();
            }
        }

        // The password is never loaded into a UI-bound property.
        // Empty input preserves the existing secret unless ClearStoredSmtpPassword is checked.
        public string SmtpHost { get => _smtpHost; set => SetProperty(ref _smtpHost, value); }
        public int SmtpPort { get => _smtpPort; set => SetProperty(ref _smtpPort, value); }
        public bool SmtpUseTls { get => _smtpUseTls; set => SetProperty(ref _smtpUseTls, value); }
        public bool SmtpAllowInsecure { get => _smtpAllowInsecure; set => SetProperty(ref _smtpAllowInsecure, value); }
        public string SmtpUserName { get => _smtpUserName; set => SetProperty(ref _smtpUserName, value); }
        public string SmtpPassword { get => _smtpPassword; set => SetProperty(ref _smtpPassword, value); }
        public bool ClearStoredSmtpPassword { get => _clearStoredSmtpPassword; set => SetProperty(ref _clearStoredSmtpPassword, value); }
        public bool HasStoredSmtpPassword { get => _hasStoredSmtpPassword; private set => SetProperty(ref _hasStoredSmtpPassword, value); }
        public string SmtpFromName { get => _smtpFromName; set => SetProperty(ref _smtpFromName, value); }
        public string SmtpFromAddress { get => _smtpFromAddress; set => SetProperty(ref _smtpFromAddress, value); }

        public bool EmailIncludeCertificateAttachment
        {
            get => _emailIncludeCertificateAttachment;
            set => SetProperty(ref _emailIncludeCertificateAttachment, value);
        }
        public int UsbRecoveryRetryCount
        {
            get => _usbRecoveryRetryCount;
            set => SetProperty(ref _usbRecoveryRetryCount, value);
        }
        public string DatabaseSize
        {
            get => _databaseSize;
            private set => SetProperty(ref _databaseSize, value);
        }
        public string LastBackupText
        {
            get => _lastBackupText;
            private set => SetProperty(ref _lastBackupText, value);
        }

        public IAsyncRelayCommand SaveSettingsCommand { get; }
        public IAsyncRelayCommand ResetSettingsCommand { get; }
        public IAsyncRelayCommand BrowseExportPathCommand { get; }
        public IAsyncRelayCommand CreateBackupCommand { get; }
        public IAsyncRelayCommand RestoreBackupCommand { get; }
        public IAsyncRelayCommand RefreshBackupsCommand { get; }
        public IRelayCommand<string> SetLanguageCommand { get; }
        public IRelayCommand GoBackCommand { get; }
        public IAsyncRelayCommand OpenDataFolderCommand { get; }
        public IAsyncRelayCommand TestSmtpCommand { get; }
        public IRelayCommand ClearCacheCommand { get; }
        public IAsyncRelayCommand TestDatabaseConnectionCommand { get; }
    public IRelayCommand NavigateToExportImportCommand { get; }

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

        private async Task LoadSettingsAsync()
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
                EmailSendOnlyForLongRunningTests = await _settingsService.GetEmailSendOnlyForLongRunningTestsAsync();
                EmailIncludeCertificateAttachment = await _settingsService.GetEmailIncludeCertificateAttachmentAsync();
                // This is the actual source consumed by TestCompletionNotificationService.
                // The legacy general settings flag alone did not affect attachments.
                try
                {
                    var smtp = await _emailSettingsService.GetAsync();
                    EmailIncludeCertificateAttachment = smtp.IncludeCertificateAttachment;
                    SmtpHost = smtp.Host;
                    SmtpPort = smtp.Port;
                    SmtpUseTls = smtp.UseSsl;
                    SmtpAllowInsecure = smtp.AllowInsecureSmtp;
                    SmtpUserName = smtp.UserName;
                    SmtpPassword = string.Empty;
                    ClearStoredSmtpPassword = false;
                    HasStoredSmtpPassword = !string.IsNullOrEmpty(smtp.Password);
                    SmtpFromName = smtp.FromName;
                    SmtpFromAddress = smtp.FromAddress;
                    IsEmailAttachmentAvailable = true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException or System.Data.Common.DbException or IOException)
                {
                    // The main Settings panel must remain editable if SMTP
                    // storage is unavailable. Disable its unsupported toggle.
                    IsEmailAttachmentAvailable = false;
                }
                UsbRecoveryRetryCount = await _settingsService.GetUsbRecoveryRetryCountAsync();
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
                if (!string.IsNullOrWhiteSpace(Language) &&
                    !string.Equals(_localizationService.CurrentLocale, Language, StringComparison.OrdinalIgnoreCase))
                    _localizationService.SetLocale(Language);

                var concreteSettings = _settingsService as DiskChecker.Application.Services.SettingsService;
                var dbLocationWarning = concreteSettings?.OtherDatabaseCandidates;
                var active = concreteSettings?.StartupDatabaseStorageSettings ?? dbSettings;
                // Display only the currently active local SQLite file, not a pending backend.
                DatabaseSize = GetActiveDatabaseSize(active);
                // Never display non-SQLite connection strings here: they may include credentials.
                var activeDescription = active.Provider == DatabaseProviderKind.Sqlite
                    ? active.ConnectionString ?? "(bez cesty)"
                    : active.Provider.ToString();
                var pendingChanges = active.Provider != dbSettings.Provider ||
                    !string.Equals(active.ConnectionString, dbSettings.ConnectionString, StringComparison.Ordinal);
                var databaseMessage = $"Aktivní DB: {activeDescription}" +
                    (pendingChanges ? ". Nastavení nové databáze se použije až po restartu." : "");
                StatusMessage = (dbLocationWarning is { Count: > 0 }
                    ? $"{databaseMessage}. Pozor: nalezeny i další databáze: {string.Join("; ", dbLocationWarning)}. Nebyly změněny."
                    : databaseMessage) +
                    (IsEmailAttachmentAvailable ? "" : " " + L.Get("Settings.EmailSettingsUnavailable"));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při načítání nastavení: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.SettingsLoadFailed"), ex.Message));
            }
        }

        private async Task SaveSettingsAsync() => await SaveSettingsCoreAsync();

        private async Task<bool> SaveSettingsCoreAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Ukládám nastavení...";
                
                if (AutoSaveInterval is < 0 or > 1440 ||
                    SmartCacheTtlMinutes is < 1 or > 1440 ||
                    SmartProbeTimeoutSeconds is < 1 or > 300 ||
                    UsbRecoveryRetryCount is < 0 or > 10)
                    throw new InvalidOperationException(L.Get("Settings.InvalidNumberRange"));
                if (IsEmailAttachmentAvailable)
                {
                    if (SmtpPort is < 1 or > 65535)
                        throw new InvalidOperationException(L.Get("Settings.SmtpInvalidPort"));
                    if (!SmtpUseTls && !SmtpAllowInsecure)
                        throw new InvalidOperationException(L.Get("Settings.SmtpInsecurePermissionRequired"));
                }

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
                await _settingsService.SetEmailSendOnlyForLongRunningTestsAsync(EmailSendOnlyForLongRunningTests);
                if (IsEmailAttachmentAvailable)
                {
                    var smtp = await _emailSettingsService.GetAsync();
                    smtp.Host = SmtpHost.Trim();
                    smtp.Port = SmtpPort;
                    smtp.UseSsl = SmtpUseTls;
                    smtp.AllowInsecureSmtp = SmtpAllowInsecure;
                    smtp.UserName = SmtpUserName.Trim();
                    smtp.FromName = SmtpFromName.Trim();
                    smtp.FromAddress = SmtpFromAddress.Trim();
                    smtp.IncludeCertificateAttachment = EmailIncludeCertificateAttachment;
                    if (ClearStoredSmtpPassword) smtp.Password = string.Empty;
                    else if (!string.IsNullOrEmpty(SmtpPassword)) smtp.Password = SmtpPassword;
                    await _emailSettingsService.SaveAsync(smtp);
                    HasStoredSmtpPassword = !string.IsNullOrEmpty(smtp.Password);
                    SmtpPassword = string.Empty;
                    ClearStoredSmtpPassword = false;
                    await _settingsService.SetEmailIncludeCertificateAttachmentAsync(EmailIncludeCertificateAttachment);
                }
                await _settingsService.SetUsbRecoveryRetryCountAsync(UsbRecoveryRetryCount);
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
                if (!string.IsNullOrWhiteSpace(Language) &&
                    !string.Equals(_localizationService.CurrentLocale, Language, StringComparison.OrdinalIgnoreCase))
                    _localizationService.SetLocale(Language);

                StatusMessage = L.Get("Settings.Saved");
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při ukládání nastavení: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.SettingsSaveFailed"), ex.Message));
                return false;
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task TestSmtpAsync()
        {
            if (!IsEmailAttachmentAvailable) return;
            // The test uses the same persisted server settings and the same
            // sender as automatic completion emails; no simulated success.
            if (!await SaveSettingsCoreAsync()) return;
            IsSaving = true;
            try
            {
                if (string.IsNullOrWhiteSpace(ReportRecipientEmail))
                    throw new InvalidOperationException(L.Get("Settings.SmtpRecipientRequired"));
                StatusMessage = L.Get("Settings.SmtpSendingTest");
                await _emailSender.SendAsync(new EmailMessage
                {
                    ToAddress = ReportRecipientEmail.Trim(),
                    Subject = L.Get("Settings.SmtpTestSubject"),
                    TextBody = L.Get("Settings.SmtpTestBody")
                });
                StatusMessage = L.Get("Settings.SmtpTestAccepted");
                await _dialogService.ShowSuccessAsync(L.Get("Settings.SmtpTestTitle"), StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = L.Get("Settings.SmtpTestFailed") + ": " + ex.Message;
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), StatusMessage);
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
                    IsSaving = true;
                    StatusMessage = L.Get("Settings.Resetting");

                    // General settings and notification recipient are reset.
                    // SMTP server credentials reside in a separate DB-backed
                    // service and are intentionally preserved.
                    await _settingsService.ResetToDefaultsAsync();
                    await LoadSettingsAsync();

                    StatusMessage = L.Get("Settings.ResetDone");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba při resetování nastavení: {ex.Message}";
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), $"Nepodařilo se resetovat nastavení: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task BrowseExportPathAsync()
        {
            try
            {
                StatusMessage = "Vyberte složku pro export...";
                var folders = await _dialogService.PickFoldersAsync(L.Get("Settings.SelectExportFolder"), false);
                if (folders.Count == 0)
                {
                    StatusMessage = L.Get("Settings.ExportSelectionCancelled");
                    return;
                }
                DefaultExportPath = folders[0];
                StatusMessage = L.Get("Settings.ExportFolderSelected");
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
                var ordered = backups.OrderByDescending(b => b.CreatedAt).ToList();
                var oldSelection = SelectedBackup?.FilePath;
                AvailableBackups = new ObservableCollection<IBackupService.BackupInfo>(ordered);
                // Explicit user choice is mandatory; never auto-restore the newest backup.
                SelectedBackup = ordered.FirstOrDefault(b => b.FilePath == oldSelection);
                LastBackupText = ordered.Count == 0 ? "—" :
                    ordered[0].CreatedAt.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
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

        private static string GetActiveDatabaseSize(DatabaseStorageSettings settings)
        {
            if (settings.Provider != DatabaseProviderKind.Sqlite ||
                string.IsNullOrWhiteSpace(settings.ConnectionString))
                return "N/A";
            try
            {
                var path = new SqliteConnectionStringBuilder(settings.ConnectionString).DataSource;
                if (!File.Exists(path)) return "N/A";
                // SQLite may keep a substantial amount of data in its live
                // write-ahead log. Showing only DiskChecker.db underreports
                // actual local disk use.
                long total = new FileInfo(path).Length;
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    if (File.Exists(path + suffix))
                        total += new FileInfo(path + suffix).Length;
                }
                return $"{total / 1048576d:F1} MiB";
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return "N/A";
            }
        }

        private void ClearCache()
        {
            // Only invalidate the 30-second in-memory disk discovery cache.
            // Never delete history, telemetry or any persisted database files.
            _diskCacheService.ClearCache();
            StatusMessage = L.Get("Settings.CacheCleared");
        }

        private async Task OpenDataFolderAsync()
        {
            try
            {
                var path = (_settingsService as DiskChecker.Application.Services.SettingsService)?.SettingsFilePath;
                var folder = !string.IsNullOrWhiteSpace(path)
                    ? Path.GetDirectoryName(path)
                    : _backupService.GetDefaultBackupDirectory();
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    throw new DirectoryNotFoundException(folder);
                await Task.Run(() => DocumentLauncher.OpenFolder(folder));
                StatusMessage = L.Get("Settings.DataFolderOpened");
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), ex.Message);
            }
        }

        private void NavigateToExportImport()
        {
            var navService = App.GetService<INavigationService>();
            navService?.NavigateTo<ExportImportViewModel>();
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
