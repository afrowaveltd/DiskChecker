using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for displaying detailed disk card information.
/// </summary>
public partial class DiskCardDetailViewModel : ViewModelBase, INavigableViewModel
{
    private readonly IDiskCardRepository _diskCardRepository;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IDiskComparisonService _comparisonService;
    private readonly ICertificateGenerator _certificateGenerator;
    private readonly ISelectedDiskService _selectedDiskService;
    
    private DiskCard? _currentCard;
    private TestSession? _selectedSession;
    private DiskCertificate? _latestCertificate;
    private bool _isLoading;
    private string _statusMessage = "Detail karty disku";

    private string _notes = string.Empty;

    public DiskCardDetailViewModel(
        IDiskCardRepository diskCardRepository,
        INavigationService navigationService,
        IDialogService dialogService,
        IDiskComparisonService comparisonService,
        ICertificateGenerator certificateGenerator,
        ISelectedDiskService selectedDiskService)
    {
        _diskCardRepository = diskCardRepository;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _comparisonService = comparisonService;
        _certificateGenerator = certificateGenerator;
        _selectedDiskService = selectedDiskService;
        
        TestSessions = new ObservableCollection<TestSession>();
        SmartHistory = new ObservableCollection<SmartHistoryItem>();
        SpeedChartModel = new PlotModel { Title = "Rychlost testu" };
        TemperatureChartModel = new PlotModel { Title = "Teplota během testu" };
    }

    #region Properties

    public DiskCard? CurrentCard
    {
        get => _currentCard;
        private set
        {
            if (SetProperty(ref _currentCard, value))
            {
                OnPropertyChanged(nameof(HasCard));
                OnPropertyChanged(nameof(DiskModel));
                OnPropertyChanged(nameof(DiskSerial));
                OnPropertyChanged(nameof(DiskCapacity));
                OnPropertyChanged(nameof(DiskType));
                OnPropertyChanged(nameof(DiskInterface));
                OnPropertyChanged(nameof(DiskFirmware));
                OnPropertyChanged(nameof(OverallGrade));
                OnPropertyChanged(nameof(OverallScore));
                OnPropertyChanged(nameof(TestCount));
                OnPropertyChanged(nameof(LastTested));
                OnPropertyChanged(nameof(IsLocked));
                OnPropertyChanged(nameof(IsArchived));
            }
        }
    }

