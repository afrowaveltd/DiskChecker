using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// View model for comparing multiple disks and selecting the best one.
/// </summary>
public partial class DiskComparisonViewModel : ViewModelBase, INavigableViewModel
{
    private readonly IDiskCardRepository _diskCardRepository;
    private readonly IDiskComparisonService _comparisonService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    
    private ObservableCollection<DiskCard> _availableDisks = new();
    private ObservableCollection<DiskComparisonItem> _selectedDisks = new();
    private ObservableCollection<DiskComparisonResult> _comparisonResults = new();
    private DiskCard? _bestDisk;
    private bool _isLoading;
    private string _statusMessage = "Porovnání disků";

    public DiskComparisonViewModel(
        IDiskCardRepository diskCardRepository,
        IDiskComparisonService comparisonService,
        INavigationService navigationService,
        IDialogService dialogService)
    {
        _diskCardRepository = diskCardRepository;
        _comparisonService = comparisonService;
        _navigationService = navigationService;
        _dialogService = dialogService;

        ComparisonChartModel = new PlotModel { Title = "Porovnání výkonu" };
    }

    #region Properties

    public ObservableCollection<DiskCard> AvailableDisks
    {
        get => _availableDisks;
        set => SetProperty(ref _availableDisks, value);
    }

    public ObservableCollection<DiskComparisonItem> SelectedDisks
    {
        get => _selectedDisks;
        set => SetProperty(ref _selectedDisks, value);
    }

    public ObservableCollection<DiskComparisonResult> ComparisonResults
    {
        get => _comparisonResults;
        set => SetProperty(ref _comparisonResults, value);
    }

