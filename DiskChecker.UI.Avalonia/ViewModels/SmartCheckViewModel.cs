using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Full-featured SMART data viewer and disk health monitor.
/// Supports ATA/SATA, NVMe, and SCSI/SAS drives.
/// </summary>
public partial class SmartCheckViewModel : ViewModelBase, INavigableViewModel, IDisposable
{
    private readonly ISmartaProvider _smartaProvider;
    private readonly IDiskDetectionService _diskDetectionService;
    private readonly IQualityCalculator _qualityCalculator;
    private readonly IDialogService _dialogService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly ISettingsService _settingsService;
    private readonly DiskCardTestService _cardTestService;
    private readonly INavigationService _navigationService;

    private DateTime _lastSmartPersistUtc;
    private string? _lastSmartPersistDiskPath;

    private ObservableCollection<DiskStatusCardItem> _disks = new();
    private DiskStatusCardItem? _selectedDisk;
    private bool _isChecking;
    private bool _isLoading;
    private string _statusMessage = "Načítám disky...";
    private SmartaData? _currentSmartData;
    private QualityRating? _currentQuality;
    private ObservableCollection<SmartaAttributeItem> _smartAttributes = new();
    private ObservableCollection<SmartaSelfTestEntry> _selfTestLog = new();
    private ObservableCollection<SmartaAttributeItem> _criticalAttributes = new();
    private string _selectedTestType = "Short";
    private string _smartCacheInfo = string.Empty;
    private string _smartCacheStats = string.Empty;
    private CancellationTokenSource? _loadSmartDataCts;