    public TestSession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                OnPropertyChanged(nameof(HasSelectedSession));
                UpdateCharts();
            }
        }
    }

    public DiskCertificate? LatestCertificate
    {
        get => _latestCertificate;
        private set => SetProperty(ref _latestCertificate, value);
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

    public ObservableCollection<TestSession> TestSessions { get; }

    public ObservableCollection<SmartHistoryItem> SmartHistory { get; }

    public PlotModel SpeedChartModel { get; }
    public PlotModel TemperatureChartModel { get; }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    // Compatibility aliases for XAML bindings
    public DiskCard? Card => CurrentCard;
    public bool HasTestSessions => TestSessions.Count > 0;
    public IRelayCommand NavigateBackCommand => GoBackCommand;
    public IAsyncRelayCommand SaveNotesCommand => AddNoteCommand;

    public string GradeColor => (CurrentCard?.OverallGrade ?? "?") switch
    {
        "A" => "#27AE60",
        "B" => "#2ECC71",
        "C" => "#F39C12",
        "D" => "#E67E22",
        "E" => "#E74C3C",
        "F" => "#C0392B",
        _ => "#95A5A6"
    };

    public double AverageScore => TestSessions.Count == 0 ? 0 : TestSessions.Average(s => s.Score);

    public string BestGrade => TestSessions.Count == 0
        ? "-"
        : TestSessions.OrderByDescending(s => s.Score).First().Grade;

    public string WorstGrade => TestSessions.Count == 0
        ? "-"
        : TestSessions.OrderBy(s => s.Score).First().Grade;

    public long TotalSectorsTested => TestSessions.Sum(s => s.SectorsTested);

    public int TotalErrors => TestSessions.Sum(s => s.ErrorCount);

    // Computed properties
    public bool HasCard => CurrentCard != null;
    public bool HasSelectedSession => SelectedSession != null;

    public string DiskModel => CurrentCard?.ModelName ?? "Neznámý";
    public string DiskSerial => CurrentCard?.SerialNumber ?? "-";
    public string DiskCapacity => FormatCapacity(CurrentCard?.Capacity ?? 0);
    public string DiskType => CurrentCard?.DiskType ?? "Neznámý";
    public string DiskInterface => CurrentCard?.InterfaceType ?? "-";
    public string DiskFirmware => CurrentCard?.FirmwareVersion ?? "-";
    public string OverallGrade => CurrentCard?.OverallGrade ?? "?";
    public string OverallScore => CurrentCard != null ? $"{CurrentCard.OverallScore:F1}" : "-";
    public string TestCount => CurrentCard?.TestCount.ToString() ?? "0";
    public string LastTested => CurrentCard?.LastTestedAt.ToString("dd.MM.yyyy HH:mm") ?? "-";
    public bool IsLocked => CurrentCard?.IsLocked ?? false;
    public bool IsArchived => CurrentCard?.IsArchived ?? false;

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        // Load card based on selected disk service
        if (_selectedDiskService.SelectedDisk != null)
        {
            // Try to find existing card or create new one for the disk
            _ = LoadCardForDiskAsync(_selectedDiskService.SelectedDisk);
        }
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (CurrentCard != null)
        {
            await LoadCardAsync(CurrentCard.Id);
        }
    }

    [RelayCommand]
    private void ViewSession(TestSession session)
    {
        if (session == null) return;
        SelectedSession = session;
        StatusMessage = $"Test: {session.TestType} - {session.StartedAt:dd.MM.yyyy HH:mm}";
    }

    [RelayCommand]
    private async Task GenerateCertificateAsync()
    {
        if (CurrentCard == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Generuji certifikát...";

            var sessions = await _diskCardRepository.GetTestSessionsAsync(CurrentCard.Id);
            var latestSession = sessions.FirstOrDefault();

            if (latestSession == null)
            {
                await _dialogService.ShowErrorAsync("Chyba", "Neexistuje žádný test pro tento disk.");
                return;
            }

            var certificate = await _certificateGenerator.GenerateCertificateAsync(latestSession, CurrentCard);
            var pdfPath = await _certificateGenerator.GeneratePdfAsync(certificate);

            // Save certificate
            await _diskCardRepository.CreateCertificateAsync(certificate);
            LatestCertificate = certificate;

            StatusMessage = $"Certifikát vygenerován: {certificate.CertificateNumber}";
            
            var openPdf = await _dialogService.ShowConfirmationAsync(
                "Certifikát",
                $"Certifikát vytvořen:\n{certificate.CertificateNumber}\n\nZnámka: {certificate.Grade}\nSkóre: {certificate.Score:F0}/100\n\n" +
                $"PDF uloženo:\n{pdfPath}\n\nOtevřít PDF?");

            if (openPdf)
            {
                // Open PDF with default application
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vygenerovat certifikát: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CompareWithOtherAsync()
    {
        if (CurrentCard == null) return;
        _navigationService.NavigateTo<DiskComparisonViewModel>();
    }

    [RelayCommand]
    private async Task AddNoteAsync()
    {
        if (CurrentCard == null) return;

        var note = await _dialogService.ShowInputDialogAsync("Poznámka", "Zadejte poznámku:", CurrentCard.Notes ?? "");
        if (note == null)
        {
            return;
        }

        CurrentCard.Notes = note;
        Notes = note;
        await _diskCardRepository.UpdateAsync(CurrentCard);
        StatusMessage = "Poznámka uložena";
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<DiskCardsViewModel>();
    }

    #endregion

    #region Private Methods

    private async Task LoadCardForDiskAsync(CoreDriveInfo disk)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Hledám kartu disku...";

            var identityKey = DriveIdentityResolver.BuildIdentityKey(
                disk.Path,
                disk.SerialNumber,
                disk.Name ?? "Unknown",
                disk.FirmwareVersion);
            var legacyKey = BuildLegacyIdentityKey(
                disk.Path,
                disk.SerialNumber,
                disk.Name ?? "Unknown",
                disk.FirmwareVersion);

            DiskCard? card = await _diskCardRepository.GetBySerialNumberAsync(identityKey)
                           ?? await _diskCardRepository.GetBySerialNumberAsync(legacyKey)
                           ?? await _diskCardRepository.GetByDevicePathAsync(disk.Path);

            if (card == null)
            {
                // Create new card
                card = new DiskCard
                {
                    ModelName = disk.Name ?? "Unknown",
                    SerialNumber = identityKey,
                    DevicePath = disk.Path,
                    Capacity = disk.TotalSize,
                    FirmwareVersion = disk.FirmwareVersion ?? string.Empty,
                    DiskType = "Unknown",
                    InterfaceType = disk.BusType.ToString(),
                    IsLocked = _selectedDiskService.IsSelectedDiskLocked
                };

                card = await _diskCardRepository.CreateAsync(card);
                StatusMessage = "Vytvořena nová karta disku";
            }
            else
            {
                StatusMessage = "Karta disku načtena";
            }

            await LoadCardAsync(card.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst kartu: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCardAsync(int cardId)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Načítám detail...";

            CurrentCard = await _diskCardRepository.GetByIdAsync(cardId);

            if (CurrentCard != null)
            {
                // Load test sessions
                var sessions = await _diskCardRepository.GetTestSessionsAsync(cardId);
                TestSessions.Clear();
                SmartHistory.Clear();

                foreach (var session in sessions.OrderByDescending(s => s.StartedAt))
                {
                    TestSessions.Add(session);

                    SmartHistory.Add(new SmartHistoryItem
                    {
                        TestedAt = session.StartedAt,
                        Temperature = session.Temperature,
                        PowerOnHours = 0,
                        ReallocatedSectors = 0,
                        PendingSectors = 0,
                        ReadErrors = session.ReadErrors,
                        ReallocEvents = session.VerificationErrors,
                        Notes = session.Notes ?? string.Empty
                    });
                }

                Notes = CurrentCard.Notes ?? string.Empty;

                // Load latest certificate
                LatestCertificate = await _diskCardRepository.GetLatestCertificateAsync(cardId);

                OnPropertyChanged(nameof(Card));
                OnPropertyChanged(nameof(HasTestSessions));
                OnPropertyChanged(nameof(GradeColor));
                OnPropertyChanged(nameof(AverageScore));
                OnPropertyChanged(nameof(BestGrade));
                OnPropertyChanged(nameof(WorstGrade));
                OnPropertyChanged(nameof(TotalSectorsTested));
                OnPropertyChanged(nameof(TotalErrors));

                if (SelectedSession == null)
                {
                    SelectedSession = TestSessions.FirstOrDefault();
                }

                StatusMessage = $"Karta načtena: {CurrentCard.ModelName}";
            }
            else
            {
                StatusMessage = "Karta nenalezena";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string BuildLegacyIdentityKey(string drivePath, string? serialNumber, string deviceModel, string? firmwareVersion)
    {
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"{serialNumber}_{deviceModel}_{firmwareVersion ?? "unknown"}";
        }

        if (!string.IsNullOrWhiteSpace(deviceModel))
        {
            return $"{deviceModel}_{drivePath.Replace("\\", "_").Replace("/", "_")}_{firmwareVersion ?? "unknown"}";
        }

        return drivePath;
    }

    private void UpdateCharts()
    {
        if (SelectedSession == null) return;

        // Update speed chart
        SpeedChartModel.Series.Clear();
        SpeedChartModel.Axes.Clear();

        var writeSeries = new LineSeries { Title = "Zápis (MB/s)", Color = OxyColors.Blue };
        var readSeries = new LineSeries { Title = "Čtení (MB/s)", Color = OxyColors.Green };

        // Use DataPoint correctly with (double x, double y)
        int index = 0;
        foreach (var sample in SelectedSession.WriteSamples)
        {
            writeSeries.Points.Add(new OxyPlot.DataPoint((double)index, sample.SpeedMBps));
            index++;
        }

        index = 0;
        foreach (var sample in SelectedSession.ReadSamples)
        {
            readSeries.Points.Add(new OxyPlot.DataPoint((double)index, sample.SpeedMBps));
            index++;
        }

        SpeedChartModel.Series.Add(writeSeries);
        SpeedChartModel.Series.Add(readSeries);
        SpeedChartModel.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Čas (vzorky)" });
        SpeedChartModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Rychlost (MB/s)" });
        SpeedChartModel.InvalidatePlot(true);

        // Update temperature chart
        TemperatureChartModel.Series.Clear();
        TemperatureChartModel.Axes.Clear();

        var tempSeries = new LineSeries { Title = "Teplota (°C)", Color = OxyColors.Red };

        index = 0;
        foreach (var sample in SelectedSession.TemperatureSamples)
        {
            tempSeries.Points.Add(new OxyPlot.DataPoint((double)index, sample.TemperatureCelsius));
            index++;
        }

        TemperatureChartModel.Series.Add(tempSeries);
        TemperatureChartModel.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Čas (vzorky)" });
        TemperatureChartModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Teplota (°C)" });
        TemperatureChartModel.InvalidatePlot(true);
    }

    private static string FormatCapacity(long bytes)
    {
        if (bytes <= 0) return "Neznámý";
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1000) return $"{gb / 1024.0:F2} TB";
        return $"{gb:F0} GB";
    }

    #endregion
}