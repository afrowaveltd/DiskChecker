using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.WPF.Services;

/// <summary>
/// Vytváří adaptivní štítky různých velikostí pro tisk informací o disku.
/// Obsah se automaticky přizpůsobuje velikosti štítku.
/// </summary>
public static class DiskLabelPrinterBuilder
{
   /// <summary>
   /// Velikost štítku.
   /// </summary>
   public enum LabelSize
   {
      /// <summary>
      /// Minimální (A10, 105x148mm) - jen QR kod, datum, známka.
      /// </summary>
      Minimal,
      
      /// <summary>
      /// Malý (A9, 74x105mm) - známka, teplota, základní info.
      /// </summary>
      Small,
      
      /// <summary>
      /// Standardní (A7, 74x52mm) - nejčastěji používaný.
      /// </summary>
      Standard,
      
      /// <summary>
      /// Velký (A5, 148x210mm) - detailní info s grafy.
      /// </summary>
      Large,
      
      /// <summary>
      /// Kompletní A4 report.
      /// </summary>
      A4Complete
   }

   private static readonly Dictionary<LabelSize, (double width, double height)> LabelDimensions = new()
   {
      { LabelSize.Minimal, (297.6, 419.5) },   // 105x148mm
      { LabelSize.Small, (209.4, 297.6) },     // 74x105mm
      { LabelSize.Standard, (209.4, 147.4) },  // 74x52mm
      { LabelSize.Large, (419.5, 595.3) },     // 148x210mm
      { LabelSize.A4Complete, (793.7, 1122.5) } // A4
   };

   /// <summary>
   /// Vytvoří label pro tisk s adaptivním obsahem podle velikosti.
   /// </summary>
   public static FixedDocument CreateLabel(
      CoreDriveInfo drive,
      SmartCheckResult smartResult,
      SurfaceTestResult? surfaceResult,
      LabelSize size,
      DateTime testDate)
   {
      var (width, height) = LabelDimensions[size];

      var document = new FixedDocument
      {
         DocumentPaginator = { PageSize = new Size(width, height) }
      };

      var page = new FixedPage
      {
         Width = width,
         Height = height,
         Background = Brushes.White
      };

      var root = size switch
      {
         LabelSize.Minimal => BuildMinimalLabel(drive, smartResult, testDate, width, height),
         LabelSize.Small => BuildSmallLabel(drive, smartResult, testDate, width, height),
         LabelSize.Standard => BuildStandardLabel(drive, smartResult, surfaceResult, testDate, width, height),
         LabelSize.Large => BuildLargeLabel(drive, smartResult, surfaceResult, testDate, width, height),
         _ => BuildA4Label(drive, smartResult, surfaceResult, testDate, width, height)
      };

      FixedPage.SetLeft(root, 0);
      FixedPage.SetTop(root, 0);
      page.Children.Add(root);

      var pageContent = new PageContent();
      ((System.Windows.Markup.IAddChild)pageContent).AddChild(page);
      document.Pages.Add(pageContent);

      return document;
   }

   private static Grid BuildMinimalLabel(CoreDriveInfo drive, SmartCheckResult smart, DateTime testDate, double width, double height)
   {
      var root = new Grid { Width = width, Height = height, Background = Brushes.White };
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

      // Header
      var header = new Border
      {
         Background = new SolidColorBrush(Color.FromRgb(15, 76, 129)),
         Padding = new Thickness(8),
         Child = new TextBlock
         {
            Text = "DiskChecker",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.Bold
         }
      };
      Grid.SetRow(header, 0);
      root.Children.Add(header);

      // Content
      var contentStack = new StackPanel { Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };

      contentStack.Children.Add(new TextBlock
      {
         Text = testDate.ToString("dd.MM.yyyy"),
         FontSize = 14,
         FontWeight = FontWeights.Bold,
         Foreground = new SolidColorBrush(Color.FromRgb(26, 26, 26))
      });

      var gradeBorder = new Border
      {
         Background = GetGradeBrush(smart.Rating.Grade),
         Width = 50,
         Height = 50,
         CornerRadius = new CornerRadius(25),
         Margin = new Thickness(0, 8, 0, 8),
         Child = new TextBlock
         {
            Text = smart.Rating.Grade.ToString(),
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
         }
      };
      contentStack.Children.Add(gradeBorder);

      Grid.SetRow(contentStack, 1);
      root.Children.Add(contentStack);

      // Footer
      var footer = new TextBlock
      {
         Text = $"ID: {drive.Name[..Math.Min(20, drive.Name.Length)]}",
         FontSize = 8,
         Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
         Margin = new Thickness(4)
      };
      Grid.SetRow(footer, 2);
      root.Children.Add(footer);

      return root;
   }

