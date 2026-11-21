using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
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
    private int _selectionGeneration;

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

        ComparisonChartModel = new PlotModel { Title = "Skóre zdraví (0–100)" };
        SpeedChartModel = new PlotModel { Title = "Naměřená rychlost (MB/s)" };
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
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(CanCompare));
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(IsNotLoading));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public PlotModel ComparisonChartModel { get; }
    public PlotModel SpeedChartModel { get; }
    public bool CanCompare => SelectedDisks.Count >= 2 && !IsLoading;
    public bool HasComparison => ComparisonResults.Count > 0;
    public bool CanExport => HasComparison && !IsLoading;
    public bool IsNotLoading => !IsLoading;

    public bool HasBestDisk => BestDisk != null;
    public string BestDiskText => ComparisonResults.Count > 0
        ? $"🏆 {ComparisonResults[0].Disk.ModelName}: {ComparisonResults[0].Grade}, {ComparisonResults[0].Score:F0}/100"
        : L.Get("DiskComparison.NeedTwo");

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
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), $"Nepodařilo se načíst disky: {ex.Message}");
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
        if (SelectedDisks.Count < 2 || IsLoading)
        {
            StatusMessage = L.Get("DiskComparison.NeedTwo");
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Porovnávám disky...";

            var diskIds = SelectedDisks.Select(d => d.Disk.Id).ToList();
            int generation = _selectionGeneration;
            var results = await _comparisonService.CompareAsync(diskIds);
            if (generation != _selectionGeneration) return;

            ComparisonResults.Clear();
            foreach (var result in results.OrderBy(r => r.Rank))
            {
                ComparisonResults.Add(result);
            }

            // Update chart
            UpdateChart();

            // Find best disk
            BestDisk = results.OrderBy(r => r.Rank).FirstOrDefault()?.Disk;
            OnPropertyChanged(nameof(HasComparison));
            OnPropertyChanged(nameof(CanExport));
            StatusMessage = "Porovnání dokončeno";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba: {ex.Message}";
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), $"Nepodařilo se porovnat disky: {ex.Message}");
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

            await _dialogService.ShowMessageAsync(L.Get("Common.Comparison"), message);
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
        _selectionGeneration++;
        ComparisonResults.Clear();
        BestDisk = null;
        UpdateChart();
        OnPropertyChanged(nameof(SelectedDisks));
        OnPropertyChanged(nameof(CanCompare));
        OnPropertyChanged(nameof(HasComparison));
        OnPropertyChanged(nameof(CanExport));
        StatusMessage = L.Get("DiskComparison.SelectionCount", SelectedDisks.Count);
    }

    /// <summary>Export only the visible, real comparison results.</summary>
    [RelayCommand]
    private async Task ExportComparisonAsync()
    {
        if (!CanExport) return;
        var path = await _dialogService.PickSaveFileAsync(
            L.Get("DiskComparison.ExportTitle"), "disk-comparison.csv");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var csv = DiskComparisonCsvExporter.Build(ComparisonResults);
            await File.WriteAllTextAsync(path, csv, new UTF8Encoding(true));
            StatusMessage = L.Get("DiskComparison.ExportSuccess");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusMessage = ex.Message;
            await _dialogService.ShowErrorAsync(L.Get("Common.Error"), ex.Message);
        }
    }

    private void UpdateChart()
    {
        // Scores and throughput must NEVER share the same value axis.
        // Distinct plots also avoid treating an unmeasured write phase as 0 MB/s.
        ComparisonChartModel.Series.Clear();
        ComparisonChartModel.Axes.Clear();
        SpeedChartModel.Series.Clear();
        SpeedChartModel.Axes.Clear();

        var scores = new ColumnSeries { Title = "Score" };
        var writes = new ColumnSeries { Title = "Write MB/s" };
        var reads = new ColumnSeries { Title = "Read MB/s" };
        var scoreCategory = new CategoryAxis { Position = AxisPosition.Bottom, Title = "Disk" };
        var speedCategory = new CategoryAxis { Position = AxisPosition.Bottom, Title = "Disk" };
        int index = 0;
        foreach (var result in ComparisonResults)
        {
            string name = result.Disk.ModelName;
            string label = name.Length > 20 ? name[..17] + "..." : name;
            scoreCategory.Labels.Add(label);
            speedCategory.Labels.Add(label);
            if (double.IsFinite(result.Score))
                scores.Items.Add(new ColumnItem(Math.Clamp(result.Score, 0d, 100d), index)
                {
                    Color = GetScoreColor(result.Score)
                });
            if (result.HasWriteMeasurement && double.IsFinite(result.AvgWriteSpeed) && result.AvgWriteSpeed >= 0)
                writes.Items.Add(new ColumnItem(result.AvgWriteSpeed, index));
            if (result.HasReadMeasurement && double.IsFinite(result.AvgReadSpeed) && result.AvgReadSpeed >= 0)
                reads.Items.Add(new ColumnItem(result.AvgReadSpeed, index));
            index++;
        }

        if (index > 0)
        {
            ComparisonChartModel.Series.Add(scores);
            ComparisonChartModel.Axes.Add(scoreCategory);
            ComparisonChartModel.Axes.Add(new LinearAxis
            {
                Title = "Score (0–100)", Position = AxisPosition.Left,
                Minimum = 0, Maximum = 100
            });
            SpeedChartModel.Series.Add(writes);
            SpeedChartModel.Series.Add(reads);
            SpeedChartModel.Axes.Add(speedCategory);
            SpeedChartModel.Axes.Add(new LinearAxis
            {
                Title = "MB/s", Position = AxisPosition.Left, Minimum = 0
            });
        }
        ComparisonChartModel.InvalidatePlot(true);
        SpeedChartModel.InvalidatePlot(true);
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