    public SmartCheckViewModel(
        ISmartaProvider smartaProvider, 
        IDiskDetectionService diskDetectionService,
        IQualityCalculator qualityCalculator, 
        IDialogService dialogService,
        ISelectedDiskService selectedDiskService,
        ISettingsService settingsService,
        DiskCardTestService cardTestService,
        INavigationService navigationService)
    {
        _smartaProvider = smartaProvider ?? throw new ArgumentNullException(nameof(smartaProvider));
        _diskDetectionService = diskDetectionService ?? throw new ArgumentNullException(nameof(diskDetectionService));
        _qualityCalculator = qualityCalculator ?? throw new ArgumentNullException(nameof(qualityCalculator));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _selectedDiskService = selectedDiskService ?? throw new ArgumentNullException(nameof(selectedDiskService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _cardTestService = cardTestService ?? throw new ArgumentNullException(nameof(cardTestService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        LoadDisksCommand = new AsyncRelayCommand(LoadDisksAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => CanRunSelfTest);
        RunShortTestCommand = new AsyncRelayCommand(RunShortTestAsync, () => CanRunSelfTest);
        RunLongTestCommand = new AsyncRelayCommand(RunLongTestAsync, () => CanRunSelfTest);
        RunSmartFeatureCommand = new AsyncRelayCommand(RunSmartFeatureAsync, () => CanRunSelfTest);
        ClearSelfTestResultCommand = new AsyncRelayCommand(ClearSelfTestResultAsync);
        AbortTestCommand = new AsyncRelayCommand(AbortTestAsync, () => SelectedDisk != null && (IsSelfTestRunning || !IsChecking));
        ClearCacheForSelectedCommand = new AsyncRelayCommand(ClearCacheForSelectedAsync, () => SelectedDisk != null);
        ClearAllSmartCacheCommand = new AsyncRelayCommand(ClearAllSmartCacheAsync);
        // Provide runtime TTL setter command (string-based for UI binding)
        SetCacheTtlCommand = new AsyncRelayCommand<string>(async s => await SetCacheTtlFromStringAsync(s ?? string.Empty));
        SetProbeTimeoutCommand = new AsyncRelayCommand<string>(async s => await SetProbeTimeoutFromStringAsync(s ?? string.Empty));
        SetProbeParallelismCommand = new AsyncRelayCommand<string>(async s => await SetProbeParallelismFromStringAsync(s ?? string.Empty));
        SelectVolumeCommand = new RelayCommand<CoreDriveInfo?>(SelectVolume);
        NavigateToBackupCommand = new AsyncRelayCommand(NavigateToBackupAsync, () => IsFailing);
        ForceTestAnywayCommand = new RelayCommand(ForceTestAnyway);

        // Initialize localized SMART features
        SmartFeatures = new[] {
            L.Get("SmartCheck.Action.ShortTest"),
            L.Get("SmartCheck.Action.ExtendedTest"),
            L.Get("SmartCheck.Action.ReadSmart"),
            L.Get("SmartCheck.Action.ReadSelfTestLog")
        };
        SmartFeatureDescription = L.Get("SmartCheck.Desc.ShortTest");
    }

    #region Properties

    public ObservableCollection<DiskStatusCardItem> Disks
    {
        get => _disks;
        set => SetProperty(ref _disks, value);
    }

    public DiskStatusCardItem? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
            {
                OnPropertyChanged(nameof(CanRunSelfTest));
                OnPropertyChanged(nameof(HasSelectedDisk));
                OnPropertyChanged(nameof(DiskName));
                OnPropertyChanged(nameof(DiskPath));
                RefreshCommand.NotifyCanExecuteChanged();
                RunShortTestCommand.NotifyCanExecuteChanged();
                RunLongTestCommand.NotifyCanExecuteChanged();
                AbortTestCommand.NotifyCanExecuteChanged();
                ClearCacheForSelectedCommand.NotifyCanExecuteChanged();

                if (value != null)
                {
                    _ = LoadSmartDataAsync();
                }
            }
        }
    }

    public bool HasSelectedDisk => SelectedDisk != null;

    public bool IsChecking
    {
        get => _isChecking;
        set
        {
            if (SetProperty(ref _isChecking, value))
            {
                OnPropertyChanged(nameof(CanRunSelfTest));
                RefreshCommand.NotifyCanExecuteChanged();
                RunShortTestCommand.NotifyCanExecuteChanged();
                RunLongTestCommand.NotifyCanExecuteChanged();
                AbortTestCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public SmartaData? CurrentSmartData
    {
        get => _currentSmartData;
        set
        {
            if (SetProperty(ref _currentSmartData, value))
            {
                OnPropertyChanged(nameof(HasData));
                OnPropertyChanged(nameof(HasHealthData));
                UpdateComputedProperties();
            }
        }
    }

    public string SmartCacheInfo
    {
        get => _smartCacheInfo;
        set => SetProperty(ref _smartCacheInfo, value);
    }

    public string SmartCacheStats
    {
        get => _smartCacheStats;
        set => SetProperty(ref _smartCacheStats, value);
    }

    public QualityRating? CurrentQuality
    {
        get => _currentQuality;
        set
        {
            if (SetProperty(ref _currentQuality, value))
            {
                UpdateComputedProperties();
            }
        }
    }

    public ObservableCollection<SmartaAttributeItem> SmartAttributes
    {
        get => _smartAttributes;
        set => SetProperty(ref _smartAttributes, value);
    }

    public ObservableCollection<SmartaAttributeItem> CriticalAttributes
    {
        get => _criticalAttributes;
        set => SetProperty(ref _criticalAttributes, value);
    }

    public ObservableCollection<SmartaSelfTestEntry> SelfTestLog
    {
        get => _selfTestLog;
        set => SetProperty(ref _selfTestLog, value);
    }

    // Raw smartctl output for display
    private string _rawSmartOutput = string.Empty;
    public string RawSmartOutput
    {
        get => _rawSmartOutput;
        set => SetProperty(ref _rawSmartOutput, value);
    }

    
    
    
public string SelectedTestType
    {
        get => _selectedTestType;
        set => SetProperty(ref _selectedTestType, value);
    }

    // Computed properties for display
    public bool HasData => CurrentSmartData != null;
    public bool HasHealthData => CurrentSmartData?.IsHealthy == true;
    public bool IsFailing => CurrentSmartData?.IsFailing == true;
    public string FailurePrediction => CurrentSmartData?.FailurePrediction ?? string.Empty;
    public bool HasFailingAttributes => CurrentSmartData?.FailingAttributes is { Count: > 0 };
    public string FailingAttributesSummary => CurrentSmartData?.FailingAttributes != null
        ? string.Join("\n", CurrentSmartData.FailingAttributes)
        : string.Empty;
    
    // Connection speed info
    private string _connectionSpeedText = string.Empty;
    public string ConnectionSpeedText
    {
        get => _connectionSpeedText;
        set => SetProperty(ref _connectionSpeedText, value);
    }

    private string _connectionSpeedDescription = string.Empty;
    public string ConnectionSpeedDescription
    {
        get => _connectionSpeedDescription;
        set => SetProperty(ref _connectionSpeedDescription, value);
    }

    private bool _hasConnectionSpeedInfo;
    public bool HasConnectionSpeedInfo
    {
        get => _hasConnectionSpeedInfo;
        set => SetProperty(ref _hasConnectionSpeedInfo, value);
    }

    // Self-test progress tracking
    private int _selfTestProgress;
    private string _selfTestProgressText = "";
    private string _selfTestTypeText = "";
    private bool _isSelfTestRunning;
    private CancellationTokenSource? _selfTestPollingCts;
    private bool _wasTestInProgress;
    private string _currentTestType = "";
    private TestType _currentSmartTestType = TestType.SmartShort;
    private bool _disposed;

    public int SelfTestProgress
    {
        get => _selfTestProgress;
        set => SetProperty(ref _selfTestProgress, value);
    }
    
    public string SelfTestProgressText
    {
        get => _selfTestProgressText;
        set => SetProperty(ref _selfTestProgressText, value);
    }
    
    public string SelfTestTypeText
    {
        get => _selfTestTypeText;
        set => SetProperty(ref _selfTestTypeText, value);
    }
    
    public bool IsSelfTestRunning
    {
        get => _isSelfTestRunning;
        set
        {
            if (SetProperty(ref _isSelfTestRunning, value))
            {
                OnPropertyChanged(nameof(CanRunSelfTest));
                RunShortTestCommand.NotifyCanExecuteChanged();
                RunLongTestCommand.NotifyCanExecuteChanged();
                AbortTestCommand.NotifyCanExecuteChanged();
            }
        }
    }
    
    // Self-test result display
    private string _selfTestResult = "";
    public string SelfTestResult
    {
        get => _selfTestResult;
        set => SetProperty(ref _selfTestResult, value);
    }
    
    private string _selfTestResultColor = "#666666";
    public string SelfTestResultColor
    {
        get => _selfTestResultColor;
        set => SetProperty(ref _selfTestResultColor, value);
    }
    
    private bool _hasSelfTestResult;
    public bool HasSelfTestResult
    {
        get => _hasSelfTestResult;
        set => SetProperty(ref _hasSelfTestResult, value);
    }
    
    public bool CanRunSelfTest => !IsChecking && !IsSelfTestRunning && SelectedDisk != null;
    
    // Safe property accessors with fallbacks
    public string DiskName => SelectedDisk?.DisplayName ?? "Nevybrán žádný disk";
    public string DiskPath => SelectedDisk?.DisplayPath ?? "";
    
    public string DeviceModel => CurrentSmartData?.DeviceModel ?? "Načítám...";
    public string SerialNumber => CurrentSmartData?.SerialNumber ?? "-";
    public string FirmwareVersion => CurrentSmartData?.FirmwareVersion ?? "-";
    public string DeviceType => CurrentSmartData?.DeviceType ?? "Neznámý";
    
    public string Temperature => CurrentSmartData?.Temperature > 0 
        ? $"{CurrentSmartData.Temperature}°C" : "-";
    public string TemperatureStatus => CurrentSmartData?.TemperatureStatus ?? "-";
    public string PowerOnHours => CurrentSmartData?.PowerOnHours > 0 
        ? $"{CurrentSmartData.PowerOnHours:N0} h" : "-";
    public string LifetimeStatus => CurrentSmartData?.LifetimeStatus ?? "-";
    
    public string PowerCycles => CurrentSmartData?.PowerCycleCount > 0 
        ? $"{CurrentSmartData.PowerCycleCount:N0}" : "-";
    
    public string ReallocatedSectors => CurrentSmartData?.ReallocatedSectorCount > 0 
        ? $"{CurrentSmartData.ReallocatedSectorCount:N0}" : "0 ✓";
    public string PendingSectors => CurrentSmartData?.PendingSectorCount > 0 
        ? $"{CurrentSmartData.PendingSectorCount:N0}" : "0 ✓";
    public string UncorrectableErrors => CurrentSmartData?.UncorrectableErrorCount > 0 
        ? $"{CurrentSmartData.UncorrectableErrorCount:N0}" : "0 ✓";
    
    // NVMe specific
    public bool IsNvMe => CurrentSmartData?.DeviceType?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) == true;
    public string NvMePercentageUsed => CurrentSmartData?.PercentageUsed != null 
        ? $"{CurrentSmartData.PercentageUsed}%" : "-";
    public string NvMeAvailableSpare => CurrentSmartData?.AvailableSpare != null
        ? $"{CurrentSmartData.AvailableSpare}%" : "-";
    public string SsdWearIndicator => CurrentSmartData?.WearLevelingCount != null
        ? $"{CurrentSmartData.WearLevelingCount}%" : "-";
    public string NvMeMediaErrors => CurrentSmartData?.MediaErrors > 0 
        ? $"{CurrentSmartData.MediaErrors:N0}" : "0";
    public string NvMeUnsafeShutdowns => CurrentSmartData?.UnsafeShutdowns > 0 
        ? $"{CurrentSmartData.UnsafeShutdowns:N0}" : "0";
    
    // Quality display
    public string Grade => CurrentQuality?.Grade.ToString() ?? "-";
    public string Score => CurrentQuality?.Score > 0 
        ? $"{CurrentQuality.Score:F0}" : "-";
    public string HealthStatus => CurrentSmartData?.HealthStatus ?? "-";
    
    // Color helpers
    public string GradeColor => GetGradeColor(CurrentQuality?.Grade);
    public string TempColor => GetTemperatureColor(CurrentSmartData?.Temperature);
    public string HealthColor => GetHealthColor(CurrentQuality?.Score);

    // Test type options for dropdown
    public string[] TestTypes { get; } = new[] { "Short", "Extended", "Conveyance" };
    
    // SMART Features for dropdown
    public string[] SmartFeatures { get; private set; } = Array.Empty<string>();
    
    private int _selectedSmartFeatureIndex;
    public int SelectedSmartFeatureIndex
    {
        get => _selectedSmartFeatureIndex;
        set
        {
            if (SetProperty(ref _selectedSmartFeatureIndex, value))
            {
                SmartFeatureDescription = value switch
                {
                    0 => L.Get("SmartCheck.Desc.ShortTest"),
                    1 => L.Get("SmartCheck.Desc.ExtendedTest"),
                    2 => L.Get("SmartCheck.Desc.ReadSmart"),
                    _ => L.Get("SmartCheck.Desc.ReadSelfTestLog")
                };
            }
        }
    }
    
    private string _smartFeatureDescription = string.Empty;
    public string SmartFeatureDescription
    {
        get => _smartFeatureDescription;
        set => SetProperty(ref _smartFeatureDescription, value);
    }

    #endregion

    #region Commands

    public IAsyncRelayCommand LoadDisksCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RunShortTestCommand { get; }
    public IAsyncRelayCommand RunLongTestCommand { get; }
    public IAsyncRelayCommand AbortTestCommand { get; }
    public IAsyncRelayCommand ClearCacheForSelectedCommand { get; }
    public IAsyncRelayCommand ClearAllSmartCacheCommand { get; }
    public IAsyncRelayCommand<string> SetCacheTtlCommand { get; }
    public IAsyncRelayCommand<string> SetProbeTimeoutCommand { get; }
    public IAsyncRelayCommand<string> SetProbeParallelismCommand { get; }
    public IRelayCommand<CoreDriveInfo?> SelectVolumeCommand { get; }
    public IAsyncRelayCommand? RunSmartFeatureCommand { get; private set; }
    public IAsyncRelayCommand? ClearSelfTestResultCommand { get; private set; }
    public IAsyncRelayCommand NavigateToBackupCommand { get; }
    public IRelayCommand ForceTestAnywayCommand { get; }

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        _ = LoadDisksAsync();
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<DiskSelectionViewModel>();
    }

    private void SelectVolume(CoreDriveInfo? volume)
    {
        if (volume == null)
        {
            return;
        }

        var card = new DiskStatusCardItem
        {
            Drive = volume,
            DisplayName = !string.IsNullOrEmpty(volume.Name) ? volume.Name : volume.Path,
            DisplayPath = volume.Path,
            CapacityText = FormatCapacity(volume.TotalSize),
            IsSystemDisk = volume.IsSystemDisk,
            IsSystemDiskLabel = volume.IsSystemDisk ? "Systémový" : string.Empty
        };

        _selectedDiskService.SelectedDisk = volume;
        _selectedDiskService.SelectedDiskDisplayName = card.DisplayName;
        SelectedDisk = card;
    }

    private async Task LoadDisksAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Načítám seznam disků...";
            
            var drives = (await _diskDetectionService.GetDrivesAsync()).ToList();

            Disks.Clear();
            var systemDiskPath = "\\.\\PhysicalDrive0";
            var preferredDisk = _selectedDiskService.SelectedDisk;

            // Concurrency strategy: cores*2, unless cores >= 16 then cores*1
            var cores = Environment.ProcessorCount;
            var maxParallel = cores >= 16 ? cores : Math.Max(2, Math.Min(12, cores * 2));
            using var semaphore = new SemaphoreSlim(maxParallel);
            var timeoutPerDrive = TimeSpan.FromSeconds(4);

            // Create placeholders immediately so UI shows items and progress
            for (int i = 0; i < drives.Count; i++)
            {
                var d = drives[i];
                var placeholder = new DiskStatusCardItem
                {
                    Drive = d,
                    DisplayName = d.Name ?? d.Path,
                    DisplayPath = d.Path,
                    CapacityText = FormatCapacity(d.TotalSize),
                    GradeText = "-",
                    TemperatureText = "...",
                    IsSystemDisk = d.Path.Contains(systemDiskPath) || IsSystemDisk(d),
                    IsSystemDiskLabel = d.Path.Contains(systemDiskPath) || IsSystemDisk(d) ? "Systémový" : "",
                    IsLoading = true
                };

                Disks.Add(placeholder);
            }

            var total = drives.Count;
            var processed = 0;

            // Detect connection speeds for all drives in parallel
            var speedDetectionTasks = new List<Task>();
            for (int i = 0; i < drives.Count; i++)
            {
                var idx = i;
                var drive = drives[i];
                speedDetectionTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var (speedMbps, speedDesc) = await _diskDetectionService.DetectConnectionSpeedAsync(drive.Path, drive.BusType);
                        drive.ConnectionSpeedMbps = speedMbps;
                        drive.ConnectionSpeedDescription = speedDesc;
                    }
                    catch
                    {
                        // Non-critical – just leave speed info empty
                    }
                }));
            }
            // Don't await – let speed detection run in background while SMART loads
            _ = Task.WhenAll(speedDetectionTasks).ContinueWith(_ =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var card in Disks)
                    {
                        if (card.Drive != null)
                        {
                            card.ConnectionSpeedMbps = card.Drive.ConnectionSpeedMbps;
                            card.ConnectionSpeedDescription = card.Drive.ConnectionSpeedDescription;
                        }
                    }
                });
            });

            // Local worker to probe single drive and update placeholder
            async Task ProbeDriveAsync(int index, CoreDriveInfo drive)
            {
                await semaphore.WaitAsync();
                try
                {
                    SmartaData? smartData = null;
                    string? error = null;
                    
                    // Skip SMART query if the drive doesn't support SMART
                    if (drive.SupportsSmart)
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(timeoutPerDrive);
                            smartData = await _smartaProvider.GetSmartaDataAsync(drive.Path, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            error = "Timeout při načítání SMART";
                            System.Diagnostics.Debug.WriteLine($"SMART read timed out for {drive.Path}");
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                            System.Diagnostics.Debug.WriteLine($"Error reading SMART for {drive.Path}: {ex.Message}");
                        }
                    }
                    else
                    {
                        error = "Disk nepodporuje SMART";
                    }

                    QualityRating? quality = null;
                    if (smartData != null)
                    {
                        quality = _qualityCalculator.CalculateQuality(smartData);
                    }

                    // Update the placeholder on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (index >= 0 && index < Disks.Count)
                        {
                            var card = Disks[index];
                            card.SmartData = smartData;
                            card.Quality = quality;
                            card.DisplayName = !string.IsNullOrEmpty(smartData?.DeviceModel) ? smartData.DeviceModel.Trim() : (drive.Name ?? drive.Path);
                            card.GradeText = quality?.Grade.ToString() ?? "N/A";
                            card.TemperatureText = smartData?.Temperature > 0 ? $"{smartData.Temperature}°C" : "N/A";
                            card.IsLoading = false;
                            card.ErrorMessage = error ?? string.Empty;
                            card.BusType = drive.BusType;
                            card.ConnectionSpeedMbps = drive.ConnectionSpeedMbps;
                            card.ConnectionSpeedDescription = drive.ConnectionSpeedDescription;
                        }

                        processed++;
                        StatusMessage = $"Načteno {processed}/{total} disků";
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }

            var tasks = new List<Task>();
            for (int i = 0; i < drives.Count; i++)
            {
                tasks.Add(ProbeDriveAsync(i, drives[i]));
            }

            await Task.WhenAll(tasks);

            var preferredSelection = preferredDisk != null
                ? Disks.FirstOrDefault(d =>
                    string.Equals(d.Drive?.Path, preferredDisk.Path, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(preferredDisk.SerialNumber) &&
                     string.Equals(d.Drive?.SerialNumber, preferredDisk.SerialNumber, StringComparison.OrdinalIgnoreCase)))
                : null;

            SelectedDisk = preferredSelection
                ?? Disks.FirstOrDefault(d => !d.IsSystemDisk)
                ?? Disks.FirstOrDefault();

            StatusMessage = $"Nalezeno {Disks.Count} disků";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.LoadFailed"), ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSmartDataAsync()
    {
        Console.WriteLine($"[SMART] LoadSmartDataAsync called, SelectedDisk: {SelectedDisk?.DisplayName ?? "null"}");
        
        _loadSmartDataCts?.Cancel();
        _loadSmartDataCts?.Dispose();
        _loadSmartDataCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var cancellationToken = _loadSmartDataCts.Token;

        if (SelectedDisk?.Drive == null)
        {
            Console.WriteLine("[SMART] No disk selected, clearing data");
            CurrentSmartData = null;
            CurrentQuality = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SmartAttributes.Clear();
                CriticalAttributes.Clear();
                SelfTestLog.Clear();
            });
            return;
        }

        try
        {
            IsChecking = true;
            StatusMessage = $"Načítám SMART data: {SelectedDisk.DisplayName}";
            
            // Skip SMART query if the drive doesn't support SMART
            if (!SelectedDisk.Drive.SupportsSmart)
            {
                StatusMessage = "SMART data nedostupná – disk nepodporuje SMART (např. USB adaptér)";
                CurrentSmartData = null;
                CurrentQuality = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SmartAttributes.Clear();
                    CriticalAttributes.Clear();
                    SelfTestLog.Clear();
                });
                UpdateComputedProperties();
                return;
            }

            var smartData = await _smartaProvider.GetSmartaDataAsync(SelectedDisk.Drive.Path, cancellationToken);
            
            if (smartData == null)
            {
                if (_smartaProvider.LastOperationWasPermissionDenied)
                {
                    StatusMessage = "⚠️ SMART data nedostupná – spusťte aplikaci s právy root (sudo)";
                    // Permission issue is informative - don't block with error dialog
                    // User can still see disk info and use other features
                }
                else
                {
                    StatusMessage = "SMART data nedostupná – disk nemusí podporovat SMART (např. USB adaptér)";
                    // SMART unavailability is informative, not an error
                    // The disk may still be perfectly functional
                }
                
                // Clear SMART-dependent data but keep disk selected
                CurrentSmartData = null;
                CurrentQuality = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SmartAttributes.Clear();
                    CriticalAttributes.Clear();
                    SelfTestLog.Clear();
                });
                UpdateComputedProperties();
                return;
            }
            
            // Determine device type from SMART data
            if (smartData.Attributes?.Any(a => a.Id == 5) == true && 
                smartData.Attributes.Any(a => a.Id == 194))
            {
                smartData.DeviceType = "SATA/ATA";
            }
            
            // Ensure RetrievedAtUtc is set
            if (smartData.RetrievedAtUtc == null)
                smartData.RetrievedAtUtc = DateTime.UtcNow;

            MergeCriticalCountersFromAttributes(smartData, smartData.Attributes);
            CurrentSmartData = smartData;
            CurrentQuality = _qualityCalculator.CalculateQuality(smartData!);

            await PersistSmartSnapshotAsync(SelectedDisk.Drive, smartData, CurrentQuality);

            // Build cache info text
            if (smartData.RetrievedAtUtc != null)
            {
                var age = DateTime.UtcNow - smartData.RetrievedAtUtc.Value;
                var ageText = age.TotalMinutes >= 1 ? $"{(int)age.TotalMinutes} min" : $"{(int)age.TotalSeconds} s";
                SmartCacheInfo = smartData.IsFromCache ? $"Data z cache ({ageText})" : $"Aktuální data ({ageText})";
                if (_smartaProvider is IAdvancedSmartaProvider statsProvider)
                {
                    try
                    {
                        var stats = await statsProvider.GetSmartCacheStatsAsync();
                        SmartCacheStats = $"Cache: hits={stats.Hits} misses={stats.Misses} items={stats.Items}";
                    }
                    catch { SmartCacheStats = string.Empty; }
                }
            }
            else
            {
                SmartCacheInfo = string.Empty;
            }
            
            // Update disk card
            if (SelectedDisk != null)
            {
                SelectedDisk.SmartData = smartData;
                SelectedDisk.Quality = CurrentQuality;
                SelectedDisk.TemperatureText = smartData?.Temperature > 0 ? $"{smartData.Temperature}°C" : "N/A";
                SelectedDisk.GradeText = CurrentQuality?.Grade.ToString() ?? "?";
            }
            
            // Load attributes using advanced provider
            Console.WriteLine($"[SMART] Loading attributes, provider type: {_smartaProvider?.GetType().Name}");
            if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
            {
                var devicePath = SelectedDisk!.Drive.Path;
                Console.WriteLine($"[SMART] Device path: {devicePath}");
                try
                {
                    var attributes = await advancedProvider.GetSmartAttributesAsync(devicePath, cancellationToken);
                    Console.WriteLine($"[SMART] GetSmartAttributesAsync returned {attributes?.Count ?? -1} attributes");
                    
                    MergeCriticalCountersFromAttributes(smartData!, attributes);
                    CurrentQuality = _qualityCalculator.CalculateQuality(smartData);

                    // Update collections on UI thread for proper CollectionChanged events
                    Console.WriteLine($"[SMART] Updating collections on UI thread...");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Console.WriteLine($"[SMART] Inside UI thread, attributes count: {attributes?.Count ?? -1}");
                        SmartAttributes.Clear();
                        if (attributes != null)
                        {
                            foreach (var attr in attributes)
                                SmartAttributes.Add(attr);
                        }
                        Console.WriteLine($"[SMART] SmartAttributes now has {SmartAttributes.Count} items");
                        
                        CriticalAttributes.Clear();
                        foreach (var attr in BuildCriticalAttributes(smartData!, attributes))
                        {
                            CriticalAttributes.Add(attr);
                        }
                        UpdateComputedProperties();
                        Console.WriteLine($"[SMART] CriticalAttributes now has {CriticalAttributes.Count} items");
                    });
                    System.Diagnostics.Debug.WriteLine($"Found {CriticalAttributes.Count} critical attributes");
                }
                catch (OperationCanceledException)
                {
                    StatusMessage = "Načtení SMART atributů vypršelo";
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SMART] ERROR loading attributes: {ex.Message}");
                    Console.WriteLine($"[SMART] Stack trace: {ex.StackTrace}");
                    System.Diagnostics.Debug.WriteLine($"Error loading SMART attributes: {ex.Message}");
                }
                
                try
                {
                    var log = await advancedProvider.GetSelfTestLogAsync(devicePath, cancellationToken);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SelfTestLog.Clear();
                        foreach (var entry in log)
                            SelfTestLog.Add(entry);
                    });
                    
                    var status = await advancedProvider.GetSelfTestStatusAsync(devicePath, cancellationToken);
                    if (status == SmartaSelfTestStatus.InProgress)
                    {
                        if (CurrentSmartData != null)
                        {
                            CurrentSmartData.SelfTestInProgress = true;
                        }
                        StatusMessage = "⚠️ Self-test právě běží...";
                    }
                }
                catch (OperationCanceledException)
                {
                    StatusMessage = "Načtení SMART self-test logu vypršelo";
                    return;
                }
                catch
                {
                    /* Self-test log not supported */
                }
            }
            
            UpdateComputedProperties();
            StatusMessage = $"SMART načten: {CurrentSmartData?.HealthStatus ?? "-"}";
            
            // Load raw smartctl output
            _ = RefreshRawOutput();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Načtení SMART dat trvalo příliš dlouho";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task RefreshAsync()
    {
        await LoadSmartDataAsync();
    }

    private async Task PersistSmartSnapshotAsync(CoreDriveInfo drive, SmartaData smartData, QualityRating rating)
    {
        ArgumentNullException.ThrowIfNull(drive);
        ArgumentNullException.ThrowIfNull(smartData);

        if (smartData.IsFromCache)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (string.Equals(_lastSmartPersistDiskPath, drive.Path, StringComparison.OrdinalIgnoreCase) &&
            (now - _lastSmartPersistUtc) < TimeSpan.FromSeconds(30))
        {
            return;
        }

        try
        {
            var card = await _cardTestService.GetOrCreateCardAsync(drive, smartData);
            await _cardTestService.SaveSmartCheckAsync(card, smartData, rating, _currentSmartTestType);
            _lastSmartPersistDiskPath = drive.Path;
            _lastSmartPersistUtc = now;
        }
        catch (DbUpdateException ex)
        {
            StatusMessage = $"SMART načten, ale uložení do karty selhalo: {ex.InnerException?.Message ?? ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"SMART načten, ale uložení do karty selhalo: {ex.Message}";
        }
    }

    private async Task ClearCacheForSelectedAsync()
    {
        if (SelectedDisk?.Drive == null) return;

        if (_smartaProvider is IAdvancedSmartaProvider advanced)
        {
            await advanced.RemoveSmartCacheForDeviceAsync(SelectedDisk.Drive.Path);
            SmartCacheInfo = "Cache vymazána pro tento disk";
            await LoadSmartDataAsync();
        }
    }

    private async Task ClearAllSmartCacheAsync()
    {
        if (_smartaProvider is IAdvancedSmartaProvider advanced)
        {
            await advanced.ClearSmartCacheAsync();
            SmartCacheInfo = "Globální SMART cache vymazána";
            await LoadSmartDataAsync();
        }
    }

    private async Task SetCacheTtlAsync(int minutes)
    {
        if (_smartaProvider is IAdvancedSmartaProvider advanced)
        {
            await advanced.SetCacheTtlMinutesAsync(minutes);
            SmartCacheInfo = $"Cache TTL nastaveno na {minutes} min";
            try
            {
                var stats = await advanced.GetSmartCacheStatsAsync();
                SmartCacheStats = $"Cache: hits={stats.Hits} misses={stats.Misses} items={stats.Items}";
            }
            catch { }
        }
    }

    private async Task SetCacheTtlFromStringAsync(string s)
    {
        if (int.TryParse(s, out var minutes) && minutes > 0)
        {
            // Persist new TTL and apply to provider
            try
            {
                await _settingsService.SetSmartCacheTtlMinutesAsyncPersistent(minutes);
            }
            catch { }

            if (_smartaProvider is IAdvancedSmartaProvider adv)
            {
                try
                {
                    await adv.SetCacheTtlMinutesAsync(minutes);
                }
                catch { }
            }

            SmartCacheInfo = $"Cache TTL nastaveno na {minutes} min";
        }
        else
        {
            SmartCacheInfo = "Neplatná hodnota TTL";
        }
    }

    private async Task SetProbeTimeoutFromStringAsync(string s)
    {
        if (int.TryParse(s, out var seconds) && seconds > 0)
        {
            // persist
            await _settingsService.SetSmartProbeTimeoutSecondsAsync(seconds);
            SmartCacheInfo = $"Timeout probe nastaven na {seconds} s";
        }
        else
        {
            SmartCacheInfo = "Neplatná hodnota timeoutu";
        }
    }

    private async Task SetProbeParallelismFromStringAsync(string s)
    {
        if (int.TryParse(s, out var p) && p >= 0)
        {
            await _settingsService.SetSmartProbeParallelismAsync(p);
            SmartCacheInfo = p == 0 ? "Paralelismus: auto" : $"Paralelismus nastaven: {p}";
        }
        else
        {
            SmartCacheInfo = "Neplatná hodnota paralelismu";
        }
    }

    private async Task RunShortTestAsync()
    {
        await RunSelfTestAsync(SmartaSelfTestType.ShortTest, "krátký", TestType.SmartShort);
    }

    private async Task RunLongTestAsync()
    {
        await RunSelfTestAsync(SmartaSelfTestType.Extended, "rozšířený", TestType.SmartExtended);
    }

    private async Task RunConveyanceTestAsync()
    {
        await RunSelfTestAsync(SmartaSelfTestType.Conveyance, "přepravní", TestType.SmartConveyance);
    }

    private async Task RunSelfTestAsync(SmartaSelfTestType testType, string testName, TestType sessionTestType = TestType.SmartShort)
    {
        if (SelectedDisk?.Drive == null) return;
        
        if (SelectedDisk.IsSystemDisk)
        {
            await _dialogService.ShowErrorAsync(L.Get("Common.Warning"), 
                "Na systémovém disku nelze spustit self-test.\n\nVyberte jiný disk.");
            return;
        }

        _currentSmartTestType = sessionTestType;

        var result = await ShowSelfTestConfirmationAsync(testName);
        if (result == SelfTestConfirmationResult.Cancel) return;

        try
        {
            IsChecking = true;
            StatusMessage = "Spouštím " + testName + " self-test...";
            
            if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
            {
                var success = await advancedProvider.StartSelfTestAsync(SelectedDisk.Drive.Path, testType);
                
                if (success)
                {
                    StatusMessage = "✅ " + testName.Capitalize() + " self-test spuštěn";
                    
                    try
                    {
                        await advancedProvider.RemoveSmartCacheForDeviceAsync(SelectedDisk.Drive.Path);
                        if (!string.IsNullOrWhiteSpace(CurrentSmartData?.SerialNumber))
                            await advancedProvider.RemoveSmartCacheForSerialAsync(CurrentSmartData.SerialNumber);
                    }
                    catch { }
                    
                    if (result == SelfTestConfirmationResult.StartWithPolling)
                    {
                        // Set the test type text for progress display
                        _currentTestType = testName.Capitalize() + " self-test";
                        _wasTestInProgress = true; // Mark test as running for completion detection
                        
                        // Start polling for progress
                        _ = StartPollingSelfTestProgressCommand.ExecuteAsync(null);
                    }
                    else
                    {
                        await _dialogService.ShowSuccessAsync(L.Get("SmartCheck.SelfTest"), 
                            string.Format(L.Get("SmartCheck.Dialog.TestStarted"), testName));
                    }
                }
                else
                {
                    StatusMessage = "❌ Nepodařilo se spustit self-test";
                    await _dialogService.ShowErrorAsync(L.Get("Common.Error"), "Nepodařilo se spustit self-test. Ujistěte se, že disk podporuje SMART.");
                }
            }
            else
            {
                await _dialogService.ShowErrorAsync(L.Get("Common.Unsupported"), 
                    "Self-test není podporován na tomto systému.\n\n" +
                    "Ujistěte se, že je nainstalován smartmontools.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Chyba: " + ex.Message;
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), ex.Message);
        }
        finally
        {
            IsChecking = false;
        }
    }


    // Self-test confirmation result
    private enum SelfTestConfirmationResult { Cancel, Start, StartWithPolling }
    
    private async Task<SelfTestConfirmationResult> ShowSelfTestConfirmationAsync(string testName)
    {
        var message = "Spustit " + testName + " self-test na disku:\n\n" +
                      "📋 " + (SelectedDisk?.DisplayName ?? "Neznámý") + "\n\n" +
                      "Možnosti:\n" +
                      "• Spustit - jen spustí test\n" +
                      "• Spustit s monitoringem - sleduje průběh\n\n" +
                      "⚠️ Během testu může být disk dočasně nedostupný.";
        
        var confirmed = await _dialogService.ShowConfirmationAsync(
            L.Get("SmartCheck.Dialog.StartTest", testName),
            message);
        
        if (!confirmed) return SelfTestConfirmationResult.Cancel;
        
        var wantPolling = await _dialogService.ShowConfirmationAsync(
            L.Get("SmartCheck.Dialog.MonitorPrompt"),
            L.Get("SmartCheck.Dialog.MonitorMessage"));
        
        return wantPolling ? SelfTestConfirmationResult.StartWithPolling : SelfTestConfirmationResult.Start;
    }

