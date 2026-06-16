using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DiskChecker.UI.Avalonia.Views;

public partial class SeekTestView : UserControl
{
    public SeekTestView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
