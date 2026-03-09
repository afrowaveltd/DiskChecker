using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.WPF.Services;

internal static class SurfaceTestCertificateDocumentBuilder
{
    private const double A4Width = 793.7;
    private const double A4Height = 1122.5;

    internal static FixedDocument CreateDocument(CoreDriveInfo drive, SurfaceTestResult surface, SmartCheckResult smart)
    {
        var document = new FixedDocument
        {
            DocumentPaginator = { PageSize = new Size(A4Width, A4Height) }
        };

        var page = new FixedPage
        {
            Width = A4Width,
            Height = A4Height,
            Background = Brushes.White
        };

        var root = BuildLayout(drive, surface, smart);
        FixedPage.SetLeft(root, 36);
        FixedPage.SetTop(root, 36);
        page.Children.Add(root);

        var pageContent = new PageContent();
        ((System.Windows.Markup.IAddChild)pageContent).AddChild(page);
        document.Pages.Add(pageContent);
        return document;
    }

    private static Grid BuildLayout(CoreDriveInfo drive, SurfaceTestResult surface, SmartCheckResult smart)
    {
        var root = new Grid
        {
            Width = A4Width - 72,
            Height = A4Height - 72,
            Background = new SolidColorBrush(Color.FromRgb(252, 252, 252))
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 76, 129)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "DiskChecker certifikát povrchového testu",
            Foreground = Brushes.White,
            FontSize = 26,
            FontWeight = FontWeights.Bold
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"Disk: {drive.Name} ({drive.Path})",
            Foreground = new SolidColorBrush(Color.FromRgb(220, 235, 248)),
            FontSize = 13,
            Margin = new Thickness(0, 4, 0, 0)
        });
        headerGrid.Children.Add(titleStack);

        var gradeBadge = new Border
        {
            Background = GetGradeBrush(smart.Rating.Grade),
            CornerRadius = new CornerRadius(50),
            Width = 100,
            Height = 100,
            Child = new TextBlock
            {
                Text = smart.Rating.Grade.ToString(),
                Foreground = Brushes.White,
                FontSize = 44,
                FontWeight = FontWeights.ExtraBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };

        Grid.SetColumn(gradeBadge, 1);
        headerGrid.Children.Add(gradeBadge);
        header.Child = headerGrid;
        root.Children.Add(header);

        var qualityLine = new TextBlock
        {
            Text = $"Skóre kvality: {smart.Rating.Score:F1} / 100    |    Dokončeno: {surface.CompletedAtUtc:dd.MM.yyyy HH:mm}",
            Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(qualityLine, 1);
        root.Children.Add(qualityLine);

        var smartTable = BuildSmartTable(smart.SmartaData);
        Grid.SetRow(smartTable, 2);
        root.Children.Add(smartTable);

        var graphCard = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 12, 0, 0)
        };

        var graphLayout = new Grid();
        graphLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        graphLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        graphLayout.Children.Add(new TextBlock
        {
            Text = "Graf průběhu přepisu celého disku (MB/s)",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var graphCanvas = BuildGraph(surface);
        Grid.SetRow(graphCanvas, 1);
        graphLayout.Children.Add(graphCanvas);
        graphCard.Child = graphLayout;

        Grid.SetRow(graphCard, 3);
        root.Children.Add(graphCard);

        var footer = new TextBlock
        {
            Text = "DiskChecker • Certifikát je určen pro interní evidenci testů disků",
            Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        return root;
    }

    private static Border BuildSmartTable(SmartaData data)
    {
        var tableBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            Padding = new Thickness(12)
        };

        var grid = new Grid();
        for (var i = 0; i < 5; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddCell(grid, 0, 0, "Model", true);
        AddCell(grid, 0, 1, data.DeviceModel ?? "N/A", false);
        AddCell(grid, 0, 2, "Firmware", true);
        AddCell(grid, 0, 3, data.FirmwareVersion ?? "N/A", false);

        AddCell(grid, 1, 0, "Sériové číslo", true);
        AddCell(grid, 1, 1, data.SerialNumber ?? "N/A", false);
        AddCell(grid, 1, 2, "Teplota", true);
        AddCell(grid, 1, 3, $"{data.Temperature:F1} °C", false);

        AddCell(grid, 2, 0, "Provozní hodiny", true);
        AddCell(grid, 2, 1, data.PowerOnHours?.ToString() ?? "N/A", false);
        AddCell(grid, 2, 2, "Přemapované sektory", true);
        AddCell(grid, 2, 3, data.ReallocatedSectorCount.ToString(), false);

        AddCell(grid, 3, 0, "Čekající sektory", true);
        AddCell(grid, 3, 1, data.PendingSectorCount.ToString(), false);
        AddCell(grid, 3, 2, "Neopravitelné chyby", true);
        AddCell(grid, 3, 3, data.UncorrectableErrorCount.ToString(), false);

        AddCell(grid, 4, 0, "Wear leveling", true);
        AddCell(grid, 4, 1, data.WearLevelingCount?.ToString() ?? "N/A", false);

        tableBorder.Child = grid;
        return tableBorder;
    }

    private static Canvas BuildGraph(SurfaceTestResult surface)
    {
        var canvas = new Canvas
        {
            Height = 320,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
        };

        var samples = surface.Samples;
        if (samples.Count < 2)
        {
            canvas.Children.Add(new TextBlock
            {
                Text = "Data grafu nejsou k dispozici.",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 14
            });
            return canvas;
        }

        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            StrokeThickness = 2.4
        };

        var maxY = samples.Max(s => s.ThroughputMbps);
        if (maxY <= 0)
        {
            maxY = 1;
        }

        const double margin = 20;
        var width = 640d;
        var height = 280d;

        for (var i = 0; i < samples.Count; i++)
        {
            var x = margin + (width * i / (samples.Count - 1));
            var y = margin + height - (samples[i].ThroughputMbps / maxY * height);
            polyline.Points.Add(new Point(x, y));
        }

        canvas.Width = width + margin * 2;
        canvas.Height = height + margin * 2;

        canvas.Children.Add(new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
            StrokeThickness = 1
        });
        Canvas.SetLeft(canvas.Children[^1], margin);
        Canvas.SetTop(canvas.Children[^1], margin);

        canvas.Children.Add(polyline);

        canvas.Children.Add(new TextBlock
        {
            Text = $"MAX: {maxY:F1} MB/s",
            Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            FontSize = 12
        });
        Canvas.SetLeft(canvas.Children[^1], margin + 4);
        Canvas.SetTop(canvas.Children[^1], 2);

        return canvas;
    }

    private static void AddCell(Grid grid, int row, int column, string text, bool isLabel)
    {
        var tb = new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 2, 10, 6),
            FontSize = 12,
            FontWeight = isLabel ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isLabel ? new SolidColorBrush(Color.FromRgb(70, 70, 70)) : new SolidColorBrush(Color.FromRgb(35, 35, 35))
        };

        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private static Brush GetGradeBrush(QualityGrade grade) => grade switch
    {
        QualityGrade.A => new SolidColorBrush(Color.FromRgb(40, 167, 69)),
        QualityGrade.B => new SolidColorBrush(Color.FromRgb(90, 179, 87)),
        QualityGrade.C => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
        _ => new SolidColorBrush(Color.FromRgb(220, 53, 69))
    };
}
