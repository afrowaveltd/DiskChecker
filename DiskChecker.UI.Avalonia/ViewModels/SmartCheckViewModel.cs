using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public partial class SmartCheckViewModel : ViewModelBase, INavigableViewModel
    {
        private readonly ISmartaProvider _smartaProvider;
        private readonly IDiskDetectionService _diskDetectionService;
        private readonly IQualityCalculator _qualityCalculator;
        private readonly IDialogService _dialogService;
        
        private ObservableCollection<DiskStatusCardItem> _disks = new();
        private DiskStatusCardItem? _selectedDisk;
        private bool _isChecking;
        private string _statusMessage = string.Empty;
        private SmartaData? _currentSmartData;
        private QualityRating? _currentQuality;
        private ObservableCollection<SmartaAttributeItem> _smartAttributes = new();
        private ObservableCollection<SmartaSelfTestEntry> _selfTestLog = new();

        public SmartCheckViewModel(
            ISmartaProvider smartaProvider, 
            IDiskDetectionService diskDetectionService,
            IQualityCalculator qualityCalculator, 
            IDialogService dialogService)
        {
            _smartaProvider = smartaProvider ?? throw new ArgumentNullException(nameof(smartaProvider));
            _diskDetectionService = diskDetectionService ?? throw new ArgumentNullException(nameof(diskDetectionService));
            _qualityCalculator = qualityCalculator ?? throw new ArgumentNullException(nameof(qualityCalculator));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            
            LoadDisksCommand = new AsyncRelayCommand(LoadDisksAsync);
            CheckSmartCommand = new AsyncRelayCommand(CheckSmartAsync, () => SelectedDisk != null && !IsChecking);
            RunSelfTestCommand = new AsyncRelayCommand(RunSelfTestAsync, () => SelectedDisk != null && !IsChecking);
            RefreshCommand = new AsyncRelayCommand(CheckSmartAsync, () => SelectedDisk != null && !IsChecking);
            
            _ = LoadDisksAsync();
        }

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
                    CheckSmartCommand.NotifyCanExecuteChanged();
                    RunSelfTestCommand.NotifyCanExecuteChanged();
                    RefreshCommand.NotifyCanExecuteChanged();
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
                    CheckSmartCommand.NotifyCanExecuteChanged();
                    RunSelfTestCommand.NotifyCanExecuteChanged();
                    RefreshCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public SmartaData? CurrentSmartData
        {
            get => _currentSmartData;
            set => SetProperty(ref _currentSmartData, value);
        }

        public QualityRating? CurrentQuality
        {
            get => _currentQuality;
            set => SetProperty(ref _currentQuality, value);
        }

        public ObservableCollection<SmartaAttributeItem> SmartAttributes
        {
            get => _smartAttributes;
            set => SetProperty(ref _smartAttributes, value);
        }

        public ObservableCollection<SmartaSelfTestEntry> SelfTestLog
        {
            get => _selfTestLog;
            set => SetProperty(ref _selfTestLog, value);
        }

        // Computed properties for XAML binding
        public string DiskName => SelectedDisk?.DisplayName ?? "Nevybrán žádný disk";
        public string DiskPath => SelectedDisk?.DisplayPath ?? "";
        public string DeviceModel => CurrentSmartData?.DeviceModel ?? "-";
        public string SerialNumber => CurrentSmartData?.SerialNumber ?? "-";
        public string FirmwareVersion => CurrentSmartData?.FirmwareVersion ?? "-";
        public string Temperature => CurrentSmartData?.Temperature > 0 ? $"{CurrentSmartData.Temperature}°C" : "-";
        public string PowerOnHours => CurrentSmartData?.PowerOnHours?.ToString() ?? "-";
        public string Grade => CurrentQuality?.Grade.ToString() ?? "-";
        public string Score => CurrentQuality?.Score.ToString("F0") ?? "-";

        public IAsyncRelayCommand LoadDisksCommand { get; }
        public IAsyncRelayCommand CheckSmartCommand { get; }
        public IAsyncRelayCommand RunSelfTestCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }

        public void OnNavigatedTo()
        {
            _ = LoadDisksAsync();
        }

        private async Task LoadDisksAsync()
        {
            try
            {
                StatusMessage = "Načítám seznam disků...";
                IsChecking = true;
                
                var drives = await _diskDetectionService.GetDrivesAsync();
                Disks.Clear();
                
                foreach (var drive in drives)
                {
                    var smartData = await _smartaProvider.GetSmartaDataAsync(drive.Path);
                    var quality = smartData != null 
                        ? _qualityCalculator.CalculateQuality(smartData) 
                        : new QualityRating(QualityGrade.F, 0);
                    
                    var card = new DiskStatusCardItem
                    {
                        Drive = drive,
                        DisplayName = !string.IsNullOrEmpty(smartData?.DeviceModel) 
                            ? smartData.DeviceModel 
                            : drive.Name ?? "Unknown",
                        DisplayPath = drive.Path,
                        CapacityText = FormatCapacity(drive.TotalSize),
                        GradeText = quality.Grade.ToString(),
                        TemperatureText = smartData?.Temperature > 0 ? $"{smartData.Temperature}°C" : "N/A",
                        SmartData = smartData,
                        Quality = quality,
                        IsSystemDisk = drive.Path.Contains('0')
                    };
                    
                    Disks.Add(card);
                }
                
                // Auto-select first disk if available
                if (Disks.Count > 0 && SelectedDisk == null)
                {
                    SelectedDisk = Disks[0];
                    await CheckSmartAsync();
                }
                
                StatusMessage = $"Nalezeno {Disks.Count} disků";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst disky: {ex.Message}");
            }
            finally
            {
                IsChecking = false;
            }
        }

        private async Task CheckSmartAsync()
        {
            if (SelectedDisk == null) return;

            try
            {
                IsChecking = true;
                StatusMessage = $"Kontroluji SMART data...";
                
                var drive = SelectedDisk.Drive;
                if (drive == null)
                {
                    StatusMessage = "Disk není vybrán";
                    return;
                }
                
                var smartData = await _smartaProvider.GetSmartaDataAsync(drive.Path);
                
                if (smartData == null)
                {
                    await _dialogService.ShowErrorAsync("Chyba", "Nepodařilo se načíst SMART data");
                    StatusMessage = "SMART data nejsou dostupná";
                    return;
                }
                
                CurrentSmartData = smartData;
                CurrentQuality = _qualityCalculator.CalculateQuality(smartData);
                
                SelectedDisk.SmartData = smartData;
                SelectedDisk.Quality = CurrentQuality;
                SelectedDisk.TemperatureText = smartData.Temperature > 0 ? $"{smartData.Temperature}°C" : "N/A";
                SelectedDisk.GradeText = CurrentQuality.Grade.ToString();
                
                // Get SMART attributes if available
                if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
                {
                    try
                    {
                        var attributes = await advancedProvider.GetSmartAttributesAsync(drive.Path);
                        SmartAttributes = new ObservableCollection<SmartaAttributeItem>(attributes);
                    }
                    catch { }
                    
                    try
                    {
                        var log = await advancedProvider.GetSelfTestLogAsync(drive.Path);
                        SelfTestLog = new ObservableCollection<SmartaSelfTestEntry>(log);
                    }
                    catch { }
                }
                
                // Notify property changes for computed properties
                OnPropertyChanged(nameof(DiskName));
                OnPropertyChanged(nameof(DiskPath));
                OnPropertyChanged(nameof(DeviceModel));
                OnPropertyChanged(nameof(SerialNumber));
                OnPropertyChanged(nameof(FirmwareVersion));
                OnPropertyChanged(nameof(Temperature));
                OnPropertyChanged(nameof(PowerOnHours));
                OnPropertyChanged(nameof(Grade));
                OnPropertyChanged(nameof(Score));
                
                StatusMessage = "SMART kontrola dokončena";
                
                var healthStatus = CurrentQuality?.Grade >= QualityGrade.B ? "Zdravý" : "Vyžaduje pozornost";
                await _dialogService.ShowMessageAsync("Výsledek SMART kontroly", 
                    $"Disk: {smartData.DeviceModel ?? "Unknown"}\n" +
                    $"Serial: {smartData.SerialNumber ?? "N/A"}\n" +
                    $"Teplota: {smartData.Temperature}°C\n" +
                    $"Hodin v provozu: {smartData.PowerOnHours}\n" +
                    $"Přemapované sektory: {smartData.ReallocatedSectorCount}\n" +
                    $"Čekající sektory: {smartData.PendingSectorCount}\n" +
                    $"Hodnocení: {CurrentQuality?.Grade} ({CurrentQuality?.Score:F0}%)\n" +
                    $"Stav: {healthStatus}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se provést SMART kontrolu: {ex.Message}");
            }
            finally
            {
                IsChecking = false;
            }
        }

        private async Task RunSelfTestAsync()
        {
            if (SelectedDisk?.Drive == null) return;

            try
            {
                IsChecking = true;
                StatusMessage = "Spouštím SMART self-test...";
                
                if (_smartaProvider is IAdvancedSmartaProvider advancedProvider)
                {
                    var success = await advancedProvider.StartSelfTestAsync(SelectedDisk.Drive.Path, SmartaSelfTestType.Extended);
                    if (success)
                    {
                        StatusMessage = "Self-test byl spuštěn. Výsledek bude dostupný po dokončení testu.";
                        await _dialogService.ShowMessageAsync("Self-Test", "Self-test byl úspěšně spuštěn.\n\nDoba trvání: cca 60-120 minut\n\nVýsledek zkontrolujte později.");
                    }
                    else
                    {
                        StatusMessage = "Nepodařilo se spustit self-test";
                        await _dialogService.ShowErrorAsync("Chyba", "Nepodařilo se spustit SMART self-test");
                    }
                }
                else
                {
                    await _dialogService.ShowMessageAsync("Nepodporováno", "Self-test není podporován na tomto systému.");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chyba: {ex.Message}";
                await _dialogService.ShowErrorAsync("Chyba", ex.Message);
            }
            finally
            {
                IsChecking = false;
            }
        }

        private static string FormatCapacity(long bytes)
        {
            if (bytes <= 0) return "Unknown";
            var gb = bytes / (1024.0 * 1024.0 * 1024.0);
            if (gb >= 1000) return $"{gb / 1024.0:F1} TB";
            return $"{gb:F0} GB";
        }
    }
}