private async Task AbortTestAsync()
    {
        if (SelectedDisk?.Drive == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(L.Get("Common.Confirmation"), 
            "Opravdu chcete přerušit běžící self-test?");
        
        if (!confirmed) return;

        try
        {
            IsChecking = true;
            
            if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
            {
                var success = await advancedProvider.StartSelfTestAsync(SelectedDisk.Drive.Path, SmartaSelfTestType.Abort);
                StatusMessage = success ? "✅ Self-test přerušen" : "❌ Nepodařilo se přerušit test";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    
    [RelayCommand]
    private async Task RefreshRawOutput()
    {
        if (SelectedDisk?.Drive == null) return;
        
        try
        {
            var devicePath = SelectedDisk.Drive.Path;
            
            // Find smartctl - cross-platform
            string? smartctlPath = null;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: use same paths as LinuxSmartaProvider + which/command -v
                var linuxPaths = new[]
                {
                    "/usr/sbin/smartctl",
                    "/usr/bin/smartctl",
                    "/usr/local/sbin/smartctl",
                    "/usr/local/bin/smartctl",
                    "/sbin/smartctl",
                    "/bin/smartctl",
                    "/snap/bin/smartctl"
                };
                
                foreach (var p in linuxPaths)
                {
                    if (File.Exists(p)) { smartctlPath = p; break; }
                }
                
                if (smartctlPath == null)
                {
                    // Try 'which'
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "/usr/bin/which",
                            Arguments = "smartctl",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            await proc.WaitForExitAsync();
                            if (proc.ExitCode == 0)
                            {
                                var output = await proc.StandardOutput.ReadToEndAsync();
                                smartctlPath = output.Trim().Split('\n')[0].Trim();
                            }
                        }
                    }
                    catch { }
                }
                
                if (smartctlPath == null)
                {
                    // Try 'command -v'
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "/bin/sh",
                            Arguments = "-c \"command -v smartctl\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            await proc.WaitForExitAsync();
                            if (proc.ExitCode == 0)
                            {
                                var output = await proc.StandardOutput.ReadToEndAsync();
                                smartctlPath = output.Trim().Split('\n')[0].Trim();
                            }
                        }
                    }
                    catch { }
                }
            }
            else
            {
                // Windows: check known install paths + where
                var windowsPaths = new[]
                {
                    @"C:\Program Files\smartmontools\bin\smartctl.exe",
                    @"C:\Program Files (x86)\smartmontools\bin\smartctl.exe",
                    @"C:\ProgramData\chocolatey\bin\smartctl.exe"
                };
                
                foreach (var p in windowsPaths)
                {
                    if (File.Exists(p)) { smartctlPath = p; break; }
                }
                
                if (smartctlPath == null)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "where",
                            Arguments = "smartctl",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            await proc.WaitForExitAsync();
                            if (proc.ExitCode == 0)
                            {
                                var output = await proc.StandardOutput.ReadToEndAsync();
                                smartctlPath = output.Trim().Split('\n')[0].Trim();
                            }
                        }
                    }
                    catch { }
                }
            }
            
            if (smartctlPath == null)
            {
                RawSmartOutput = "smartctl nenalezen. Nainstalujte smartmontools.";
                return;
            }
            
            // Build device-specific arguments
            string args;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: use actual device path with device type detection
                var deviceType = devicePath.Contains("nvme", StringComparison.OrdinalIgnoreCase) ? "nvme" : "auto";
                args = deviceType == "nvme" 
                    ? $"-d nvme -a {devicePath}" 
                    : $"-a {devicePath}";
            }
            else
            {
                // Windows: use /dev/pdN format
                var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(driveNumber))
                {
                    RawSmartOutput = "Nelze určit číslo disku pro Windows smartctl.";
                    return;
                }
                args = $"-a /dev/pd{driveNumber}";
            }
            
            var startInfo = new ProcessStartInfo
            {
                FileName = smartctlPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                RawSmartOutput = "Nepodařilo se spustit smartctl.";
                return;
            }
            
            // Read output and wait for exit in parallel to avoid deadlock
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            
            // Timeout after 15 seconds to avoid hanging on unresponsive disks
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                RawSmartOutput = "Timeout: smartctl neodpověděl do 15 sekund.";
                return;
            }
            
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            
            var output2 = stdout;
            if (!string.IsNullOrEmpty(stderr) && !stderr.Contains("smartctl", StringComparison.OrdinalIgnoreCase))
                output2 += "\n\n--- Errors ---\n" + stderr;
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RawSmartOutput = output2;
            });
        }
        catch (Exception ex)
        {
            RawSmartOutput = "Chyba: " + ex.Message;
        }
    }


    private static IEnumerable<SmartaAttributeItem> BuildCriticalAttributes(SmartaData smartData, IReadOnlyList<SmartaAttributeItem>? providerAttributes)
    {
        var criticalIds = new[] { 5, 197, 198, 187, 188, 196, 199, 170, 174, 177, 179, 181, 182, 190, 194, 195, 231, 233 };
        var attrs = (providerAttributes?.Count > 0 ? providerAttributes : smartData.Attributes) ?? new List<SmartaAttributeItem>();
        var result = attrs
            .Where(a => criticalIds.Contains(a.Id) || IsCriticalAttributeName(a.Name))
            .GroupBy(a => a.Id)
            .Select(g => g.OrderByDescending(a => a.RawValue).First())
            .OrderBy(a => Array.IndexOf(criticalIds, a.Id) < 0 ? int.MaxValue : Array.IndexOf(criticalIds, a.Id))
            .ToList();

        AddOrOverrideSynthetic(result, 5, "Reallocated Sectors", smartData.ReallocatedSectorCount);
        AddOrOverrideSynthetic(result, 197, "Current Pending Sectors", smartData.PendingSectorCount);
        AddOrOverrideSynthetic(result, 198, smartData.DeviceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ? "Media/Data Integrity Errors" : "Offline Uncorrectable", smartData.MediaErrors ?? smartData.UncorrectableErrorCount);
        AddOrOverrideSynthetic(result, 170, "Available Spare", smartData.AvailableSpare);
        AddOrOverrideSynthetic(result, 177, "SSD/NVMe Life Used", smartData.PercentageUsed);
        AddOrOverrideSynthetic(result, 174, "Unsafe Shutdowns", smartData.UnsafeShutdowns);
        return result;
    }

    private static void MergeCriticalCountersFromAttributes(SmartaData smartData, IReadOnlyList<SmartaAttributeItem>? providerAttributes)
    {
        var attrs = (providerAttributes?.Count > 0 ? providerAttributes : smartData.Attributes) ?? new List<SmartaAttributeItem>();

        smartData.ReallocatedSectorCount = MaxKnownCounter(smartData.ReallocatedSectorCount, FindRawCounter(attrs, 5, "Reallocated"));
        smartData.PendingSectorCount = MaxKnownCounter(smartData.PendingSectorCount, FindRawCounter(attrs, 197, "Pending"));
        smartData.UncorrectableErrorCount = MaxKnownCounter(smartData.UncorrectableErrorCount, FindRawCounter(attrs, 198, "Uncorrect"));
        smartData.MediaErrors = MaxKnownCounter(smartData.MediaErrors, FindRawCounter(attrs, 198, "Media"));
    }

    private static int? FindRawCounter(IEnumerable<SmartaAttributeItem> attrs, int id, string nameFragment)
    {
        var match = attrs
            .Where(a => a.Id == id || a.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.RawValue)
            .FirstOrDefault();

        return match == null ? null : (int)Math.Min(match.RawValue, int.MaxValue);
    }

    private static int? MaxKnownCounter(int? current, int? fromAttributes)
    {
        if (!fromAttributes.HasValue)
        {
            return current;
        }

        if (!current.HasValue)
        {
            return fromAttributes;
        }

        return Math.Max(current.Value, fromAttributes.Value);
    }

    private static bool IsCriticalAttributeName(string name) =>
        name.Contains("Reallocated", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Pending", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Uncorrect", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Media", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Spare", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Wear", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Percentage", StringComparison.OrdinalIgnoreCase);

    private static void AddOrOverrideSynthetic(List<SmartaAttributeItem> items, int id, string name, int? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        var normalized = Math.Max(0, value.Value);
        var existing = items.FirstOrDefault(a => a.Id == id);
        if (existing != null)
        {
            if (normalized > existing.RawValue)
            {
                existing.RawValue = (uint)normalized;
                existing.Name = string.IsNullOrWhiteSpace(existing.Name) ? name : existing.Name;
                existing.IsOk = normalized == 0;
                existing.Interpretation = normalized == 0 ? existing.Interpretation : $"{normalized:N0}";
            }
            return;
        }

        items.Add(new SmartaAttributeItem { Id = id, Name = name, RawValue = (uint)normalized, Value = 100, Worst = 100, Threshold = 0, IsOk = normalized == 0, Current = 100, Interpretation = normalized == 0 ? string.Empty : $"{normalized:N0}" });
    }

