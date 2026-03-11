using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public SmartCheckViewModel(
        ISmartaProvider smartaProvider, 
        IDiskDetectionService diskDetectionService,
        IQualityCalculator qualityCalculator, 
        IDialogService dialogService,
        ISelectedDiskService selectedDiskService,
        ISettingsService settingsService)
    {
        _smartaProvider = smartaProvider ?? throw new ArgumentNullException(nameof(smartaProvider));
        _diskDetectionService = diskDetectionService ?? throw new ArgumentNullException(nameof(diskDetectionService));
        _qualityCalculator = qualityCalculator ?? throw new ArgumentNullException(nameof(qualityCalculator));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _selectedDiskService = selectedDiskService ?? throw new ArgumentNullException(nameof(selectedDiskService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        
        LoadDisksCommand = new AsyncRelayCommand(LoadDisksAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => CanRunSelfTest);
        RunShortTestCommand = new AsyncRelayCommand(RunShortTestAsync, () => CanRunSelfTest);
        RunLongTestCommand = new AsyncRelayCommand(RunLongTestAsync, () => CanRunSelfTest);
        AbortTestCommand = new AsyncRelayCommand(AbortTestAsync, () => SelectedDisk != null && (IsSelfTestRunning || !IsChecking));
        ClearCacheForSelectedCommand = new AsyncRelayCommand(ClearCacheForSelectedAsync, () => SelectedDisk != null);
        ClearAllSmartCacheCommand = new AsyncRelayCommand(ClearAllSmartCacheAsync);
        // Provide runtime TTL setter command (string-based for UI binding)
        SetCacheTtlCommand = new AsyncRelayCommand<string>(async s => await SetCacheTtlFromStringAsync(s ?? string.Empty));
        SetProbeTimeoutCommand = new AsyncRelayCommand<string>(async s => await SetProbeTimeoutFromStringAsync(s ?? string.Empty));
        SetProbeParallelismCommand = new AsyncRelayCommand<string>(async s => await SetProbeParallelismFromStringAsync(s ?? string.Empty));
        SelectVolumeCommand = new RelayCommand<CoreDriveInfo?>(SelectVolume);
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
                RefreshCommand.NotifyCanExecuteChanged();
                RunShortTestCommand.NotifyCanExecuteChanged();
                RunLongTestCommand.NotifyCanExecuteChanged();
                AbortTestCommand.NotifyCanExecuteChanged();
                _ = LoadSmartDataAsync();
            }
        }
    }

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
    
    // Self-test progress tracking
    private int _selfTestProgress;
    private string _selfTestProgressText = "";
    private string _selfTestTypeText = "";
    private bool _isSelfTestRunning;
    private CancellationTokenSource? _selfTestPollingCts;
    private bool _wasTestInProgress; // Track if test was running to detect completion
    
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
    public string NvMePercentageUsed => CurrentSmartData?.PercentageUsed > 0 
        ? $"{CurrentSmartData.PercentageUsed}%" : "-";
    public string NvMeMediaErrors => CurrentSmartData?.MediaErrors > 0 
        ? $"{CurrentSmartData.MediaErrors:N0}" : "0 ✓";
    public string NvMeUnsafeShutdowns => CurrentSmartData?.UnsafeShutdowns > 0 
        ? $"{CurrentSmartData.UnsafeShutdowns:N0}" : "-";
    
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

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        // Check if a disk was selected from disk selection view
        if (_selectedDiskService.SelectedDisk != null)
        {
            // Create disk card from selected disk
            var card = new DiskStatusCardItem
            {
                Drive = _selectedDiskService.SelectedDisk,
                DisplayName = _selectedDiskService.SelectedDiskDisplayName ?? _selectedDiskService.SelectedDisk.Name ?? "Unknown",
                DisplayPath = _selectedDiskService.SelectedDisk.Path,
                IsLocked = _selectedDiskService.IsSelectedDiskLocked
            };

            Disks.Clear();
            Disks.Add(card);
            SelectedDisk = card;
        }
        else
        {
            _ = LoadDisksAsync();
        }
    }

    private void SelectVolume(CoreDriveInfo? volume)
    {
        if (volume == null) return;

        // Create a disk card for the selected volume and set it as SelectedDisk
        var card = new DiskStatusCardItem
        {
            Drive = volume,
            DisplayName = !string.IsNullOrEmpty(volume.Name) ? volume.Name : volume.Path,
            DisplayPath = volume.Path,
            CapacityText = FormatCapacity(volume.TotalSize),
            IsSystemDisk = volume.IsSystemDisk,
            IsSystemDiskLabel = volume.IsSystemDisk ? "Systémový" : string.Empty
        };

        // Update shared selected disk service
        _selectedDiskService.SelectedDisk = volume;
        _selectedDiskService.SelectedDiskDisplayName = card.DisplayName;

        // Set selection which will trigger LoadSmartDataAsync
        SelectedDisk = card;
    }

    #endregion

    
    private async Task<string> GetRawSmartctlOutputAsync(string devicePath)
    {
        try
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"smartctl_output_{Guid.NewGuid():N}.txt");
            
            // Extract drive number from path
            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(driveNumber)) return "Chyba: Nelze určit číslo disku";
            
            var psi = new ProcessStartInfo
            {
                FileName = "smartctl.exe",
                Arguments = $"-a /dev/pd{driveNumber}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = @"C:\Program Files\smartmontools\bin"
            };
            
            using var process = Process.Start(psi);
            if (process == null) return "Chyba: Nelze spustit smartctl";
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            return output;
        }
        catch (Exception ex)
        {
            return $"Chyba při získávání SMART dat: {ex.Message}";
        }
    }

