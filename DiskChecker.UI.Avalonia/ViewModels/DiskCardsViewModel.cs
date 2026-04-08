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

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for displaying and managing disk cards (medical records for disks).
/// </summary>
public partial class DiskCardsViewModel : ViewModelBase, INavigableViewModel
{
    private readonly IDiskCardRepository _diskCardRepository;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly IDiskCacheService _diskCacheService;
    
    private ObservableCollection<DiskCard> _diskCards = new();
    private ObservableCollection<DiskCard> _filteredCards = new();
    private DiskCard? _selectedCard;
    private string _searchText = "";
    private string _selectedGradeFilter = "Všechny";
    private string _selectedStatusFilter = "Všechny";
    private bool _showLockedOnly;
    private bool _showArchivedOnly;
    private bool _isLoading;
    private string _statusMessage = "Načítám karty disků...";
    private bool _isOpeningDetails;

    public DiskCardsViewModel(
        IDiskCardRepository diskCardRepository,
        INavigationService navigationService,
        IDialogService dialogService,
        ISelectedDiskService selectedDiskService,
        IDiskCacheService diskCacheService)
    {
        _diskCardRepository = diskCardRepository;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _selectedDiskService = selectedDiskService;
        _diskCacheService = diskCacheService;
        
        NavigateToReportsCommand = new RelayCommand(NavigateToReports);
        NavigateBackCommand = new RelayCommand(NavigateBack);
        RefreshCommand = new AsyncRelayCommand(() => LoadDiskCardsAsync(includeMaintenance: true));
        
        GradeFilters = new ObservableCollection<string> { "Všechny", "A", "B", "C", "D", "E", "F" };
        StatusFilters = new ObservableCollection<string> { "Všechny", "Aktivní", "Archivované" };
    }

    #region Properties

    public ObservableCollection<DiskCard> DiskCards
    {
        get => _diskCards;
        set => SetProperty(ref _diskCards, value);
    }

    public ObservableCollection<DiskCard> FilteredCards
    {
        get => _filteredCards;
        set => SetProperty(ref _filteredCards, value);
    }

