using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using Spectre.Console;

namespace DiskChecker.UI.Console;

/// <summary>
/// Provides live SMART data display during surface tests using Spectre.Console.
/// </summary>
public class LiveSmartDisplay
{
    private readonly SmartCheckService _smartCheckService;
    private SmartaData? _currentSmartData;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveSmartDisplay"/> class.
    /// </summary>
    /// <param name="smartCheckService">SMART check service for reading data.</param>
    public LiveSmartDisplay(SmartCheckService smartCheckService)
    {
        _smartCheckService = smartCheckService;
    }

    /// <summary>
    /// Gets the current SMART data snapshot.
    /// </summary>
    public SmartaData? CurrentSmartData => _currentSmartData;

    /// <summary>
    /// Starts live SMART data monitoring for the specified drive.
    /// </summary>
    /// <param name="drive">Drive to monitor.</param>
    /// <param name="updateAction">Action to call when data is updated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartMonitoringAsync(
        CoreDriveInfo drive,
        Action? updateAction = null,
        CancellationToken cancellationToken = default)
    {
        // Load initial data
        var initialData = await _smartCheckService.GetSmartaDataSnapshotAsync(drive, cancellationToken);
        if (initialData != null)
        {
            _currentSmartData = initialData;
            updateAction?.Invoke();
        }
    }

    /// <summary>
    /// Updates SMART data once (for manual refresh).
    /// </summary>
    /// <param name="drive">Drive to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RefreshDataAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        var data = await _smartCheckService.GetSmartaDataSnapshotAsync(drive, cancellationToken);
        if (data != null)
        {
            _currentSmartData = data;
        }
    }

    /// <summary>
    /// Creates a formatted table with SMART data for display.
    /// </summary>
    /// <returns>Spectre Console table with SMART data.</returns>
    public Table CreateSmartDataTable()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn(new TableColumn("[bold cyan]SMART Parametr[/]").Width(25))
            .AddColumn(new TableColumn("[bold cyan]Hodnota[/]").Width(35));

        if (_currentSmartData == null)
        {
            table.AddRow("[dim]SMART data[/]", "[dim]Načítání...[/]");
            return table;
        }

        // Model and basic info
        if (!string.IsNullOrWhiteSpace(_currentSmartData.DeviceModel))
        {
            table.AddRow("[yellow]Model disku[/]", $"[bold white]{Markup.Escape(_currentSmartData.DeviceModel)}[/]");
        }

        if (!string.IsNullOrWhiteSpace(_currentSmartData.ModelFamily))
        {
            table.AddRow("[yellow]Rodina[/]", Markup.Escape(_currentSmartData.ModelFamily));
        }

        if (!string.IsNullOrWhiteSpace(_currentSmartData.SerialNumber))
        {
            table.AddRow("[yellow]Sériové číslo[/]", Markup.Escape(_currentSmartData.SerialNumber));
        }

        if (!string.IsNullOrWhiteSpace(_currentSmartData.FirmwareVersion))
        {
            table.AddRow("[yellow]Firmware[/]", Markup.Escape(_currentSmartData.FirmwareVersion));
        }

        // Temperature (with live indicator)
        if (_currentSmartData.Temperature > 0)
        {
            var tempColor = GetTemperatureColor(_currentSmartData.Temperature);
            var tempIcon = GetTemperatureIcon(_currentSmartData.Temperature);
            table.AddRow(
                "[yellow]🌡️  Teplota (živě)[/]",
                $"[{tempColor}]{tempIcon} {_currentSmartData.Temperature:F1} °C[/]");
        }

        // Power-on hours
        if (_currentSmartData.PowerOnHours > 0)
        {
            var formatted = FormatPowerOnTime(_currentSmartData.PowerOnHours);
            table.AddRow(
                "[yellow]⏱️  Odpracováno[/]",
                $"[white]{_currentSmartData.PowerOnHours:N0} h[/] [dim]({formatted})[/]");
        }

        // Wear leveling (for SSD)
        if (_currentSmartData.WearLevelingCount.HasValue)
        {
            var wearColor = GetWearLevelColor(_currentSmartData.WearLevelingCount.Value);
            table.AddRow(
                "[yellow]⚙️  Opotřebení SSD[/]",
                $"[{wearColor}]{_currentSmartData.WearLevelingCount.Value} %[/]");
        }

        // Critical health indicators
        var errorColor = GetSectorErrorColor(
            _currentSmartData.ReallocatedSectorCount +
            _currentSmartData.PendingSectorCount +
            _currentSmartData.UncorrectableErrorCount);

        table.AddRow(
            "[yellow]🔴 Přemístěné sektory[/]",
            FormatSectorCount(_currentSmartData.ReallocatedSectorCount));

        table.AddRow(
            "[yellow]⚠️  Čekající sektory[/]",
            FormatSectorCount(_currentSmartData.PendingSectorCount));

        table.AddRow(
            "[yellow]❌ Neopravitelné chyby[/]",
            FormatSectorCount(_currentSmartData.UncorrectableErrorCount));

        return table;
    }

    /// <summary>
    /// Creates a compact single-line status for inline display.
    /// </summary>
    /// <returns>Formatted status string.</returns>
    public string CreateCompactStatus()
    {
        if (_currentSmartData == null)
        {
            return "[dim]SMART: načítání...[/]";
        }

        var tempColor = GetTemperatureColor(_currentSmartData.Temperature);
        var tempIcon = GetTemperatureIcon(_currentSmartData.Temperature);
        var totalErrors = _currentSmartData.ReallocatedSectorCount +
                         _currentSmartData.PendingSectorCount +
                         _currentSmartData.UncorrectableErrorCount;
        var errorStatus = totalErrors == 0 ? "[green]✓[/]" : $"[red]⚠ {totalErrors}[/]";

        return $"[dim]SMART:[/] [{tempColor}]{tempIcon} {_currentSmartData.Temperature:F1}°C[/] | {errorStatus}";
    }

    private static string GetTemperatureColor(double temperature)
    {
        return temperature switch
        {
            >= 60 => "red",
            >= 50 => "yellow",
            >= 40 => "orange1",
            _ => "green"
        };
    }

    private static string GetTemperatureIcon(double temperature)
    {
        return temperature switch
        {
            >= 60 => "🔥",
            >= 50 => "⚠️",
            >= 40 => "🌡️",
            _ => "❄️"
        };
    }

    private static string GetWearLevelColor(int wearPercent)
    {
        return wearPercent switch
        {
            >= 90 => "red",
            >= 70 => "yellow",
            >= 50 => "orange1",
            _ => "green"
        };
    }

    private static string GetSectorErrorColor(long count)
    {
        return count switch
        {
            > 10 => "red",
            > 0 => "yellow",
            _ => "green"
        };
    }

    private static string FormatSectorCount(long count)
    {
        if (count == 0)
        {
            return "[green]✓ 0[/]";
        }

        var color = GetSectorErrorColor(count);
        return $"[{color}]⚠ {count:N0}[/]";
    }

    private static string FormatPowerOnTime(int hours)
    {
        if (hours < 24)
            return $"{hours} h";

        var days = hours / 24;
        if (days < 30)
            return $"{days} dní";

        var months = days / 30;
        if (months < 12)
            return $"{months} měsíců";

        var years = months / 12;
        var remainingMonths = months % 12;
        return remainingMonths > 0 ? $"{years} let {remainingMonths} měsíců" : $"{years} let";
    }
}