#region Private Methods

    private async Task LoadDisksAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Načítám seznam disků...";
            
            var drives = (await _diskDetectionService.GetDrivesAsync()).ToList();

            Disks.Clear();
            // Identify system disk (usually PhysicalDrive0)
            var systemDiskPath = "\\\\.\\PhysicalDrive0";

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

            // Local worker to probe single drive and update placeholder
            async Task ProbeDriveAsync(int index, CoreDriveInfo drive)
            {
                await semaphore.WaitAsync();
                try
                {
                    SmartaData? smartData = null;
                    string? error = null;
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

                    var quality = smartData != null
                        ? _qualityCalculator.CalculateQuality(smartData)
                        : new QualityRating(QualityGrade.F, 0);

                    // Update the placeholder on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (index >= 0 && index < Disks.Count)
                        {
                            var card = Disks[index];
                            card.SmartData = smartData;
                            card.Quality = quality;
                            card.DisplayName = !string.IsNullOrEmpty(smartData?.DeviceModel) ? smartData.DeviceModel.Trim() : (drive.Name ?? drive.Path);
                            card.GradeText = quality.Grade.ToString();
                            card.TemperatureText = smartData?.Temperature > 0 ? $"{smartData.Temperature}°C" : "N/A";
                            card.IsLoading = false;
                            card.ErrorMessage = error;
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

            // Ensure selection after probes
            SelectedDisk = Disks.FirstOrDefault(d => !d.IsSystemDisk) ?? Disks.FirstOrDefault();
            StatusMessage = $"Nalezeno {Disks.Count} disků";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst disky: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSmartDataAsync()
    {
        Console.WriteLine($"[SMART] LoadSmartDataAsync called, SelectedDisk: {SelectedDisk?.DisplayName ?? "null"}");
        
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
            
            var smartData = await _smartaProvider.GetSmartaDataAsync(SelectedDisk.Drive.Path);
            
            if (smartData == null)
            {
                StatusMessage = "Nepodařilo se načíst SMART data";
                await _dialogService.ShowErrorAsync("Chyba", 
                    $"Nepodařilo se načíst SMART data pro disk {SelectedDisk.DisplayName}.\n\n" +
                    "Ujistěte se, že:\n" +
                    "1. Aplikace běží s administrátorskými právy\n" +
                    "2. Disk podporuje SMART\n" +
                    "3. je nainstalován smartmontools");
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

            CurrentSmartData = smartData;
            CurrentQuality = _qualityCalculator.CalculateQuality(smartData);

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
                    var attributes = await advancedProvider.GetSmartAttributesAsync(devicePath);
                    Console.WriteLine($"[SMART] GetSmartAttributesAsync returned {attributes?.Count ?? -1} attributes");
                    
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
                        
                        var criticalIds = new[] { 5, 177, 179, 181, 182, 187, 188, 190, 194, 195, 196, 197, 198, 199, 231, 233 };
                        CriticalAttributes.Clear();
                        if (attributes != null)
                        {
                            foreach (var attr in attributes.Where(a => criticalIds.Contains(a.Id))
                                .OrderBy(a => Array.IndexOf(criticalIds, a.Id)))
                            {
                                CriticalAttributes.Add(attr);
                            }
                        }
                        Console.WriteLine($"[SMART] CriticalAttributes now has {CriticalAttributes.Count} items");
                    });
                    System.Diagnostics.Debug.WriteLine($"Found {CriticalAttributes.Count} critical attributes");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SMART] ERROR loading attributes: {ex.Message}");
                    Console.WriteLine($"[SMART] Stack trace: {ex.StackTrace}");
                    System.Diagnostics.Debug.WriteLine($"Error loading SMART attributes: {ex.Message}");
                }
                
                try
                {
                    var log = await advancedProvider.GetSelfTestLogAsync(devicePath);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SelfTestLog.Clear();
                        foreach (var entry in log)
                            SelfTestLog.Add(entry);
                    });
                    
                    var status = await advancedProvider.GetSelfTestStatusAsync(devicePath);
                    if (status == SmartaSelfTestStatus.InProgress)
                    {
                        if (CurrentSmartData != null)
                        {
                            CurrentSmartData.SelfTestInProgress = true;
                        }
                        StatusMessage = "⚠️ Self-test právě běží...";
                    }
                }
                catch { /* Self-test log not supported */ }
            }
            
            UpdateComputedProperties();
            StatusMessage = $"SMART načten: {CurrentSmartData?.HealthStatus ?? "-"}";
            
            // Load raw smartctl output
            _ = RefreshRawOutput();
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
        await RunSelfTestAsync(SmartaSelfTestType.ShortTest, "krátký");
    }

    private async Task RunLongTestAsync()
    {
        await RunSelfTestAsync(SmartaSelfTestType.Extended, "rozšířený");
    }

    private async Task RunSelfTestAsync(SmartaSelfTestType testType, string testName)
    {
        if (SelectedDisk?.Drive == null) return;
        
        if (SelectedDisk.IsSystemDisk)
        {
            await _dialogService.ShowErrorAsync("Upozornění", 
                "Na systémovém disku nelze spustit self-test.\n\nVyberte jiný disk.");
            return;
        }

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
                        _ = StartPollingSelfTestProgressCommand.ExecuteAsync(null);
                    }
                    else
                    {
                        await _dialogService.ShowSuccessAsync("Self-Test", 
                            testName.Capitalize() + " self-test byl úspěšně spuštěn.\n\n" +
                            "Výsledek zkontrolujte obnovením dat za několik minut.");
                    }
                }
                else
                {
                    StatusMessage = "❌ Nepodařilo se spustit self-test";
                    await _dialogService.ShowErrorAsync("Chyba", 
                        "Nepodařilo se spustit self-test. Ujistěte se, že disk podporuje SMART.");
                }
            }
            else
            {
                await _dialogService.ShowErrorAsync("Nepodporováno", 
                    "Self-test není podporován na tomto systému.\n\n" +
                    "Ujistěte se, že je nainstalován smartmontools.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Chyba: " + ex.Message;
            await _dialogService.ShowErrorAsync("Chyba", ex.Message);
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
            "Spustit " + testName + " self-test?", 
            message);
        
        if (!confirmed) return SelfTestConfirmationResult.Cancel;
        
        var wantPolling = await _dialogService.ShowConfirmationAsync(
            "Sledovat průběh?",
            "Chcete sledovat průběh self-testu v reálném čase?\n\n" +
            "Aplikace bude kontrolovat stav testu každých 5 sekund.");
        
        return wantPolling ? SelfTestConfirmationResult.StartWithPolling : SelfTestConfirmationResult.Start;
    }

