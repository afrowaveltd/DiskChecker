using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// Provides trend analysis for historical tests of a selected disk.
/// </summary>
public partial class AnalysisViewModel : ViewModelBase
{
    private readonly HistoryService _historyService;
    private readonly LineSeries _scoreSeries;
    private readonly LineSeries _temperatureSeries;

    [ObservableProperty]
    private ObservableCollection<DriveCompareItem> drives = [];

    [ObservableProperty]
    private DriveCompareItem? selectedDrive;

    [ObservableProperty]
    private ObservableCollection<TestHistoryItem> driveHistory = [];

    [ObservableProperty]
    private PlotModel trendPlotModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisViewModel"/> class.
    /// </summary>
    public AnalysisViewModel(HistoryService historyService)
    {
        _historyService = historyService;
        _scoreSeries = new LineSeries
        {
            Title = "Skóre",
            Color = OxyColors.SteelBlue,
            StrokeThickness = 2
        };
        _temperatureSeries = new LineSeries
        {
            Title = "Teplota",
            Color = OxyColors.OrangeRed,
            StrokeThickness = 2,
            YAxisKey = "TempAxis"
        };
        TrendPlotModel = CreatePlotModel();
    }

    /// <inheritdoc />
    public override async Task InitializeAsync()
    {
        var drivesWithTests = await _historyService.GetDrivesWithTestsAsync();
        Drives = new ObservableCollection<DriveCompareItem>(drivesWithTests.OrderBy(d => d.DriveName));
        SelectedDrive = Drives.FirstOrDefault();
        await RefreshAnalysisAsync();
    }

    partial void OnSelectedDriveChanged(DriveCompareItem? value)
    {
        _ = RefreshAnalysisAsync();
    }

    /// <summary>
    /// Refreshes trend analysis for selected drive.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAnalysisAsync()
    {
        if (SelectedDrive == null)
        {
            DriveHistory.Clear();
            _scoreSeries.Points.Clear();
            _temperatureSeries.Points.Clear();
            TrendPlotModel.InvalidatePlot(true);
            StatusMessage = "Vyberte disk pro analýzu v čase.";
            return;
        }

        IsBusy = true;
        var history = await _historyService.GetDriveHistoryAsync(SelectedDrive.DriveName);
        DriveHistory = new ObservableCollection<TestHistoryItem>(history.OrderBy(h => h.TestDate));

        _scoreSeries.Points.Clear();
        _temperatureSeries.Points.Clear();

        foreach (var item in DriveHistory)
        {
            var x = DateTimeAxis.ToDouble(item.TestDate);
            _scoreSeries.Points.Add(new DataPoint(x, item.Score));

            if (item.SmartaData != null && item.SmartaData.Temperature > 0)
            {
                _temperatureSeries.Points.Add(new DataPoint(x, item.SmartaData.Temperature));
            }
        }

        TrendPlotModel.InvalidatePlot(true);
        StatusMessage = $"Analýza načtena: {DriveHistory.Count} testů pro disk {SelectedDrive.DriveName}.";
        IsBusy = false;
    }

    private PlotModel CreatePlotModel()
    {
        var model = new PlotModel
        {
            Title = "Trend disku v čase"
        };

        model.Axes.Add(new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Datum testu",
            StringFormat = "dd.MM HH:mm"
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Skóre",
            Minimum = 0,
            Maximum = 100
        });

        model.Axes.Add(new LinearAxis
        {
            Key = "TempAxis",
            Position = AxisPosition.Right,
            Title = "Teplota (°C)",
            Minimum = 0
        });

        model.Series.Add(_scoreSeries);
        model.Series.Add(_temperatureSeries);
        return model;
    }
}