   private static Grid BuildSmallLabel(CoreDriveInfo drive, SmartCheckResult smart, DateTime testDate, double width, double height)
   {
      var root = new Grid { Width = width, Height = height, Background = Brushes.White };
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

      // Header - Border with inner StackPanel
      var headerStack = new StackPanel { Margin = new Thickness(0) };
      var headerBorder = new Border
      {
         Background = new SolidColorBrush(Color.FromRgb(15, 76, 129)),
         Padding = new Thickness(6),
         Child = headerStack
      };

      headerStack.Children.Add(new TextBlock
      {
         Text = drive.Name.Length > 15 ? drive.Name[..15] + "..." : drive.Name,
         Foreground = Brushes.White,
         FontSize = 10,
         FontWeight = FontWeights.Bold
      });
      headerStack.Children.Add(new TextBlock
      {
         Text = testDate.ToString("dd.MM.yyyy"),
         Foreground = new SolidColorBrush(Color.FromRgb(200, 220, 248)),
         FontSize = 8
      });
      
      Grid.SetRow(headerBorder, 0);
      root.Children.Add(headerBorder);

      // Content
      var content = new Grid { Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
      content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

      var gradeBadge = new Border
      {
         Background = GetGradeBrush(smart.Rating.Grade),
         Width = 40,
         Height = 40,
         CornerRadius = new CornerRadius(20),
         Child = new TextBlock
         {
            Text = smart.Rating.Grade.ToString(),
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
         }
      };
      Grid.SetColumn(gradeBadge, 0);
      content.Children.Add(gradeBadge);

      var dataStack = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
      dataStack.Children.Add(new TextBlock
      {
         Text = $"Teplota: {smart.SmartaData.Temperature}°C",
         FontSize = 8,
         Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51))
      });
      dataStack.Children.Add(new TextBlock
      {
         Text = $"Skóre: {smart.Rating.Score:F0}",
         FontSize = 8,
         Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
         Margin = new Thickness(0, 2, 0, 0)
      });

      Grid.SetColumn(dataStack, 1);
      content.Children.Add(dataStack);

      Grid.SetRow(content, 1);
      root.Children.Add(content);

      return root;
   }

   private static Grid BuildStandardLabel(
      CoreDriveInfo drive,
      SmartCheckResult smart,
      SurfaceTestResult? surface,
      DateTime testDate,
      double width,
      double height)
   {
      var root = new Grid { Width = width, Height = height, Background = Brushes.White };
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

      // Header
      var header = new Border
      {
         Background = new SolidColorBrush(Color.FromRgb(15, 76, 129)),
         Padding = new Thickness(4),
         Child = new TextBlock
         {
            Text = $"{drive.Name} | {testDate:dd.MM}",
            Foreground = Brushes.White,
            FontSize = 8,
            FontWeight = FontWeights.Bold
         }
      };
      Grid.SetRow(header, 0);
      root.Children.Add(header);

      // Content - 2 columny
      var contentGrid = new Grid { Margin = new Thickness(4) };
      contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

      // Grade badge
      var gradeBadge = new Border
      {
         Background = GetGradeBrush(smart.Rating.Grade),
         Width = 28,
         Height = 28,
         CornerRadius = new CornerRadius(14),
         Child = new TextBlock
         {
            Text = smart.Rating.Grade.ToString(),
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
         }
      };
      Grid.SetColumn(gradeBadge, 0);
      contentGrid.Children.Add(gradeBadge);

      // Info
      var infoStack = new StackPanel { Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
      infoStack.Children.Add(new TextBlock
      {
         Text = $"Teplota: {smart.SmartaData.Temperature}°C",
         FontSize = 7,
         Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51))
      });
      infoStack.Children.Add(new TextBlock
      {
         Text = $"Čtení: {(surface?.AverageSpeedMbps ?? 0):F0} MB/s",
         FontSize = 7,
         Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
         Margin = new Thickness(0, 1, 0, 0)
      });

      Grid.SetColumn(infoStack, 1);
      contentGrid.Children.Add(infoStack);

      Grid.SetRow(contentGrid, 1);
      root.Children.Add(contentGrid);

      return root;
   }

   private static Grid BuildLargeLabel(
      CoreDriveInfo drive,
      SmartCheckResult smart,
      SurfaceTestResult? surface,
      DateTime testDate,
      double width,
      double height)
   {
      // Prozatím stejný jako standard, bude se rozšiřovat
      return BuildStandardLabel(drive, smart, surface, testDate, width, height);
   }

   private static Grid BuildA4Label(
      CoreDriveInfo drive,
      SmartCheckResult smart,
      SurfaceTestResult? surface,
      DateTime testDate,
      double width,
      double height)
   {
      // A4 je kompletní report - prozatím stejný jako large
      return BuildLargeLabel(drive, smart, surface, testDate, width, height);
   }

   private static Brush GetGradeBrush(QualityGrade grade)
   {
      return grade switch
      {
         QualityGrade.A => new SolidColorBrush(Color.FromRgb(40, 167, 69)),    // Green
         QualityGrade.B => new SolidColorBrush(Color.FromRgb(23, 162, 184)),   // Blue
         QualityGrade.C => new SolidColorBrush(Color.FromRgb(255, 193, 7)),    // Yellow
         QualityGrade.D => new SolidColorBrush(Color.FromRgb(253, 126, 20)),   // Orange
         QualityGrade.E => new SolidColorBrush(Color.FromRgb(220, 53, 69)),    // Red
         QualityGrade.F => new SolidColorBrush(Color.FromRgb(114, 28, 36)),    // Dark Red
         _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))                // Gray
      };
   }
}
