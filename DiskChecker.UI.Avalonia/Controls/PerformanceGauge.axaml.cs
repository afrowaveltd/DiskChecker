using Avalonia;
using Avalonia.Controls;

namespace DiskChecker.UI.Avalonia.Controls;

/// <summary>
/// Reusable dashboard/gauge panel for disk test telemetry.
/// It is intentionally self-contained and theme-aware so it can later replace
/// individual statistic cards in surface, sanitization, backup/restore and seek tests.
/// </summary>
public partial class PerformanceGauge : UserControl
{
    public PerformanceGauge()
    {
        InitializeComponent();
        // Recalculate SpeedPercent when dependent properties change
        CurrentValueProperty.Changed.AddClassHandler<PerformanceGauge>((gauge, _) => gauge.UpdateSpeedPercent());
        ScaleMaxValueProperty.Changed.AddClassHandler<PerformanceGauge>((gauge, _) => gauge.UpdateSpeedPercent());
    }

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PerformanceGauge, string>(nameof(Title), "Výkon disku");

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<PerformanceGauge, string>(nameof(Subtitle), "Aktuální telemetrie testu");

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<PerformanceGauge, string>(nameof(Unit), "MB/s");

    public static readonly StyledProperty<double> CurrentValueProperty =
        AvaloniaProperty.Register<PerformanceGauge, double>(nameof(CurrentValue));

    public static readonly StyledProperty<double> MinValueProperty =
        AvaloniaProperty.Register<PerformanceGauge, double>(nameof(MinValue));

    public static readonly StyledProperty<double> AverageValueProperty =
        AvaloniaProperty.Register<PerformanceGauge, double>(nameof(AverageValue));

    public static readonly StyledProperty<double> MaxValueProperty =
        AvaloniaProperty.Register<PerformanceGauge, double>(nameof(MaxValue));

    public static readonly StyledProperty<double> ScaleMaxValueProperty =
        AvaloniaProperty.Register<PerformanceGauge, double>(nameof(ScaleMaxValue), 100d);

    public static readonly StyledProperty<double> ProgressPercentProperty =
        AvaloniaProperty.Register<PerformanceGauge, double>(nameof(ProgressPercent));

    /// <summary>
    /// Speed as percentage of scale max (0-100), computed from CurrentValue / ScaleMaxValue.
    /// Used by the gauge bar to show current speed visually.
    /// </summary>
    public static readonly StyledProperty<double> SpeedPercentProperty =
        AvaloniaProperty.Register<PerformanceGauge, double>(nameof(SpeedPercent));

    public double SpeedPercent
    {
        get => GetValue(SpeedPercentProperty);
        private set => SetValue(SpeedPercentProperty, value);
    }

    /// <summary>
    /// Recalculates SpeedPercent from CurrentValue and ScaleMaxValue.
    /// Called automatically when either dependency changes.
    /// </summary>
    private void UpdateSpeedPercent()
    {
        SpeedPercent = ScaleMaxValue > 0 ? Math.Min(100, CurrentValue / ScaleMaxValue * 100.0) : 0;
    }

    public static readonly StyledProperty<int> ErrorCountProperty =
        AvaloniaProperty.Register<PerformanceGauge, int>(nameof(ErrorCount));

    public static readonly StyledProperty<double> TemperatureCelsiusProperty =
        AvaloniaProperty.Register<PerformanceGauge, double>(nameof(TemperatureCelsius));

    public static readonly StyledProperty<string> EtaTextProperty =
        AvaloniaProperty.Register<PerformanceGauge, string>(nameof(EtaText), "--:--");

    public static readonly StyledProperty<string> StatusTextProperty =
        AvaloniaProperty.Register<PerformanceGauge, string>(nameof(StatusText), "Připraveno");

    public static readonly StyledProperty<bool> IsOverheatedProperty =
        AvaloniaProperty.Register<PerformanceGauge, bool>(nameof(IsOverheated));

    public static readonly StyledProperty<bool> HasErrorsProperty =
        AvaloniaProperty.Register<PerformanceGauge, bool>(nameof(HasErrors));

    public static readonly StyledProperty<bool> IsStalledProperty =
        AvaloniaProperty.Register<PerformanceGauge, bool>(nameof(IsStalled));

    public static readonly StyledProperty<bool> SmartWarningProperty =
        AvaloniaProperty.Register<PerformanceGauge, bool>(nameof(SmartWarning));

    public static readonly StyledProperty<bool> BackupVerifiedProperty =
        AvaloniaProperty.Register<PerformanceGauge, bool>(nameof(BackupVerified));

    public static readonly StyledProperty<bool> RestoreVerifiedProperty =
        AvaloniaProperty.Register<PerformanceGauge, bool>(nameof(RestoreVerified));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public double CurrentValue { get => GetValue(CurrentValueProperty); set => SetValue(CurrentValueProperty, value); }
    public double MinValue { get => GetValue(MinValueProperty); set => SetValue(MinValueProperty, value); }
    public double AverageValue { get => GetValue(AverageValueProperty); set => SetValue(AverageValueProperty, value); }
    public double MaxValue { get => GetValue(MaxValueProperty); set => SetValue(MaxValueProperty, value); }
    public double ScaleMaxValue { get => GetValue(ScaleMaxValueProperty); set => SetValue(ScaleMaxValueProperty, value); }
    public double ProgressPercent { get => GetValue(ProgressPercentProperty); set => SetValue(ProgressPercentProperty, value); }
    public int ErrorCount { get => GetValue(ErrorCountProperty); set => SetValue(ErrorCountProperty, value); }
    public double TemperatureCelsius { get => GetValue(TemperatureCelsiusProperty); set => SetValue(TemperatureCelsiusProperty, value); }
    public string EtaText { get => GetValue(EtaTextProperty); set => SetValue(EtaTextProperty, value); }
    public string StatusText { get => GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public bool IsOverheated { get => GetValue(IsOverheatedProperty); set => SetValue(IsOverheatedProperty, value); }
    public bool HasErrors { get => GetValue(HasErrorsProperty); set => SetValue(HasErrorsProperty, value); }
    public bool IsStalled { get => GetValue(IsStalledProperty); set => SetValue(IsStalledProperty, value); }
    public bool SmartWarning { get => GetValue(SmartWarningProperty); set => SetValue(SmartWarningProperty, value); }
    public bool BackupVerified { get => GetValue(BackupVerifiedProperty); set => SetValue(BackupVerifiedProperty, value); }
    public bool RestoreVerified { get => GetValue(RestoreVerifiedProperty); set => SetValue(RestoreVerifiedProperty, value); }
}
