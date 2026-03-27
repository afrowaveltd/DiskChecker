using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DiskChecker.UI.Avalonia.Views;

/// <summary>
/// View pro interní zobrazení plného reportu.
/// </summary>
public partial class FullReportViewerView : UserControl
{
    /// <summary>
    /// Inicializuje novou instanci třídy <see cref="FullReportViewerView"/>.
    /// </summary>
    public FullReportViewerView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