private void UpdateComputedProperties()
    {
        OnPropertyChanged(nameof(DeviceModel));
        OnPropertyChanged(nameof(SerialNumber));
        OnPropertyChanged(nameof(FirmwareVersion));
        OnPropertyChanged(nameof(DeviceType));
        OnPropertyChanged(nameof(Temperature));
        OnPropertyChanged(nameof(TemperatureStatus));
        OnPropertyChanged(nameof(PowerOnHours));
        OnPropertyChanged(nameof(LifetimeStatus));
        OnPropertyChanged(nameof(PowerCycles));
        OnPropertyChanged(nameof(Grade));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(HealthStatus));
        OnPropertyChanged(nameof(GradeColor));
        OnPropertyChanged(nameof(TempColor));
        OnPropertyChanged(nameof(HealthColor));
        OnPropertyChanged(nameof(IsNvMe));
        OnPropertyChanged(nameof(IsSelfTestRunning));
        OnPropertyChanged(nameof(CanRunSelfTest));
        OnPropertyChanged(nameof(SelfTestProgress));
        OnPropertyChanged(nameof(SelfTestProgressText));
        OnPropertyChanged(nameof(IsFailing));
        OnPropertyChanged(nameof(FailurePrediction));
        OnPropertyChanged(nameof(HasFailingAttributes));
        OnPropertyChanged(nameof(FailingAttributesSummary));
        OnPropertyChanged(nameof(SelfTestTypeText));
        OnPropertyChanged(nameof(NvMePercentageUsed));
        OnPropertyChanged(nameof(NvMeAvailableSpare));
        OnPropertyChanged(nameof(SsdWearIndicator));
        OnPropertyChanged(nameof(NvMeMediaErrors));
        OnPropertyChanged(nameof(NvMeUnsafeShutdowns));
    }

    [RelayCommand]
    private async Task StartPollingSelfTestProgress()
    {
        if (SelectedDisk?.Drive == null) return;
        
        _selfTestPollingCts?.Cancel();
        _selfTestPollingCts = new CancellationTokenSource();
        var token = _selfTestPollingCts.Token;
        // Note: _wasTestInProgress is set to true when test is started in RunSelfTestAsync
        // We don't reset it here because test might complete before first polling cycle
        
        try
        {
            IsSelfTestRunning = true;
            SelfTestProgress = 0;
            SelfTestProgressText = "Self-test probíhá...";
            SelfTestTypeText = string.IsNullOrEmpty(_currentTestType) ? "Self-test" : _currentTestType;
            StatusMessage = "⏳ Self-test probíhá, sleduji průběh...";
            ClearSelfTestResult();
            
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(3000, token); // Poll every 3 seconds
                
                if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
                {
                    try
                    {
                        var status = await advancedProvider.GetSelfTestStatusAsync(SelectedDisk.Drive.Path);
                        
                        if (status == SmartaSelfTestStatus.InProgress)
                        {
                            _wasTestInProgress = true;
                            
                            // Try to get progress from raw output
                            await RefreshRawOutput();
                            
                            // Parse progress from RawSmartOutput
                            // Try multiple patterns for different drive types (SATA, NVMe)
                            var progressMatch = System.Text.RegularExpressions.Regex.Match(
                                RawSmartOutput, 
                                @"(\d+)%\s*(?:of test )?remaining",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            if (progressMatch.Success && int.TryParse(progressMatch.Groups[1].Value, out var remainingPercent))
                            {
                                SelfTestProgress = 100 - remainingPercent;
                                SelfTestProgressText = $"Self-test probíhá: {SelfTestProgress}% dokončeno";
                                StatusMessage = $"⏳ Self-test: {SelfTestProgress}% dokončeno ({remainingPercent}% zbývá)";
                            }
                            else
                            {
                                // Try NVMe format: "Self-test in progress, X% complete" or just "X% complete"
                                var nvmeMatch = System.Text.RegularExpressions.Regex.Match(
                                    RawSmartOutput,
                                    @"(\d+)\s*%\s*(?:complete|completed)",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                
                                if (nvmeMatch.Success && int.TryParse(nvmeMatch.Groups[1].Value, out var completedPercent))
                                {
                                    SelfTestProgress = completedPercent;
                                    SelfTestProgressText = $"Self-test probíhá: {SelfTestProgress}% dokončeno";
                                    StatusMessage = $"⏳ Self-test: {SelfTestProgress}% dokončeno ({100 - completedPercent}% zbývá)";
                                }
                                else
                                {
                                    // Check for "in progress" without percentage
                                    if (RawSmartOutput.Contains("in progress", System.StringComparison.OrdinalIgnoreCase) ||
                                        RawSmartOutput.Contains("self_test_status", System.StringComparison.OrdinalIgnoreCase))
                                    {
                                        SelfTestProgressText = "Self-test probíhá...";
                                        StatusMessage = "⏳ Self-test probíhá...";
                                    }
                                }
                            }
                        }
                        else if (status == SmartaSelfTestStatus.Unknown)
                        {
                            // Unknown status - could be test just completed or NVMe drive
                            // Check raw output for "in progress" indicators
                            await RefreshRawOutput();
                            
                            // Check if test is actually in progress (NVMe may report Unknown but has "in progress" in output)
                            if (RawSmartOutput.Contains("in progress", System.StringComparison.OrdinalIgnoreCase) ||
                                RawSmartOutput.Contains("self_test_status", System.StringComparison.OrdinalIgnoreCase) ||
                                RawSmartOutput.Contains("progress", System.StringComparison.OrdinalIgnoreCase))
                            {
                                // Test is still running - set InProgress manually
                                _wasTestInProgress = true;
                                status = SmartaSelfTestStatus.InProgress;
                                
                                // Try to parse progress from NVMe format
                                var nvmeMatch = System.Text.RegularExpressions.Regex.Match(
                                    RawSmartOutput,
                                    @"(\d+)\s*%\s*(?:complete|completed)",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                
                                if (nvmeMatch.Success && int.TryParse(nvmeMatch.Groups[1].Value, out var completedPercent))
                                {
                                    SelfTestProgress = completedPercent;
                                    SelfTestProgressText = $"Self-test probíhá: {SelfTestProgress}% dokončeno";
                                    StatusMessage = $"⏳ Self-test: {SelfTestProgress}% dokončeno";
                                }
                                else
                                {
                                    SelfTestProgressText = "Self-test probíhá...";
                                    StatusMessage = "⏳ Self-test probíhá...";
                                }
                                
                                // Continue polling
                                continue;
                            }
                            
                            // Check for completion indicators in raw output (NVMe: "SMART overall-health self-assessment test result: PASSED")
                            if (RawSmartOutput.Contains("PASSED", System.StringComparison.OrdinalIgnoreCase) ||
                                RawSmartOutput.Contains("FAILED", System.StringComparison.OrdinalIgnoreCase))
                            {
                                // Test completed - determine result from raw output
                                var resultColor = RawSmartOutput.Contains("PASSED", System.StringComparison.OrdinalIgnoreCase)
                                    ? "#27AE60"
                                    : "#E74C3C";
                                var resultText = RawSmartOutput.Contains("PASSED", System.StringComparison.OrdinalIgnoreCase)
                                    ? "✅ Self-test dokončen bez chyb"
                                    : "❌ Self-test dokončen s chybami";
                                
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    IsSelfTestRunning = false;
                                    SelfTestProgress = 100;
                                    SelfTestProgressText = "Self-test dokončen";
                                    StatusMessage = resultText;
                                    SetSelfTestResult(resultText, resultColor);
                                });
                                
                                await RefreshCommand.ExecuteAsync(null);
                                break;
                            }
                            
                            // Not in progress - check if test was running before (completion detection)
                            if (_wasTestInProgress)
                            {
                                // Test was running before, now Unknown - likely completed
                                var log = await advancedProvider.GetSelfTestLogAsync(SelectedDisk.Drive.Path);
                                var latestTest = log.FirstOrDefault();
                                
                                if (latestTest != null && latestTest.Status != SmartaSelfTestStatus.InProgress)
                                {
                                    var resultColor = GetStatusColor(latestTest.Status);
                                    var resultText = GetStatusText(latestTest);
                                    
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        IsSelfTestRunning = false;
                                        SelfTestProgress = 100;
                                        SelfTestProgressText = "Self-test dokončen";
                                        StatusMessage = resultText;
                                        SetSelfTestResult(resultText, resultColor);
                                    });
                                    
                                    await RefreshCommand.ExecuteAsync(null);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Any other status (CompletedWithoutError, AbortedByUser, etc.)
                            var resultColor = GetStatusColor(status);
                            var resultText = GetStatusMessage(status);
                            
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                IsSelfTestRunning = false;
                                SelfTestProgress = 100;
                                SelfTestProgressText = "Self-test dokončen";
                                StatusMessage = resultText;
                                SetSelfTestResult(resultText, resultColor);
                            });
                            
                            await RefreshCommand.ExecuteAsync(null);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SMART] Polling error: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Polling cancelled
        }
        finally
        {
            IsSelfTestRunning = false;
        }
    }
    
    private string GetStatusColor(SmartaSelfTestStatus status)
    {
        return status switch
        {
            SmartaSelfTestStatus.CompletedWithoutError => "#27AE60",
            SmartaSelfTestStatus.AbortedByUser => "#E74C3C",
            SmartaSelfTestStatus.AbortedByHost => "#F39C12",
            SmartaSelfTestStatus.FatalError => "#E74C3C",
            SmartaSelfTestStatus.ErrorUnknown => "#E74C3C",
            SmartaSelfTestStatus.ErrorElectrical => "#E74C3C",
            SmartaSelfTestStatus.ErrorServo => "#E74C3C",
            SmartaSelfTestStatus.ErrorRead => "#E74C3C",
            SmartaSelfTestStatus.ErrorHandling => "#E74C3C",
            _ => "#666666"
        };
    }
    
    private string GetStatusMessage(SmartaSelfTestStatus status)
    {
        return status switch
        {
            SmartaSelfTestStatus.CompletedWithoutError => "✅ Self-test dokončen bez chyb",
            SmartaSelfTestStatus.AbortedByUser => "❌ Self-test byl přerušen uživatelem",
            SmartaSelfTestStatus.AbortedByHost => "⚠️ Self-test byl přerušen systémem",
            SmartaSelfTestStatus.FatalError => "❌ Self-test selhal - fatální chyba",
            SmartaSelfTestStatus.ErrorUnknown => "❌ Self-test selhal - neznámá chyba",
            SmartaSelfTestStatus.ErrorElectrical => "❌ Self-test selhal - elektrická chyba",
            SmartaSelfTestStatus.ErrorServo => "❌ Self-test selhal - mechanická chyba",
            SmartaSelfTestStatus.ErrorRead => "❌ Self-test selhal - chyba čtení",
            SmartaSelfTestStatus.ErrorHandling => "❌ Self-test selhal - chyba obsluhy",
            SmartaSelfTestStatus.NoTest => "ℹ️ Žádný test nebyl spuštěn",
            _ => $"ℹ️ Self-test: {status}"
        };
    }
    
    private string GetStatusText(SmartaSelfTestEntry entry)
    {
        var statusText = GetStatusMessage(entry.Status);
        var testName = entry.TestTypeName ?? "Test";
        return $"{statusText} ({testName})";
    }

    private void StopPollingSelfTestProgress()
    {
        _selfTestPollingCts?.Cancel();
        _selfTestPollingCts?.Dispose();
        _selfTestPollingCts = null;
        IsSelfTestRunning = false;
    }



    private static bool IsSystemDisk(CoreDriveInfo drive)
    {
        // PhysicalDrive0 is usually the system disk on Windows.
        // Robust detection: parse any digits in the path and treat index 0 as system disk.
        if (drive == null || string.IsNullOrEmpty(drive.Path)) return false;

        try
        {
            // Prefer explicit PhysicalDrive0 match
            if (drive.Path.Contains("PhysicalDrive0", StringComparison.OrdinalIgnoreCase))
                return true;

            // Extract digits and parse
            var digits = new string(drive.Path.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digits) && int.TryParse(digits, out var index))
            {
                return index == 0;
            }
        }
        catch { }

        return false;
    }

    private static string FormatCapacity(long bytes)
    {
        if (bytes <= 0) return "Unknown";
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1000) return $"{gb / 1024.0:F1} TB";
        return $"{gb:F0} GB";
    }

    private static string GetGradeColor(QualityGrade? grade)
    {
        return grade switch
        {
            QualityGrade.A => "#27AE60",
            QualityGrade.B => "#2ECC71",
            QualityGrade.C => "#F39C12",
            QualityGrade.D => "#E67E22",
            QualityGrade.E => "#E74C3C",
            QualityGrade.F => "#C0392B",
            _ => "#95A5A6"
        };
    }

    private static string GetTemperatureColor(int? temp)
    {
        if (temp == null) return "#888888";
        return temp switch
        {
            < 40 => "#27AE60",
            < 50 => "#2ECC71",
            < 60 => "#F39C12",
            < 70 => "#E67E22",
            _ => "#E74C3C"
        };
    }

    private static string GetHealthColor(double? score)
    {
        if (score == null) return "#888888";
        return score switch
        {
            >= 90 => "#27AE60",
            >= 80 => "#2ECC71",
            >= 70 => "#F1C40F",
            >= 60 => "#E67E22",
            >= 50 => "#E74C3C",
            _ => "#C0392B"
        };
    }

    #endregion

    public void SetSelfTestResult(string message, string color)
    {
        SelfTestResult = message;
        SelfTestResultColor = color;
        HasSelfTestResult = true;
    }
    
    public void ClearSelfTestResult()
    {
        HasSelfTestResult = false;
        SelfTestResult = "";
    }

    private async Task RunSmartFeatureAsync()
    {
        switch (SelectedSmartFeatureIndex)
        {
            case 0:
                await RunShortTestAsync();
                break;
            case 1:
                await RunLongTestAsync();
                break;
            case 2:
            case 3:
                await RefreshCommand.ExecuteAsync(null);
                break;
        }
    }
    
    private Task ClearSelfTestResultAsync()
    {
        ClearSelfTestResult();
        return Task.CompletedTask;
    }

    private async Task NavigateToBackupAsync()
    {
        if (SelectedDisk?.Drive == null) return;
        
        // Navigate to backup view with the failing disk pre-selected
        _selectedDiskService.SelectedDisk = SelectedDisk.Drive;
        _navigationService.NavigateTo<BackupViewModel>();
        await Task.CompletedTask;
    }

    private void ForceTestAnyway()
    {
        // User acknowledges the risk and wants to proceed with testing
        // The warning banner stays visible but the user can now use destructive tests
        StatusMessage = "⚠️ Uživatel pokračuje přes varování SMART — testování na vlastní riziko.";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var loadCts = Interlocked.Exchange(ref _loadSmartDataCts, null);
        try { loadCts?.Cancel(); } catch (ObjectDisposedException) { }
        loadCts?.Dispose();

        var cts = Interlocked.Exchange(ref _selfTestPollingCts, null);
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        cts?.Dispose();

        GC.SuppressFinalize(this);
    }
}


public static class StringExtensions
{
    public static string Capitalize(this string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
    }
}
