using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DiskChecker.UI.Avalonia.Views
{
    public partial class AnalysisView : UserControl
    {
        public AnalysisView()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
