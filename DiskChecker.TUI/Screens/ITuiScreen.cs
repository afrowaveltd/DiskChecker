namespace DiskChecker.TUI.Screens;

/// <summary>Result of a screen interaction.</summary>
public enum ScreenAction
{
    /// <summary>Re-render the same screen.</summary>
    Stay,

    /// <summary>Go back to the previous screen.</summary>
    Back,

    /// <summary>Open another screen.</summary>
    Navigate,

    /// <summary>Leave the application.</summary>
    Exit,

    /// <summary>Show the main navigation menu.</summary>
    Menu,
}

/// <summary>Outcome produced by a screen run loop.</summary>
public readonly record struct ScreenResult(ScreenAction Action, string? Target = null)
{
    public static ScreenResult Stay() => new(ScreenAction.Stay);

    public static ScreenResult Back() => new(ScreenAction.Back);

    public static ScreenResult Exit() => new(ScreenAction.Exit);

    public static ScreenResult Menu() => new(ScreenAction.Menu);

    public static ScreenResult Navigate(string target) => new(ScreenAction.Navigate, target);
}

/// <summary>Identifies a navigable terminal screen.</summary>
public static class ScreenIds
{
    public const string DiskSelection = "disks";
    public const string DiskCards = "cards";
    public const string DiskCardDetail = "card";
    public const string SurfaceTest = "surface";
    public const string SmartCheck = "smart";
    public const string SeekTest = "seek";
    public const string AbsoluteDestructive = "destructive";
    public const string SafeDestructive = "safe";
    public const string Analysis = "analysis";
    public const string Comparison = "comparison";
    public const string Report = "report";
    public const string History = "history";
    public const string Certificates = "certificates";
    public const string Certificate = "certificate";
    public const string Backup = "backup";
    public const string Restore = "restore";
    public const string Settings = "settings";
    public const string About = "about";
}

/// <summary>A terminal screen: renders content, handles actions, returns navigation intent.</summary>
public interface ITuiScreen
{
    string Id { get; }

    string TitleKey { get; }

    Task<ScreenResult> ShowAsync(Services.TuiContext context, CancellationToken cancellationToken);
}