    public DiskCard? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (SetProperty(ref _selectedCard, value))
            {
                OnPropertyChanged(nameof(HasSelectedCard));
                OnPropertyChanged(nameof(CanArchive));
                OnPropertyChanged(nameof(CanRestore));
                OnPropertyChanged(nameof(CanGenerateCertificate));
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedGradeFilter
    {
        get => _selectedGradeFilter;
        set
        {
            if (SetProperty(ref _selectedGradeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool ShowLockedOnly
    {
        get => _showLockedOnly;
        set
        {
            if (SetProperty(ref _showLockedOnly, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool ShowArchivedOnly
    {
        get => _showArchivedOnly;
        set
        {
            if (SetProperty(ref _showArchivedOnly, value))
            {
                ApplyFilters();
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

    public ObservableCollection<string> GradeFilters { get; }
    public ObservableCollection<string> StatusFilters { get; }

    public IRelayCommand NavigateToReportsCommand { get; }
    public IRelayCommand NavigateBackCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    
    public bool HasSelectedCard => SelectedCard != null;
    public bool CanArchive => SelectedCard != null && !SelectedCard.IsArchived;
    public bool CanRestore => SelectedCard != null && SelectedCard.IsArchived;
    public bool CanGenerateCertificate => SelectedCard != null && !SelectedCard.IsArchived && SelectedCard.TestCount > 0;
    public int CardCount => DiskCards.Count;
    public int FilteredCount => FilteredCards.Count;

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        _ = LoadDiskCardsAsync(includeMaintenance: false);
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task LoadDiskCardsAsync(bool includeMaintenance = false)
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Načítám karty disků...";

            var merged = 0;
            if (includeMaintenance)
            {
                merged = await _diskCardRepository.MergeDuplicateCardsAsync();
            }

            var cards = await _diskCardRepository.GetAllAsync();
            var drives = await _diskCacheService.GetDrivesAsync(forceRefresh: includeMaintenance);

            DiskCards.Clear();
            
            foreach (var card in cards.OrderByDescending(c => c.LastTestedAt))
            {
                var storedSerial = card.SerialNumber;

                var copy = new DiskCard
                {
                    Id = card.Id,
                    ModelName = card.ModelName,
                    SerialNumber = storedSerial,
                    DevicePath = card.DevicePath,
                    DiskType = card.DiskType,
                    InterfaceType = card.InterfaceType,
                    Capacity = card.Capacity,
                    FirmwareVersion = card.FirmwareVersion,
                    ConnectionType = card.ConnectionType,
                    CreatedAt = card.CreatedAt,
                    LastTestedAt = card.LastTestedAt,
                    OverallGrade = card.OverallGrade,
                    OverallScore = card.OverallScore,
                    TestCount = card.TestCount,
                    IsArchived = card.IsArchived,
                    ArchiveReason = card.ArchiveReason,
                    Notes = card.Notes,
                    PowerOnHours = card.PowerOnHours,
                    PowerCycleCount = card.PowerCycleCount,
                    IsLocked = card.IsLocked,
                    LockReason = card.LockReason
                };

                var matchedDrive = drives.FirstOrDefault(d =>
                    string.Equals(d.Path, copy.DevicePath, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(d.SerialNumber) && string.Equals(
                        DriveIdentityResolver.BuildIdentityKey(d.Path, d.SerialNumber, d.Name ?? "Unknown", d.FirmwareVersion),
                        storedSerial,
                        StringComparison.OrdinalIgnoreCase)));

                copy.SerialNumber = ResolveDisplaySerial(storedSerial, matchedDrive?.SerialNumber);

                if (matchedDrive?.Volumes != null)
                {
                    copy.Volumes = matchedDrive.Volumes
                        .Select(v => new CoreDriveInfo
                        {
                            Id = v.Id,
                            Path = v.Path,
                            Name = v.Name,
                            TotalSize = v.TotalSize,
                            FreeSpace = v.FreeSpace,
                            FileSystem = v.FileSystem,
                            Interface = v.Interface,
                            IsPhysical = v.IsPhysical,
                            IsRemovable = v.IsRemovable,
                            IsReady = v.IsReady,
                            SerialNumber = v.SerialNumber,
                            Model = v.Model,
                            FirmwareVersion = v.FirmwareVersion,
                            VolumeInfo = v.VolumeInfo,
                            IsSystemDisk = v.IsSystemDisk,
                            BusType = v.BusType,
                            MediaType = v.MediaType
                        })
                        .ToList();
                }

                DiskCards.Add(copy);
            }

            ApplyFilters();

            StatusMessage = merged > 0
                ? $"Načteno {DiskCards.Count} karet disků (sloučeno duplicit: {merged})"
                : $"Načteno {DiskCards.Count} karet disků";
            OnPropertyChanged(nameof(CardCount));
            OnPropertyChanged(nameof(FilteredCount));
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se načíst karty: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectCard(DiskCard card)
    {
        // Deselect all
        foreach (var c in DiskCards)
        {
            // Card selection state would be managed in view
        }
        
        SelectedCard = card;
        StatusMessage = $"Vybrán disk: {card.ModelName} ({card.SerialNumber})";
    }

    [RelayCommand]
    private async Task ViewCardDetails(DiskCard card)
    {
        if (card == null || _isOpeningDetails) return;

        try
        {
            _isOpeningDetails = true;

            _selectedDiskService.SelectedDisk = new CoreDriveInfo
            {
                Path = card.DevicePath,
                Name = card.ModelName,
                TotalSize = card.Capacity,
                SerialNumber = card.SerialNumber,
                FirmwareVersion = card.FirmwareVersion
            };
            _selectedDiskService.SelectedDiskDisplayName = card.ModelName;
            _selectedDiskService.IsSelectedDiskLocked = card.IsLocked;

            _navigationService.NavigateTo<DiskCardDetailViewModel>();
            await Task.CompletedTask;
        }
        finally
        {
            _isOpeningDetails = false;
        }
    }

    [RelayCommand]
    private async Task ArchiveCard(DiskCard card)
    {
        if (card == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Archivovat disk",
            $"Opravdu chcete archivovat disk:\n\n{card.ModelName}\nS/N: {card.SerialNumber}\n\n" +
            "Archivovaný disk bude přesunut do archivu a nebude se zobrazovat mezi aktivními disky.");

        if (!confirmed) return;

        var reason = await _dialogService.ShowInputDialogAsync(
            "Důvod archivace",
            "Zadejte důvod archivace:",
            "Vyřazen z provozu");

        var archiveReason = reason switch
        {
            "Vyřazen z provozu" => ArchiveReason.Failed,
            "Prodán" => ArchiveReason.Sold,
            "Darován" => ArchiveReason.Donated,
            "Recyklace" => ArchiveReason.Recycled,
            "Nahrazen" => ArchiveReason.Replaced,
            _ => ArchiveReason.Other
        };

        try
        {
            await _diskCardRepository.ArchiveAsync(card.Id, archiveReason, reason);
            await LoadDiskCardsAsync();
            StatusMessage = "Disk archivován";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se archivovat disk: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RestoreCard(DiskCard card)
    {
        if (card == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Obnovit disk",
            $"Opravdu chcete obnovit disk z archivu?\n\n{card.ModelName}\nS/N: {card.SerialNumber}");

        if (!confirmed) return;

        try
        {
            await _diskCardRepository.RestoreAsync(card.Id);
            await LoadDiskCardsAsync();
            StatusMessage = "Disk obnoven";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se obnovit disk: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteCard(DiskCard card)
    {
        if (card == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "⚠️ Smazat kartu",
            $"Opravdu chcete PERMANENTNĚ smazat kartu disku?\n\n{card.ModelName}\nS/N: {card.SerialNumber}\n\n" +
            "Tato akce je NEVRATNÁ a smaže veškerou historii testů!\n\n" +
            "Zadejte 'SMAZAT' pro potvrzení:");

        if (confirmed)
        {
            try
            {
                await _diskCardRepository.DeleteAsync(card.Id);
                await LoadDiskCardsAsync();
                StatusMessage = "Karta smazána";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se smazat kartu: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task GenerateCertificate(DiskCard card)
    {
        if (card == null || card.TestCount == 0) return;

        var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
        var latestSession = sessions.OrderByDescending(s => s.StartedAt).FirstOrDefault();
        var latestCertificate = await _diskCardRepository.GetLatestCertificateAsync(card.Id);

        _selectedDiskService.SelectedDisk = new CoreDriveInfo
        {
            Path = card.DevicePath,
            Name = card.ModelName,
            TotalSize = card.Capacity,
            SerialNumber = card.SerialNumber,
            FirmwareVersion = card.FirmwareVersion
        };
        _selectedDiskService.SelectedDiskDisplayName = card.ModelName;
        _selectedDiskService.IsSelectedDiskLocked = card.IsLocked;
        _selectedDiskService.SelectedTestSessionId = latestSession?.Id;
        _selectedDiskService.SelectedCertificateId = latestCertificate?.Id;

        _navigationService.NavigateTo<CertificateViewModel>();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void CompareDisks()
    {
        _navigationService.NavigateTo<DiskComparisonViewModel>();
    }

    [RelayCommand]
    private void SelectBestDisk()
    {
        // Find the best disk based on score
        var bestDisk = FilteredCards
            .Where(c => !c.IsArchived && c.TestCount > 0)
            .OrderByDescending(c => c.OverallScore)
            .FirstOrDefault();

        if (bestDisk != null)
        {
            SelectedCard = bestDisk;
            StatusMessage = $"Nejlepší disk: {bestDisk.ModelName} (Známka: {bestDisk.OverallGrade}, Skóre: {bestDisk.OverallScore:F0})";
        }
        else
        {
            StatusMessage = "Žádný disk s testem nenalezen";
        }
    }

    private void NavigateToReports()
    {
        _navigationService.NavigateTo<ReportViewModel>();
    }

    private void NavigateBack()
    {
        _navigationService.NavigateTo<DiskSelectionViewModel>();
    }

    #endregion

    #region Private Methods

    private static string ResolveDisplaySerial(string storedSerial, string? detectedSerial)
    {
        if (!string.IsNullOrWhiteSpace(detectedSerial))
        {
            return detectedSerial.Trim();
        }

        if (string.IsNullOrWhiteSpace(storedSerial) ||
            storedSerial.StartsWith("NOSN-", StringComparison.OrdinalIgnoreCase))
        {
            return "N/A";
        }

        return storedSerial;
    }

    private void ApplyFilters()
    {
        FilteredCards.Clear();

        foreach (var card in DiskCards)
        {
            if (!string.IsNullOrEmpty(SearchText))
            {
                var searchLower = SearchText.ToLowerInvariant();
                if (!card.ModelName.ToLowerInvariant().Contains(searchLower) &&
                    !card.SerialNumber.ToLowerInvariant().Contains(searchLower) &&
                    (card.Notes == null || !card.Notes.ToLowerInvariant().Contains(searchLower)))
                {
                    continue;
                }
            }

            if (SelectedGradeFilter != "Všechny" && card.OverallGrade != SelectedGradeFilter)
            {
                continue;
            }

            if (ShowArchivedOnly && !card.IsArchived)
            {
                continue;
            }

            if (!ShowArchivedOnly && SelectedStatusFilter == "Aktivní" && card.IsArchived)
            {
                continue;
            }
            
            if (!ShowArchivedOnly && SelectedStatusFilter == "Archivované" && !card.IsArchived)
            {
                continue;
            }

            if (ShowLockedOnly && !card.IsLocked)
            {
                continue;
            }

            FilteredCards.Add(card);
        }

        var archivedCount = DiskCards.Count(c => c.IsArchived);
        var activeCount = DiskCards.Count - archivedCount;
        
        if (FilteredCards.Count == 0 && DiskCards.Count > 0)
        {
            StatusMessage = $"Nalezeno {DiskCards.Count} karet disků (aktivní: {activeCount}, archivované: {archivedCount}), ale žádná neodpovídá filtru";
        }
        else if (FilteredCards.Count < DiskCards.Count)
        {
            StatusMessage = $"Zobrazeno {FilteredCards.Count} z {DiskCards.Count} disků (aktivní: {activeCount}, archivované: {archivedCount})";
        }
        else
        {
            StatusMessage = $"Celkem {DiskCards.Count} karet disků (aktivní: {activeCount}, archivované: {archivedCount})";
        }
        
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(CardCount));
    }

    #endregion
}