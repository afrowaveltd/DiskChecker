using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DiskChecker.UI.Avalonia.Views
{
    public partial class AnalysisView : UserControl
    {
        public AnalysisView()
        {
            InitializeComponent();
            SizeChanged += (_, args) =>
            {
                if (DataContext is DiskChecker.UI.Avalonia.ViewModels.AnalysisViewModel vm)
                {
                    vm.AvailableWidth = args.NewSize.Width;
                }
            };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
