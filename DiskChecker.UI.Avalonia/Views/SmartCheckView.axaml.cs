using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DiskChecker.UI.Avalonia.Views
{
    public partial class SmartCheckView : UserControl
    {
        public SmartCheckView()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
