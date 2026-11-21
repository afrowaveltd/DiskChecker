using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.UI.Avalonia.Services;

namespace DiskChecker.UI.Avalonia.Views;

public partial class InputDialogWindow : Window
{
    public InputDialogWindow()
    {
        InitializeComponent();
    }

    public InputDialogWindow(string title, string message, string defaultValue, LocaleService locale)
    {
        DataContext = new InputDialogViewModel(this, title, message, defaultValue, locale);
        InitializeComponent();
    }
}

public partial class InputDialogViewModel : ObservableObject
{
    private readonly InputDialogWindow _window;

    public InputDialogViewModel(InputDialogWindow window, string title, string message, string defaultValue, LocaleService locale)
    {
        _window = window;
        DialogTitle = title;
        Message = message;
        InputText = defaultValue;
        Placeholder = locale.Get("InputDialog.EnterValue");
        // A dialog has its own ViewModel, not the page-level L indexer.
        // Resolve both visible actions when the dialog opens.
        OkButtonText = locale.GetString("Common.OK", "OK");
        CancelButtonText = locale.GetString("Common.Cancel", "Cancel");
        
        OkCommand = new RelayCommand(() => _window.Close(InputText));
        CancelCommand = new RelayCommand(() => _window.Close(null));
    }

    public string DialogTitle { get; }
    public string Message { get; }
    public string Placeholder { get; }
    public string OkButtonText { get; }
    public string CancelButtonText { get; }
    
    [ObservableProperty] private string _inputText = string.Empty;

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }
}