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
    private string _selectedGradeFilter = string.Empty;
    private string _selectedStatusFilter = string.Empty;
    private bool _showLockedOnly;
    private bool _showArchivedOnly;
    private bool _isLoading;
    private string _statusMessage = L.Get("DiskCards.Status.Loading");
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
        
        GradeFilters = new ObservableCollection<string> { L.Get("Common.All"), "A", "B", "C", "D", "E", "F" };
        StatusFilters = new ObservableCollection<string> { L.Get("Common.All"), L.Get("Common.Active"), L.Get("Common.Archived") };
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
            StatusMessage = L.Get("DiskCards.Status.Loading");

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

                // DevicePath is not a stable historical identity: PhysicalDrive0 / /dev/sda can later point to
                // a different disk. Use it only for live volume enrichment, never to overwrite the card's
                // stored serial shown in the grid. Otherwise many old cards appear to have the currently
                // inserted disk's serial number.
                var identityMatchedDrive = drives.FirstOrDefault(d =>
                    !string.IsNullOrWhiteSpace(d.SerialNumber) && string.Equals(
                        DriveIdentityResolver.BuildIdentityKey(d.Path, d.SerialNumber, d.Name ?? "Unknown", d.FirmwareVersion),
                        storedSerial,
                        StringComparison.OrdinalIgnoreCase));

                var pathMatchedDrive = drives.FirstOrDefault(d =>
                    string.Equals(d.Path, copy.DevicePath, StringComparison.OrdinalIgnoreCase));

                var matchedDrive = identityMatchedDrive ?? pathMatchedDrive;
                copy.SerialNumber = ResolveDisplaySerial(storedSerial, identityMatchedDrive?.SerialNumber);

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

            var activeCount = DiskCards.Count(c => !c.IsArchived);
            var archivedCount = DiskCards.Count(c => c.IsArchived);
            StatusMessage = merged > 0
                ? L.Get("DiskCards.Status.Merged", DiskCards.Count.ToString(), activeCount.ToString(), archivedCount.ToString(), merged.ToString())
                : L.Get("DiskCards.Status.Total", DiskCards.Count.ToString(), activeCount.ToString(), archivedCount.ToString());
            OnPropertyChanged(nameof(CardCount));
            OnPropertyChanged(nameof(FilteredCount));
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = L.Get("DiskCards.Status.Error", ex.Message);
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("DiskCard.LoadCardsError", ex.Message));
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
        StatusMessage = L.Get("DiskCards.Status.DiskSelected", card.ModelName, card.SerialNumber);
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
            L.Get("DiskCards.Archive.Title"),
            L.Get("DiskCards.Archive.ConfirmMessage", card.ModelName, card.SerialNumber));

        if (!confirmed) return;

        var reason = await _dialogService.ShowInputDialogAsync(
            L.Get("DiskCards.Archive.ReasonTitle"),
            L.Get("DiskCards.Archive.ReasonPrompt"),
            L.Get("DiskCards.Archive.DefaultReason"));

        var archiveReason = reason switch
        {
            _ when reason == L.Get("DiskCards.Archive.Reason.Failed") => ArchiveReason.Failed,
            _ when reason == L.Get("DiskCards.Archive.Reason.Sold") => ArchiveReason.Sold,
            _ when reason == L.Get("DiskCards.Archive.Reason.Donated") => ArchiveReason.Donated,
            _ when reason == L.Get("DiskCards.Archive.Reason.Recycled") => ArchiveReason.Recycled,
            _ when reason == L.Get("DiskCards.Archive.Reason.Replaced") => ArchiveReason.Replaced,
            _ => ArchiveReason.Other
        };

        try
        {
            await _diskCardRepository.ArchiveAsync(card.Id, archiveReason, reason);
            await LoadDiskCardsAsync();
            StatusMessage = L.Get("DiskCards.Status.Archived");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("DiskCards.Error.ArchiveFailed", ex.Message));
        }
    }

    [RelayCommand]
    private async Task RestoreCard(DiskCard card)
    {
        if (card == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            L.Get("DiskCards.Restore.Title"),
            L.Get("DiskCards.Restore.ConfirmMessage", card.ModelName, card.SerialNumber));

        if (!confirmed) return;

        try
        {
            await _diskCardRepository.RestoreAsync(card.Id);
            await LoadDiskCardsAsync();
            StatusMessage = L.Get("DiskCards.Status.Restored");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("DiskCards.Error.RestoreFailed", ex.Message));
        }
    }

    [RelayCommand]
    private async Task DeleteCard(DiskCard card)
    {
        if (card == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            L.Get("DiskCards.Delete.Title"),
            L.Get("DiskCards.Delete.ConfirmMessage", card.ModelName, card.SerialNumber));

        if (confirmed)
        {
            try
            {
                await _diskCardRepository.DeleteAsync(card.Id);
                await LoadDiskCardsAsync();
                StatusMessage = L.Get("DiskCards.Status.Deleted");
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(L.Get("Common.Error"), L.Get("DiskCards.Error.DeleteFailed", ex.Message));
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
    private void NavigateToCertificateBrowser()
    {
        _navigationService.NavigateTo<CertificateBrowserViewModel>();
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
            StatusMessage = L.Get("DiskCards.Status.BestDisk", bestDisk.ModelName, bestDisk.OverallGrade, bestDisk.OverallScore.ToString("F0"));
        }
        else
        {
            StatusMessage = L.Get("DiskCards.Status.NoTestDisk");
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

    public static string ResolveDisplaySerial(string storedSerial, string? identityMatchedDetectedSerial)
    {
        // Only use a live detected serial when the drive matched by stable identity, not just by
        // reusable OS path. Path-only matches can point to another disk inserted later.
        if (!string.IsNullOrWhiteSpace(identityMatchedDetectedSerial) &&
            DriveIdentityResolver.IsReliableSerialNumber(identityMatchedDetectedSerial))
        {
            return identityMatchedDetectedSerial.Trim();
        }

        // If stored serial is a NOSN hash or empty, show "N/A" instead of leaking the fallback identity hash.
        if (string.IsNullOrWhiteSpace(storedSerial) ||
            storedSerial.StartsWith("NOSN-", StringComparison.OrdinalIgnoreCase))
        {
            return "N/A";
        }

        // Otherwise use the stored serial captured with the card/certificate.
        return storedSerial;
    }

    private void ApplyFilters()
    {
        FilteredCards.Clear();

        var allLabel = L.Get("Common.All");
        var activeLabel = L.Get("Common.Active");
        var archivedLabel = L.Get("Common.Archived");

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

            if (!string.IsNullOrEmpty(SelectedGradeFilter) && SelectedGradeFilter != allLabel && card.OverallGrade != SelectedGradeFilter)
            {
                continue;
            }

            if (ShowArchivedOnly && !card.IsArchived)
            {
                continue;
            }

            if (!ShowArchivedOnly && SelectedStatusFilter == activeLabel && card.IsArchived)
            {
                continue;
            }
            
            if (!ShowArchivedOnly && SelectedStatusFilter == archivedLabel && !card.IsArchived)
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
            StatusMessage = L.Get("DiskCards.Status.NoMatch", DiskCards.Count.ToString(), activeCount.ToString(), archivedCount.ToString());
        }
        else if (FilteredCards.Count < DiskCards.Count)
        {
            StatusMessage = L.Get("DiskCards.Status.Filtered", FilteredCards.Count.ToString(), DiskCards.Count.ToString(), activeCount.ToString(), archivedCount.ToString());
        }
        else
        {
            StatusMessage = L.Get("DiskCards.Status.Total", DiskCards.Count.ToString(), activeCount.ToString(), archivedCount.ToString());
        }
        
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(CardCount));
    }

    #endregion
}