private async Task AbortTestAsync()
    {
        if (SelectedDisk?.Drive == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync("Potvrzení", 
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
            string? smartctlPath = await FindSmartctlPathAsync();
            
            if (smartctlPath == null)
            {
                RawSmartOutput = "smartctl nenalezen. Nainstalujte smartmontools.\n" +
                    "Windows: winget install smartmontools\n" +
                    "Linux: sudo apt install smartmontools nebo sudo dnf install smartmontools";
                return;
            }
            
            // Determine device path for smartctl
            string devPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: use /dev/pdN format for Cygwin/MSYS smartctl
                var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(driveNumber))
                {
                    driveNumber = "0";
                }
                devPath = "/dev/pd" + driveNumber;
            }
            else
            {
                // Linux: use device path directly (/dev/sda, /dev/nvme0, etc.)
                devPath = devicePath;
            }
            
            var startInfo = new ProcessStartInfo
            {
                FileName = smartctlPath,
                Arguments = "-a " + devPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null) return;
            
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            var output2 = stdout;
            if (!string.IsNullOrEmpty(stderr) && !stderr.Contains("smartctl"))
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
    
    private static string? s_cachedSmartctlPath;
    
    private static async Task<string?> FindSmartctlPathAsync()
    {
        // Check cache first
        if (s_cachedSmartctlPath != null && File.Exists(s_cachedSmartctlPath))
            return s_cachedSmartctlPath;
        
        string[] commonPaths;
        string searchCommand;
        string searchArgs;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            commonPaths = new[] 
            { 
                @"C:\Program Files\smartmontools\bin\smartctl.exe",
                @"C:\Program Files (x86)\smartmontools\bin\smartctl.exe",
                @"C:\ProgramData\chocolatey\bin\smartctl.exe",
                @"C:\tools\smartmontools\smartctl.exe"
            };
            searchCommand = "where";
            searchArgs = "smartctl";
        }
        else
        {
            // Linux paths
            commonPaths = new[]
            {
                "/usr/sbin/smartctl",
                "/usr/bin/smartctl",
                "/usr/local/sbin/smartctl",
                "/usr/local/bin/smartctl",
                "/sbin/smartctl",
                "/bin/smartctl"
            };
            searchCommand = "/usr/bin/which";
            searchArgs = "smartctl";
        }
        
        // Check known paths first
        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                s_cachedSmartctlPath = path;
                return path;
            }
        }
        
        // Try search command
        try
        {
            var psi = new ProcessStartInfo 
            { 
                FileName = searchCommand, 
                Arguments = searchArgs, 
                RedirectStandardOutput = true, 
                UseShellExecute = false, 
                CreateNoWindow = true 
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode == 0)
                {
                    var path = output.Trim().Split('\n')[0].Trim();
                    if (File.Exists(path))
                    {
                        s_cachedSmartctlPath = path;
                        return path;
                    }
                }
            }
        }
        catch { }
        
        // Linux fallback: try command -v
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var psi = new ProcessStartInfo 
                { 
                    FileName = "/bin/sh", 
                    Arguments = "-c \"command -v smartctl\"", 
                    RedirectStandardOutput = true, 
                    UseShellExecute = false, 
                    CreateNoWindow = true 
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        var path = output.Trim();
                        if (File.Exists(path))
                        {
                            s_cachedSmartctlPath = path;
                            return path;
                        }
                    }
                }
            }
            catch { }
        }
        
        return null;
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
        OnPropertyChanged(nameof(SelfTestTypeText));
        OnPropertyChanged(nameof(NvMePercentageUsed));
        OnPropertyChanged(nameof(NvMeMediaErrors));
        OnPropertyChanged(nameof(NvMeUnsafeShutdowns));
    }

    [RelayCommand]
    private async Task StartPollingSelfTestProgress()
    {
        if (SelectedDisk?.Drive == null) return;
        
        Console.WriteLine($"[SMART] Starting polling for: {SelectedDisk.Drive.Path}");
        
        _wasTestInProgress = false; // Reset at start
        _selfTestPollingCts?.Cancel();
        _selfTestPollingCts = new CancellationTokenSource();
        var token = _selfTestPollingCts.Token;
        
        // Set UI state immediately on UI thread
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsSelfTestRunning = true;
            SelfTestProgress = 0;
            SelfTestProgressText = "Self-test spuštěn...";
            StatusMessage = "⏳ Self-test spuštěn, kontroluji stav...";
            Console.WriteLine($"[SMART] UI state set: IsSelfTestRunning={IsSelfTestRunning}");
        });
        
        // Give the drive a moment to start the test before first check
        await Task.Delay(2000, token);
        
        // Poll every 2 seconds
        var pollInterval = TimeSpan.FromSeconds(2);
        var maxPollTime = TimeSpan.FromMinutes(30);
        var startTime = DateTime.Now;
        var consecutiveUnknowns = 0;
        const int maxUnknownCount = 5; // Allow up to 5 unknown status before finishing
        
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Check timeout
                if (DateTime.Now - startTime > maxPollTime)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = "⏰ Timeout - self-test trvá příliš dlouho";
                    });
                    break;
                }
                
                if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
                {
                    try
                    {
                        Console.WriteLine($"[SMART] Polling: Getting status for {SelectedDisk.Drive.Path}");
                        var status = await advancedProvider.GetSelfTestStatusAsync(SelectedDisk.Drive.Path);
                        Console.WriteLine($"[SMART] Polling: Status = {status}");
                        
                        if (status == SmartaSelfTestStatus.InProgress)
                        {
                            consecutiveUnknowns = 0; // Reset counter since we have a valid status
                            _wasTestInProgress = true; // Mark that we've seen a test running
                            Console.WriteLine("[SMART] Test is in progress, getting raw output");
                            // Test is running - try to get progress
                            await RefreshRawOutput();
                            
                            // Parse progress from RawSmartOutput
                            // Try multiple patterns for different smartctl output formats:
                            // 1. "X% remaining" (ATA format)
                            // 2. "X% completed" or "X% complete" (NVMe format)
                            // 3. "Self-test in progress" without percentage
                            
                            var remainingMatch = System.Text.RegularExpressions.Regex.Match(
                                RawSmartOutput, 
                                @"(\d+)\s*%\s*(?:of test )?remaining",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            var completedMatch = System.Text.RegularExpressions.Regex.Match(
                                RawSmartOutput,
                                @"(?:in progress|running)[^\d]*(\d+)\s*%\s*(?:completed|complete)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            // Also try simpler pattern for progress percentage near "in progress"
                            var simpleProgressMatch = System.Text.RegularExpressions.Regex.Match(
                                RawSmartOutput,
                                @"(\d+)\s*%\s*(?:completed|complete)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (remainingMatch.Success && int.TryParse(remainingMatch.Groups[1].Value, out var remainingPercent))
                                {
                                    // ATA format: "X% remaining"
                                    SelfTestProgress = 100 - remainingPercent;
                                    SelfTestProgressText = $"Self-test probíhá: {SelfTestProgress}% dokončeno";
                                    StatusMessage = $"⏳ Self-test: {SelfTestProgress}% dokončeno ({remainingPercent}% zbývá)";
                                    Console.WriteLine($"[SMART] Parsed progress from 'remaining': {SelfTestProgress}% done");
                                }
                                else if (completedMatch.Success && int.TryParse(completedMatch.Groups[1].Value, out var completedPercent))
                                {
                                    // NVMe format: "in progress (X% completed)"
                                    SelfTestProgress = completedPercent;
                                    SelfTestProgressText = $"Self-test probíhá: {SelfTestProgress}% dokončeno";
                                    StatusMessage = $"⏳ Self-test: {SelfTestProgress}% dokončeno";
                                    Console.WriteLine($"[SMART] Parsed progress from 'completed': {SelfTestProgress}% done");
                                }
                                else if (simpleProgressMatch.Success && int.TryParse(simpleProgressMatch.Groups[1].Value, out var simplePct))
                                {
                                    // Simpler pattern: just "X% completed"
                                    SelfTestProgress = simplePct;
                                    SelfTestProgressText = $"Self-test probíhá: {SelfTestProgress}% dokončeno";
                                    StatusMessage = $"⏳ Self-test: {SelfTestProgress}% dokončeno";
                                    Console.WriteLine($"[SMART] Parsed progress from simple pattern: {SelfTestProgress}% done");
                                }
                                else if (RawSmartOutput.Contains("in progress", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    SelfTestProgressText = "Self-test probíhá...";
                                    StatusMessage = "⏳ Self-test probíhá...";
                                    Console.WriteLine("[SMART] Found 'in progress' but no percentage");
                                }
                            });
                        }
                        else if (status == SmartaSelfTestStatus.CompletedWithoutError)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                IsSelfTestRunning = false;
                                SelfTestProgress = 100;
                                SelfTestProgressText = "Self-test dokončen";
                                StatusMessage = "✅ Self-test dokončen úspěšně";
                            });
                            
                            await RefreshCommand.ExecuteAsync(null);
                            break;
                        }
                        else if (status == SmartaSelfTestStatus.AbortedByUser)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                IsSelfTestRunning = false;
                                SelfTestProgress = 0;
                                SelfTestProgressText = "Self-test přerušen";
                                StatusMessage = "⏹️ Self-test byl přerušen";
                            });
                            break;
                        }
                        else if (status == SmartaSelfTestStatus.NoTest)
                        {
                            // No test running - depending on context:
                            // 1. Right after starting: test hasn't begun yet, continue polling
                            // 2. After test was in progress: test likely completed, check log
                            Console.WriteLine($"[SMART] No test running (wasTestInProgress={_wasTestInProgress})");
                            
                            if (_wasTestInProgress)
                            {
                                // Test was running before, now it's not - likely completed
                                Console.WriteLine("[SMART] Test was in progress, now NoTest - checking log for result");
                                consecutiveUnknowns++;
                                
                                // Check the log to see the actual result
                                var log = await advancedProvider.GetSelfTestLogAsync(SelectedDisk.Drive.Path);
                                var latestTest = log.FirstOrDefault();
                                
                                Console.WriteLine($"[SMART] Log result: {latestTest?.StatusName ?? "no entry"}");
                                
                                // If latest test in log is completed (not in progress), we're done
                                if (latestTest != null && latestTest.Status != SmartaSelfTestStatus.InProgress)
                                {
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        IsSelfTestRunning = false;
                                        SelfTestProgress = 100;
                                        
                                        StatusMessage = latestTest.Status switch
                                        {
                                            SmartaSelfTestStatus.CompletedWithoutError => "✅ Self-test dokončen úspěšně bez chyb.",
                                            SmartaSelfTestStatus.AbortedByUser => "⏹️ Self-test byl přerušen uživatelem.",
                                            SmartaSelfTestStatus.AbortedByHost => "⏹️ Self-test přerušen systémem.",
                                            SmartaSelfTestStatus.FatalError => "❌ Self-test skončil s chybou!",
                                            SmartaSelfTestStatus.ErrorRead => "📖 Self-test skončil s chybou čtení.",
                                            SmartaSelfTestStatus.ErrorElectrical => "⚡ Self-test skončil s elektrickou chybou.",
                                            SmartaSelfTestStatus.ErrorServo => "🔧 Self-test skončil s mechanickou chybou.",
                                            _ => $"✅ Self-test dokončen: {latestTest.StatusName}"
                                        };
                                        SelfTestProgressText = "Self-test dokončen";
                                    });
                                    
                                    await RefreshCommand.ExecuteAsync(null);
                                    break;
                                }
                                // Otherwise continue polling (test might be finishing)
                            }
                            else
                            {
                                // Test not started yet - continue polling
                                consecutiveUnknowns++;
                            }
                        }
                        else if (status == SmartaSelfTestStatus.Unknown)
                        {
                            // Unknown status - could be test not started yet, parsing error, or other issue
                            consecutiveUnknowns++;
                            Console.WriteLine($"[SMART] Unknown status ({consecutiveUnknowns}/{maxUnknownCount}), checking log...");
                            
                            // If we've had too many unknowns, check the log and finish
                            if (consecutiveUnknowns >= maxUnknownCount)
                            {
                                var log = await advancedProvider.GetSelfTestLogAsync(SelectedDisk.Drive.Path);
                                var latestTest = log.FirstOrDefault();
                                
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    IsSelfTestRunning = false;
                                    SelfTestProgress = 100;
                                    
                                    if (latestTest != null)
                                    {
                                        StatusMessage = latestTest.Status switch
                                        {
                                            SmartaSelfTestStatus.CompletedWithoutError => "✅ Self-test dokončen úspěšně bez chyb.",
                                            SmartaSelfTestStatus.AbortedByUser => "⏹️ Self-test byl přerušen uživatelem.",
                                            SmartaSelfTestStatus.FatalError => "❌ Self-test skončil s fatální chybou!",
                                            SmartaSelfTestStatus.ErrorRead => "📖 Self-test skončil s chybou čtení.",
                                            SmartaSelfTestStatus.ErrorElectrical => "⚡ Self-test skončil s elektrickou chybou.",
                                            SmartaSelfTestStatus.InProgress => "⏳ Self-test stále probíhá...",
                                            SmartaSelfTestStatus.AbortedByHost => "⏹️ Self-test přerušen systémem.",
                                            _ => $"❓ Self-test dokončen: {latestTest.StatusName}"
                                        };
                                    }
                                    else
                                    {
                                        StatusMessage = "✅ Self-test dokončen (výsledek nedostupný)";
                                    }
                                });
                                
                                await RefreshCommand.ExecuteAsync(null);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SMART] Polling error: {ex.Message}");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusMessage = $"⚠️ Chyba při kontrole stavu: {ex.Message}";
                        });
                    }
                }
                
                // Wait before next poll
                await Task.Delay(pollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "⏹️ Sledování testu přerušeno";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"❌ Chyba: {ex.Message}";
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsSelfTestRunning = false;
            });
        }
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
    public void Dispose()
    {
        _selfTestPollingCts?.Cancel();
        _selfTestPollingCts?.Dispose();
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