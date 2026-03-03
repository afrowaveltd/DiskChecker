using System.Windows;

namespace DiskChecker.UI.WPF.Views;

/// <summary>
/// Dialog pro výběr velikosti štítku pro tisk.
/// </summary>
public partial class LabelPrinterDialog : Window
{
   /// <summary>
   /// Inicializuje novou instanci dialogu.
   /// </summary>
   public LabelPrinterDialog()
   {
      InitializeComponent();
   }

   /// <summary>
   /// Vrátí vybranou velikost štítku.
   /// </summary>
   public DiskChecker.UI.WPF.Services.DiskLabelPrinterBuilder.LabelSize GetSelectedSize()
   {
      if (rbMinimal.IsChecked == true) return DiskChecker.UI.WPF.Services.DiskLabelPrinterBuilder.LabelSize.Minimal;
      if (rbSmall.IsChecked == true) return DiskChecker.UI.WPF.Services.DiskLabelPrinterBuilder.LabelSize.Small;
      if (rbLarge.IsChecked == true) return DiskChecker.UI.WPF.Services.DiskLabelPrinterBuilder.LabelSize.Large;
      if (rbA4.IsChecked == true) return DiskChecker.UI.WPF.Services.DiskLabelPrinterBuilder.LabelSize.A4Complete;
      return DiskChecker.UI.WPF.Services.DiskLabelPrinterBuilder.LabelSize.Standard;
   }

   private void PrintButton_Click(object sender, RoutedEventArgs e)
   {
      DialogResult = true;
      Close();
   }

   private void CancelButton_Click(object sender, RoutedEventArgs e)
   {
      DialogResult = false;
      Close();
   }
}
