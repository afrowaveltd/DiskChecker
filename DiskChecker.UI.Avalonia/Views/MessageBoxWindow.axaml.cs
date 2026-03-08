using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using DiskChecker.UI.Avalonia.Services;

namespace DiskChecker.UI.Avalonia.Views;

public partial class MessageBoxWindow : Window
{
    public string MessageTitle { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string MessageIcon { get; set; } = string.Empty;
    public bool ShowYesNo { get; set; }
    public bool ShowOK { get; set; }
    public bool ShowCancel { get; set; }

    public MessageBoxWindow()
    {
        InitializeComponent();
    }

    public MessageBoxWindow(string title, string message, MessageBoxIcon icon)
    {
        MessageTitle = title;
        Message = message;
        
        switch (icon)
        {
            case MessageBoxIcon.Information:
                MessageIcon = "ℹ️";
                break;
            case MessageBoxIcon.Error:
                MessageIcon = "❌";
                break;
            case MessageBoxIcon.Question:
                MessageIcon = "❓";
                break;
        }

        ShowYesNo = icon == MessageBoxIcon.Question;
        ShowOK = icon != MessageBoxIcon.Question;
        ShowCancel = icon == MessageBoxIcon.Question;

        InitializeComponent();
        DataContext = this;
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.Yes);
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.No);
    }

    private void OKButton_Click(object sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.OK);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.Cancel);
    }
}