    public DiskCard? BestDisk
    {
        get => _bestDisk;
        set
        {
            if (SetProperty(ref _bestDisk, value))
            {
                OnPropertyChanged(nameof(HasBestDisk));
                OnPropertyChanged(nameof(BestDiskText));
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

    public PlotModel ComparisonChartModel { get; }

    public bool HasBestDisk => BestDisk != null;
    public string BestDiskText => BestDisk != null
        ? $"🏆 Nejlepší disk: {BestDisk.ModelName} (Známka: {BestDisk.OverallGrade}, Skóre: {BestDisk.OverallScore:F0})"
        : "Vyberte disky k porovnání";

    #endregion

    #region Navigation

    public void OnNavigatedTo()
    {
        _ = LoadAvailableDisksAsync();
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task LoadAvailableDisksAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Načítám disky...";

            var disks = await _diskCardRepository.GetActiveAsync();
            
            // Only disks with at least one test
            var testedDisks = disks.Where(d => d.TestCount > 0).ToList();

            AvailableDisks.Clear();
            foreach (var disk in testedDisks.OrderByDescending(d => d.OverallScore))
            {
                AvailableDisks.Add(disk);
            }

            SelectionChanged();

            StatusMessage = $"Nalezeno {testedDisks.Count} testovaných disků";
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

    [RelayCommand]
    private void AddDisk(DiskCard disk)
    {
        if (disk == null) return;
        if (SelectedDisks.Any(d => d.Disk.Id == disk.Id)) return;
        if (SelectedDisks.Count >= 5)
        {
            StatusMessage = "Maximálně 5 disků pro porovnání";
            return;
        }

        SelectedDisks.Add(new DiskComparisonItem { Disk = disk, IsSelected = true });
        SelectionChanged();
    }

    [RelayCommand]
    private void RemoveDisk(DiskComparisonItem item)
    {
        if (item == null) return;
        SelectedDisks.Remove(item);
        SelectionChanged();
    }

    [RelayCommand]
    private async Task CompareDisksAsync()
    {
        if (SelectedDisks.Count < 2)
        {
            StatusMessage = "Vyberte alespoň 2 disky k porovnání";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Porovnávám disky...";

            var diskIds = SelectedDisks.Select(d => d.Disk.Id).ToList();
            var results = await _comparisonService.CompareAsync(diskIds);

            ComparisonResults.Clear();
            foreach (var result in results.OrderBy(r => r.Rank))
            {
                ComparisonResults.Add(result);
            }

            // Update chart
            UpdateChart();

            // Find best disk
            BestDisk = results.FirstOrDefault()?.Disk;
            StatusMessage = "Porovnání dokončeno";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync("Chyba", $"Nepodařilo se porovnat disky: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectBestDiskAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Hledám nejlepší disk...";

            var testedDisks = await _diskCardRepository.GetBestDisksAsync(1);
            BestDisk = testedDisks.FirstOrDefault();

            if (BestDisk != null)
            {
                // Add to selection if not already there
                if (!SelectedDisks.Any(d => d.Disk.Id == BestDisk.Id))
                {
                    SelectedDisks.Insert(0, new DiskComparisonItem { Disk = BestDisk, IsSelected = true });
                    SelectionChanged();
                }

                StatusMessage = $"Nejlepší disk: {BestDisk.ModelName} (Skóre: {BestDisk.OverallScore:F0})";
            }
            else
            {
                StatusMessage = "Žádné testované disky nenalezeny";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ComparePerformanceAsync()
    {
        if (SelectedDisks.Count != 2)
        {
            StatusMessage = "Vyberte přesně 2 disky pro porovnání výkonu";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Porovnávám výkon...";

            var comparison = await _comparisonService.ComparePerformanceAsync(
                SelectedDisks[0].Disk.Id,
                SelectedDisks[1].Disk.Id);

            // Show detailed comparison dialog
            var message = $"Porovnání výkonu:\n\n" +
                         $"Rychlejší disk: {comparison.FasterDisk}\n" +
                         $"Rozdíl zápisu: {comparison.WriteSpeedDifference:F1} MB/s ({comparison.SpeedAdvantagePercent}%)\n" +
                         $"Rozdíl čtení: {comparison.ReadSpeedDifference:F1} MB/s\n\n" +
                         $"Spolehlivější: {comparison.MoreReliableDisk}\n\n" +
                         $"Doporučení: {comparison.RecommendedDisk}\n\n" +
                         $"{comparison.Summary}";

            await _dialogService.ShowMessageAsync("Porovnání výkonu", message);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<DiskCardsViewModel>();
    }

    #endregion

    #region Private Methods

    private void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedDisks));
        OnPropertyChanged(nameof(CanCompare));

        StatusMessage = $"Vybráno {SelectedDisks.Count} disků k porovnání";
    }

    private bool CanCompare => SelectedDisks.Count >= 2;

    private void UpdateChart()
    {
        ComparisonChartModel.Series.Clear();
        ComparisonChartModel.Axes.Clear();

        if (ComparisonResults.Count == 0) return;

        // Bar chart for scores
        var scoreSeries = new OxyPlot.Series.BarSeries
        {
            Title = "Skóre",
            LabelPlacement = LabelPlacement.Inside,
            TextColor = OxyColors.White
        };

        var writeSpeedSeries = new OxyPlot.Series.BarSeries
        {
            Title = "Rychlost zápisu (MB/s)",
            LabelPlacement = LabelPlacement.Inside,
            TextColor = OxyColors.White
        };

        var readSpeedSeries = new OxyPlot.Series.BarSeries
        {
            Title = "Rychlost čtení (MB/s)",
            LabelPlacement = LabelPlacement.Inside,
            TextColor = OxyColors.White
        };

        var categoryAxis = new CategoryAxis { Title = "Disk", Position = AxisPosition.Bottom };

        foreach (var result in ComparisonResults)
        {
            var label = result.Disk.ModelName.Length > 15
                ? $"{result.Disk.ModelName[..12]}..."
                : result.Disk.ModelName;

            categoryAxis.Labels.Add(label);

            scoreSeries.Items.Add(new OxyPlot.Series.BarItem(result.Score) { Color = GetScoreColor(result.Score) });
            writeSpeedSeries.Items.Add(new OxyPlot.Series.BarItem(result.AvgWriteSpeed) { Color = OxyColors.Blue });
            readSpeedSeries.Items.Add(new OxyPlot.Series.BarItem(result.AvgReadSpeed) { Color = OxyColors.Green });
        }

        ComparisonChartModel.Series.Add(scoreSeries);
        ComparisonChartModel.Series.Add(writeSpeedSeries);
        ComparisonChartModel.Series.Add(readSpeedSeries);
        ComparisonChartModel.Axes.Add(categoryAxis);
        ComparisonChartModel.Axes.Add(new LinearAxis { Title = "Hodnota", Position = AxisPosition.Left });
        ComparisonChartModel.InvalidatePlot(true);
    }

    private static OxyColor GetScoreColor(double score)
    {
        return score switch
        {
            >= 90 => OxyColors.DarkGreen,
            >= 80 => OxyColors.Green,
            >= 70 => OxyColors.YellowGreen,
            >= 60 => OxyColors.Orange,
            >= 50 => OxyColors.OrangeRed,
            _ => OxyColors.Red
        };
    }

    #endregion
}

/// <summary>
/// Item for disk comparison selection.
/// </summary>
public class DiskComparisonItem : ObservableObject
{
    private bool _isSelected;

    public DiskCard Disk { get; set; } = null!;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}