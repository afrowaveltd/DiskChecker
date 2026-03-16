using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for displaying and generating disk certificates.
/// </summary>
public partial class CertificateViewModel : ViewModelBase, INavigableViewModel
{
    private readonly ICertificateGenerator _certificateGenerator;
    private readonly IDiskCardRepository _diskCardRepository;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly ISelectedDiskService _selectedDiskService;
    
    private DiskCertificate? _certificate;
    private bool _isLoading;
    private string _statusMessage = "Certifikát disku";
    private byte[]? _previewImage;

    public CertificateViewModel(
        ICertificateGenerator certificateGenerator,
        IDiskCardRepository diskCardRepository,
        INavigationService navigationService,
        IDialogService dialogService,
        ISelectedDiskService selectedDiskService)
    {
        _certificateGenerator = certificateGenerator;
        _diskCardRepository = diskCardRepository;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _selectedDiskService = selectedDiskService;
    }

    #region Properties

    public DiskCertificate? Certificate
    {
        get => _certificate;
        private set
        {
            if (SetProperty(ref _certificate, value))
            {
                OnPropertyChanged(nameof(HasCertificate));
                OnPropertyChanged(nameof(CurrentDateTime));
                OnPropertyChanged(nameof(CertificateNumber));
                OnPropertyChanged(nameof(DiskModel));
                OnPropertyChanged(nameof(SerialNumber));
                OnPropertyChanged(nameof(Capacity));
                OnPropertyChanged(nameof(DiskType));
                OnPropertyChanged(nameof(Grade));
                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(HealthStatus));
                OnPropertyChanged(nameof(TestType));
                OnPropertyChanged(nameof(TestDuration));
                OnPropertyChanged(nameof(AvgWriteSpeed));
                OnPropertyChanged(nameof(AvgReadSpeed));
                OnPropertyChanged(nameof(TemperatureRange));
                OnPropertyChanged(nameof(Errors));
                OnPropertyChanged(nameof(SmartPassed));
                OnPropertyChanged(nameof(Recommended));
                OnPropertyChanged(nameof(RecommendationText));
                OnPropertyChanged(nameof(ScoringReasonsText));
                OnPropertyChanged(nameof(HasScoringReasons));
            }
        }
    }

    public byte[]? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
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

    public bool HasCertificate => Certificate != null;

    public DateTime CurrentDateTime => DateTime.Now;


    // Certificate display properties
    public string CertificateNumber => Certificate?.CertificateNumber ?? "-";
    public string DiskModel => Certificate?.DiskModel ?? "Neznámý";
    public string SerialNumber => Certificate?.SerialNumber ?? "-";
    public string Capacity => Certificate?.Capacity ?? "-";
    public string DiskType => Certificate?.DiskType ?? "-";
    public string Grade => Certificate?.Grade ?? "?";
    public string Score => Certificate != null ? $"{Certificate.Score:F0}/100" : "-";
    public string HealthStatus => Certificate?.HealthStatus ?? "-";
    public string TestType => Certificate?.TestType ?? "-";
    public string TestDuration => Certificate != null ? Certificate.TestDuration.ToString(@"hh\:mm\:ss") : "-";
    public string AvgWriteSpeed => Certificate != null ? $"{Certificate.AvgWriteSpeed:F1} MB/s" : "-";
    public string AvgReadSpeed => Certificate != null ? $"{Certificate.AvgReadSpeed:F1} MB/s" : "-";
    public string TemperatureRange => Certificate?.TemperatureRange ?? "-";
    public string Errors => Certificate?.ErrorCount.ToString() ?? "0";
    public bool SmartPassed => Certificate?.SmartPassed ?? false;
    public bool Recommended => Certificate?.Recommended ?? false;
    public string RecommendationText => Certificate?.RecommendationNotes ?? "Není k dispozici";
    public string ScoringReasonsText => string.IsNullOrWhiteSpace(Certificate?.Notes)
        ? "Bez významných varování."
        : Certificate.Notes;
    public bool HasScoringReasons => !string.IsNullOrWhiteSpace(Certificate?.Notes);

    // Grade color
    public string GradeColor => Certificate?.Grade switch
    {
        "A" => "#27AE60",
        "B" => "#2ECC71",
        "C" => "#F1C40F",
        "D" => "#E67E22",
        "E" => "#E74C3C",
        "F" => "#C0392B",
        _ => "#95A5A6"
    };

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        // Load latest certificate for selected disk
        _ = LoadCertificateAsync();
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task GenerateNewCertificateAsync()
    {
        if (_selectedDiskService.SelectedDisk == null)
        {
            StatusMessage = "Není vybrán žádný disk";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Generuji certifikát...";

            // Find disk card
            var card = await _diskCardRepository.GetByDevicePathAsync(_selectedDiskService.SelectedDisk.Path);
            
            if (card == null)
            {
                StatusMessage = "Karta disku nenalezena. Proveďte nejprve test.";
                await _dialogService.ShowErrorAsync("Chyba", "Karta disku nenalezena. Proveďte nejprve test disku.");
                return;
            }

            // Get latest test session
            var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
            var latestSession = sessions.FirstOrDefault();

            if (latestSession == null)
            {
                StatusMessage = "Žádný test nenalezen. Proveďte nejprve test.";
                await _dialogService.ShowErrorAsync("Chyba", "Pro tento disk nebyl proveden žádný test. Proveďte nejprve test.");
                return;
            }

            // Generate certificate
            Certificate = await _certificateGenerator.GenerateCertificateAsync(latestSession, card);
            
            // Generate preview
            PreviewImage = await _certificateGenerator.GeneratePreviewAsync(Certificate);

            // Save certificate
            await _diskCardRepository.CreateCertificateAsync(Certificate);

            StatusMessage = $"Certifikát vygenerován: {Certificate.CertificateNumber}";
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
    private async Task ExportPdfAsync()
    {
        if (Certificate == null)
        {
            StatusMessage = "Nejprve vygenerujte certifikát";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Exportuji PDF...";

            var pdfPath = await _certificateGenerator.GeneratePdfAsync(Certificate);

            StatusMessage = $"PDF uloženo: {pdfPath}";

            var openPdf = await _dialogService.ShowConfirmationAsync(
                "PDF Export",
                $"PDF certifikát uložen:\n{pdfPath}\n\nOtevřít soubor?");

            if (openPdf)
            {
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
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se exportovat PDF: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (Certificate == null)
        {
            StatusMessage = "Nejprve vygenerujte certifikát";
            return;
        }

        try
        {
            // For now, generate PDF and open print dialog
            var pdfPath = await _certificateGenerator.GeneratePdfAsync(Certificate);
            
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = pdfPath,
                Verb = "print",
                UseShellExecute = true
            });

            StatusMessage = "Tisk spuštěn";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba tisku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vytisknout: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PrintLabelAsync()
    {
        if (Certificate == null)
        {
            StatusMessage = "Nejprve vygenerujte certifikát";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Generuji štítek...";

            var labelPath = await _certificateGenerator.GenerateLabelAsync(Certificate);

            Process.Start(new ProcessStartInfo
            {
                FileName = labelPath,
                Verb = "print",
                UseShellExecute = true
            });

            StatusMessage = "Tisk štítku spuštěn";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Chyba tisku štítku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vytisknout štítek: {ex.Message}");
        }
        catch (IOException ex)
        {
            StatusMessage = $"Chyba tisku štítku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vytisknout štítek: {ex.Message}");
        }
        catch (Win32Exception ex)
        {
            StatusMessage = $"Chyba tisku štítku: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se vytisknout štítek: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<DiskCardDetailViewModel>();
    }

    #endregion

    #region Private Methods

    private async Task LoadCertificateAsync()
    {
        if (_selectedDiskService.SelectedDisk == null)
        {
            StatusMessage = "Není vybrán žádný disk";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Načítám certifikát...";

            var card = await _diskCardRepository.GetByDevicePathAsync(_selectedDiskService.SelectedDisk.Path);
            
            if (card != null)
            {
                var latestCert = await _diskCardRepository.GetLatestCertificateAsync(card.Id);
                
                if (latestCert != null)
                {
                    Certificate = latestCert;
                    PreviewImage = await _certificateGenerator.GeneratePreviewAsync(latestCert);
                    StatusMessage = $"Certifikát načten: {latestCert.CertificateNumber}";
                }
                else
                {
                    StatusMessage = "Žádný certifikát nenalezen. Vygenerujte nový.";
                }
            }
            else
            {
                StatusMessage = "Karta disku nenalezena.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion
}