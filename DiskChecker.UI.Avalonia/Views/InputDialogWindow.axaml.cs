using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DiskChecker.UI.Avalonia.Views;

public partial class InputDialogWindow : Window
{
    public InputDialogWindow()
    {
        InitializeComponent();
    }

    public InputDialogWindow(string title, string message, string defaultValue = "")
    {
        DataContext = new InputDialogViewModel(this, title, message, defaultValue);
        InitializeComponent();
    }
}

public partial class InputDialogViewModel : ObservableObject
{
    private readonly InputDialogWindow _window;

    public InputDialogViewModel(InputDialogWindow window, string title, string message, string defaultValue)
    {
        _window = window;
        DialogTitle = title;
        Message = message;
        InputText = defaultValue;
        Placeholder = message;
        
        OkCommand = new RelayCommand(() => _window.Close(InputText));
        CancelCommand = new RelayCommand(() => _window.Close(null));
    }

    public string DialogTitle { get; }
    public string Message { get; }
    public string Placeholder { get; }
    
    [ObservableProperty] private string _inputText = string.Empty;

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }
}