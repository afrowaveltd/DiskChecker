using System;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.UI.Avalonia.Services;

namespace DiskChecker.UI.Avalonia.Views;

public partial class MessageBoxWindow : Window
{
    public MessageBoxWindow()
    {
        InitializeComponent();
    }
}

public partial class MessageBoxViewModel : ObservableObject
{
    private readonly Window _window;
    private MessageBoxResult _result = MessageBoxResult.Cancel;

    public MessageBoxViewModel(Window window, string title, string message, DialogType dialogType)
    {
        _window = window;
        MessageTitle = title;
        Message = message;
        DialogType = dialogType;
        
        PrimaryCommand = new RelayCommand(() => Close(MessageBoxResult.OK));
        NoCommand = new RelayCommand(() => Close(MessageBoxResult.No));
        CancelCommand = new RelayCommand(() => Close(MessageBoxResult.Cancel));
        CloseCommand = new RelayCommand(() => Close(MessageBoxResult.Cancel));
    }

    public string MessageTitle { get; }
    public string Message { get; }
    
    [ObservableProperty] private DialogType _dialogType;
    [ObservableProperty] private string? _subtitle;
    [ObservableProperty] private bool _hasSubtitle;

    // Icon colors based on type - using SolidColorBrush
    public Brush HeaderBackground => DialogType switch
    {
        DialogType.Info => new SolidColorBrush(Color.Parse("#EFF6FF")),
        DialogType.Success => new SolidColorBrush(Color.Parse("#ECFDF5")),
        DialogType.Warning => new SolidColorBrush(Color.Parse("#FFFBEB")),
        DialogType.Error => new SolidColorBrush(Color.Parse("#FEF2F2")),
        DialogType.Question => new SolidColorBrush(Color.Parse("#F5F3FF")),
        DialogType.Danger => new SolidColorBrush(Color.Parse("#FEF2F2")),
        _ => new SolidColorBrush(Color.Parse("#F8FAFC"))
    };

    public Brush IconBackground => DialogType switch
    {
        DialogType.Info => new SolidColorBrush(Color.Parse("#DBEAFE")),
        DialogType.Success => new SolidColorBrush(Color.Parse("#D1FAE5")),
        DialogType.Warning => new SolidColorBrush(Color.Parse("#FEF3C7")),
        DialogType.Error => new SolidColorBrush(Color.Parse("#FECACA")),
        DialogType.Question => new SolidColorBrush(Color.Parse("#DDD6FE")),
        DialogType.Danger => new SolidColorBrush(Color.Parse("#FECACA")),
        _ => new SolidColorBrush(Color.Parse("#E2E8F0"))
    };

    public string MessageIcon => DialogType switch
    {
        DialogType.Info => "ℹ",
        DialogType.Success => "✓",
        DialogType.Warning => "⚠",
        DialogType.Error => "✕",
        DialogType.Question => "?",
        DialogType.Danger => "☠",
        _ => "•"
    };

    // Button visibility
    public bool ShowPrimary => DialogType is DialogType.Info or DialogType.Success or DialogType.Warning or DialogType.Error or DialogType.Question or DialogType.Danger;
    public bool ShowNoButton => DialogType is DialogType.Question or DialogType.Danger;
    public bool ShowCancel => DialogType is DialogType.Question or DialogType.Danger;

    // Button text
    public string PrimaryButtonText => DialogType switch
    {
        DialogType.Question => "Ano",
        DialogType.Danger => "ANO, SMAZAT!",
        _ => "OK"
    };
    
    public string NoButtonText => "Ne";
    public string CancelButtonText => "Zrušit";

    // Button colors as Brushes
    public Brush PrimaryButtonBackground => DialogType switch
    {
        DialogType.Error => new SolidColorBrush(Color.Parse("#DC2626")),
        DialogType.Danger => new SolidColorBrush(Color.Parse("#B91C1C")),
        DialogType.Warning => new SolidColorBrush(Color.Parse("#D97706")),
        DialogType.Success => new SolidColorBrush(Color.Parse("#059669")),
        _ => new SolidColorBrush(Color.Parse("#2563EB"))
    };

    public Brush PrimaryButtonHoverBackground => DialogType switch
    {
        DialogType.Error => new SolidColorBrush(Color.Parse("#B91C1C")),
        DialogType.Danger => new SolidColorBrush(Color.Parse("#991B1B")),
        DialogType.Warning => new SolidColorBrush(Color.Parse("#B45309")),
        DialogType.Success => new SolidColorBrush(Color.Parse("#047857")),
        _ => new SolidColorBrush(Color.Parse("#1D4ED8"))
    };

    // Commands
    public ICommand PrimaryCommand { get; }
    public ICommand NoCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CloseCommand { get; }

    private void Close(MessageBoxResult result)
    {
        _result = result;
        _window.Close(result);
    }
}

public enum DialogType
{
    Info,
    Success,
    Warning,
    Error,
    Question,
    Danger  // For destructive operations
}