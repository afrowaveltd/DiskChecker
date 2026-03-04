namespace DiskChecker.Application.Constants;

/// <summary>
/// Application-wide constants shared between Console, WPF and other UI implementations
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// UI color codes used across all interfaces
    /// </summary>
    public static class Colors
    {
        public const string Blue = "#4A90E2";
        public const string Green = "#28A745";
        public const string Yellow = "#FFC107";
        public const string Orange = "#FF8C00";
        public const string Red = "#DC3545";
        public const string DarkRed = "#8B0000";
        public const string Gray = "#666666";
        public const string LightGray = "#E0E0E0";
        public const string White = "#FFFFFF";
        public const string Cyan = "#007ACC";
    }

    /// <summary>
    /// UI constants for Surface Test visualization
    /// </summary>
    public static class SurfaceTest
    {
        public const int VisualGridRows = 10;
        public const int VisualGridColumns = 100;
        public const int TotalVisualBlocks = VisualGridRows * VisualGridColumns;
        public const long DefaultBytesPerVisualBlock = 1024L * 1024L * 1024L; // 1 GB
    }

    /// <summary>
    /// SMART check related constants
    /// </summary>
    public static class SmartCheck
    {
        public const int RefreshIntervalSeconds = 15;
        public const int WarningThresholdPercent = 80;
        public const int CriticalThresholdPercent = 90;
    }

    /// <summary>
    /// Report and export constants
    /// </summary>
    public static class Reports
    {
        public const int GradeAThreshold = 90;
        public const int GradeBThreshold = 80;
        public const int GradeCThreshold = 70;
        public const int GradeDThreshold = 60;
    }

    /// <summary>
    /// Block status codes for visualization
    /// </summary>
    public static class BlockStatus
    {
        public const int Untested = 0;
        public const int Processing = 1;
        public const int WriteOk = 2;
        public const int ReadOk = 3;
        public const int Error = 4;
    }

    /// <summary>
    /// Performance thresholds
    /// </summary>
    public static class Performance
    {
        public const double MinimumReadSpeedMbps = 10.0;
        public const double MinimumWriteSpeedMbps = 10.0;
        public const double GreenThresholdPercent = 0.8; // 80% of max
        public const double OrangeThresholdPercent = 0.5; // 50% of max
    }
}
