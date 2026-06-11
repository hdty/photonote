using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoNote;

/// <summary>
/// 選んだ写真から印刷用の FixedDocument を組み立てる。
/// 各写真の下に、左にメモ、右に撮影日を入れる(photonotesample01.pdf のレイアウト)。
/// </summary>
public static class PrintDocumentBuilder
{
    private const double MmToDiu = 96.0 / 25.4;
    private const double PtToDiu = 96.0 / 72.0;
    private static readonly FontFamily CaptionFont = new("Meiryo UI");

    /// <summary>B5(182×257mm)以上の用紙かどうか。余白と文字サイズの基準にする。</summary>
    private static bool IsLargePaper(double paperWidthMm, double paperHeightMm)
        => Math.Min(paperWidthMm, paperHeightMm) >= 182;

    public static FixedDocument Build(
        IReadOnlyList<PhotoItem> photos,
        PrintSettings settings,
        IReadOnlyDictionary<string, BitmapSource?> images,
        double paperWidthMm, double paperHeightMm)
    {
        var landscape = settings.Orientation == "Landscape";
        var pageWidth = (landscape ? paperHeightMm : paperWidthMm) * MmToDiu;
        var pageHeight = (landscape ? paperWidthMm : paperHeightMm) * MmToDiu;
        // 紙端の余白: B5以上=10mm、B5未満(L判・2L判・はがき)=5mm。
        // フチあり印刷の必要余白は EPSON EW-M873T で四辺3mm、
        // Canon PIXUS TS8630(A4) で左右5mm・上下3.4mm なので、5mmでも主要機種でほぼ欠けない
        var large = IsLargePaper(paperWidthMm, paperHeightMm);
        var margin = (large ? 10 : 5) * MmToDiu;
        // 文字サイズ固定: B5以上=10pt、B5未満=8pt(1ページの枚数では変えない)
        var fontSize = (large ? 10 : 8) * PtToDiu;

        var (rows, cols) = settings.PhotosPerPage switch
        {
            1 => (1, 1),
            4 => (2, 2),
            _ => settings.Layout == "Horizontal" ? (1, 2) : (2, 1),
        };
        var perPage = rows * cols;

        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

        for (var start = 0; start < photos.Count; start += perPage)
        {
            var page = new FixedPage { Width = pageWidth, Height = pageHeight, Background = Brushes.White };

            var grid = new Grid
            {
                Width = pageWidth - margin * 2,
                Height = pageHeight - margin * 2,
            };
            for (var r = 0; r < rows; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            for (var c = 0; c < cols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cellWidth = grid.Width / cols;
            var cellHeight = grid.Height / rows;
            for (var i = 0; i < perPage && start + i < photos.Count; i++)
            {
                var cell = BuildCell(photos[start + i], images, cellWidth, cellHeight, fontSize);
                Grid.SetRow(cell, i / cols);
                Grid.SetColumn(cell, i % cols);
                grid.Children.Add(cell);
            }

            FixedPage.SetLeft(grid, margin);
            FixedPage.SetTop(grid, margin);
            page.Children.Add(grid);

            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            doc.Pages.Add(content);
        }

        return doc;
    }

    private static FrameworkElement BuildCell(
        PhotoItem photo,
        IReadOnlyDictionary<string, BitmapSource?> images,
        double cellWidth, double cellHeight, double fontSize)
    {
        var spacing = fontSize * 0.5;
        var captionHeight = fontSize * 1.5;

        // 写真とキャプションをひとかたまりにして、セルの中央に置く
        var block = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(spacing),
        };

        // キャプションは写真のすぐ下に置く(間隔は文字サイズの2割ほど)
        var captionGap = fontSize * 0.2;
        var maxImageWidth = cellWidth - spacing * 2;
        var maxImageHeight = cellHeight - spacing * 2 - captionGap - captionHeight;
        var blockWidth = maxImageWidth;

        images.TryGetValue(photo.FullPath, out var bitmap);
        if (bitmap != null)
        {
            // 写真の表示サイズを先に計算し、キャプションの幅を写真の端に揃える
            var scale = Math.Min(maxImageWidth / bitmap.PixelWidth, maxImageHeight / bitmap.PixelHeight);
            blockWidth = bitmap.PixelWidth * scale;
            var image = new Image
            {
                Source = bitmap,
                Width = blockWidth,
                Height = bitmap.PixelHeight * scale,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            block.Children.Add(image);
        }

        var caption = new Grid { Width = blockWidth, Margin = new Thickness(0, captionGap, 0, 0) };
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = photo.Text,
            FontFamily = CaptionFont,
            FontSize = fontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 0);
        caption.Children.Add(text);

        var date = new TextBlock
        {
            Text = photo.DateText,
            FontFamily = CaptionFont,
            FontSize = fontSize,
            Margin = new Thickness(fontSize, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(date, 1);
        caption.Children.Add(date);

        block.Children.Add(caption);
        return block;
    }
}
