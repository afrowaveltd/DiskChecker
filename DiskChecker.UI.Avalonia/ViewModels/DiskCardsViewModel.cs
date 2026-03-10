using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    
    private ObservableCollection<DiskCard> _diskCards = new();
    private ObservableCollection<DiskCard> _filteredCards = new();
    private DiskCard? _selectedCard;
    private string _searchText = "";
    private string _selectedGradeFilter = "Všechny";
    private string _selectedStatusFilter = "Aktivní";
    private bool _isLoading;
    private string _statusMessage = "Načítám karty disků...";

    public DiskCardsViewModel(
        IDiskCardRepository diskCardRepository,
        INavigationService navigationService,
        IDialogService dialogService,
        ISelectedDiskService selectedDiskService)
    {
        _diskCardRepository = diskCardRepository;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _selectedDiskService = selectedDiskService;
        
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

    public bool HasSelectedCard => SelectedCard != null;
    public bool CanArchive => SelectedCard != null && !SelectedCard.IsArchived;
    public bool CanRestore => SelectedCard != null && SelectedCard.IsArchived;
    public bool CanGenerateCertificate => SelectedCard != null && !SelectedCard.IsArchived && SelectedCard.TestCount > 0;

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        _ = LoadDiskCardsAsync();
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task LoadDiskCardsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Načítám karty disků...";

            var cards = await _diskCardRepository.GetAllAsync();
            DiskCards.Clear();
            
            foreach (var card in cards.OrderByDescending(c => c.LastTestedAt))
            {
                DiskCards.Add(card);
            }

            ApplyFilters();

            StatusMessage = $"Načteno {DiskCards.Count} karet disků";
        }
        catch (Exception ex)
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
        if (card == null) return;
        
        // Store selected card and navigate to detail
        _selectedDiskService.SelectedDisk = new CoreDriveInfo { Path = card.DevicePath };
        _navigationService.NavigateTo<DiskCardDetailViewModel>();
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

        // Get latest test session
        var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
        var latestSession = sessions.FirstOrDefault();

        if (latestSession == null)
        {
            await _dialogService.ShowErrorAsync("Chyba", "Nebyl nalezen žádný test pro tento disk.");
            return;
        }

        // Navigate to certificate view
        _navigationService.NavigateTo<CertificateViewModel>();
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

    #endregion

    #region Private Methods

    private void ApplyFilters()
    {
        FilteredCards.Clear();

        foreach (var card in DiskCards)
        {
            // Apply search filter
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

            // Apply grade filter
            if (SelectedGradeFilter != "Všechny" && card.OverallGrade != SelectedGradeFilter)
            {
                continue;
            }

            // Apply status filter
            if (SelectedStatusFilter == "Aktivní" && card.IsArchived)
            {
                continue;
            }
            
            if (SelectedStatusFilter == "Archivované" && !card.IsArchived)
            {
                continue;
            }

            FilteredCards.Add(card);
        }

        StatusMessage = $"Zobrazeno {FilteredCards.Count} z {DiskCards.Count} disků";
    }

    #endregion
}