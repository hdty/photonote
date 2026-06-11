using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PhotoNote;

public partial class MainWindow : Window
{
    private const int PhotosPerScreen = 8; // 2列×4行

    private readonly ObservableCollection<PhotoItem> _photos = new();
    private string? _currentFolder;
    private NoteFile _noteFile = new();
    private bool _dirty;
    private bool _sortReady;
    private int _pageIndex;
    private readonly DispatcherTimer _saveTimer;
    private CancellationTokenSource? _loadCts;

    // 印刷画面の状態
    private List<PhotoItem> _printPhotos = new();
    private readonly Dictionary<string, BitmapSource?> _printImages = new();
    private bool _printViewReady;
    private bool _printImagesLoaded;

    private record PaperDef(string Id, string Label, double WidthMm, double HeightMm, PageMediaSizeName? MediaName);

    private static readonly PaperDef[] Papers =
    {
        new("A4",     "A4 (210×297mm)",      210, 297, PageMediaSizeName.ISOA4),
        new("B5",     "B5 (182×257mm)",      182, 257, PageMediaSizeName.JISB5),
        new("2L",     "2L判 (127×178mm)",    127, 178, null),
        new("L",      "L判 (89×127mm)",       89, 127, null),
        new("Hagaki", "はがき (100×148mm)",  100, 148, PageMediaSizeName.JapanHagakiPostcard),
    };

    // 撮影日時・更新日時は時刻まで含めて比較する
    private static readonly (string Id, string Label)[] SortOrders =
    {
        ("DateTakenAsc",  "撮影日時(古い順)"),
        ("DateTakenDesc", "撮影日時(新しい順)"),
        ("FileNameAsc",   "ファイル名(昇順)"),
        ("FileNameDesc",  "ファイル名(降順)"),
        ("ModifiedAsc",   "更新日時(古い順)"),
        ("ModifiedDesc",  "更新日時(新しい順)"),
    };

    public MainWindow()
    {
        InitializeComponent();

        foreach (var p in Papers)
            PaperCombo.Items.Add(new ComboBoxItem { Content = p.Label, Tag = p.Id });
        foreach (var (_, label) in SortOrders)
            SortCombo.Items.Add(label);

        // 入力のたびに書き込むのではなく、手が止まってから保存する
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNotes(); };
    }

    // ===================== 写真選び画面 =====================

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "写真の入っているフォルダを選んでください",
        };
        if (dialog.ShowDialog(this) == true)
        {
            await LoadFolderAsync(dialog.FolderName);
        }
    }

    private async Task LoadFolderAsync(string folder)
    {
        SaveNotes(); // 前のフォルダの編集内容を保存してから移る
        _loadCts?.Cancel();
        _printImages.Clear();

        _currentFolder = folder;
        _noteFile = NoteFile.Load(folder);
        _photos.Clear();

        var files = Directory.EnumerateFiles(folder)
            .Where(ImageLoader.IsSupported)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            var item = new PhotoItem(file);
            var entry = _noteFile.Photos.FirstOrDefault(
                p => string.Equals(p.File, item.FileName, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                item.Text = entry.Text;
                item.Selected = entry.Selected;
            }
            item.PropertyChanged += Photo_PropertyChanged;
            _photos.Add(item);
        }

        FolderLabel.Text = folder;
        SelectAllButton.IsEnabled = SelectNoneButton.IsEnabled = _photos.Count > 0;
        StatusLabel.Text = "写真の情報を読み込んでいます…";

        _sortReady = false;
        var sortIndex = Array.FindIndex(SortOrders, s => s.Id == _noteFile.SortOrder);
        SortCombo.SelectedIndex = sortIndex >= 0 ? sortIndex : 0;
        SortCombo.IsEnabled = _photos.Count > 0;
        _sortReady = true;

        var cts = new CancellationTokenSource();
        _loadCts = cts;

        // 並べ替えに使う撮影日を先に読み込む(画像本体より先、軽い処理)
        var items = _photos.ToList();
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (cts.IsCancellationRequested) return;
                item.DateTaken = ImageLoader.ReadDateTaken(item.FullPath) ?? item.ModifiedTime;
            }
        });
        if (cts.IsCancellationRequested) return;

        ApplySort();
        _pageIndex = 0;
        RefreshPage();
        UpdateStatus();
        _dirty = false;

        _ = LoadThumbnailsAsync(_photos.ToList(), cts.Token);
    }

    /// <summary>_photos を現在の並び順設定で並べ替える。</summary>
    private void ApplySort()
    {
        // まずファイル名順にしておき、安定ソートで第2キーとして効かせる
        var items = _photos.OrderBy(p => p.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        List<PhotoItem> sorted = _noteFile.SortOrder switch
        {
            "DateTakenDesc" => items.OrderByDescending(p => p.DateTaken ?? p.ModifiedTime).ToList(),
            "FileNameAsc"   => items,
            "FileNameDesc"  => Enumerable.Reverse(items).ToList(),
            "ModifiedAsc"   => items.OrderBy(p => p.ModifiedTime).ToList(),
            "ModifiedDesc"  => items.OrderByDescending(p => p.ModifiedTime).ToList(),
            _               => items.OrderBy(p => p.DateTaken ?? p.ModifiedTime).ToList(),
        };
        _photos.Clear();
        foreach (var p in sorted) _photos.Add(p);
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_sortReady || _currentFolder == null || SortCombo.SelectedIndex < 0) return;
        _noteFile.SortOrder = SortOrders[SortCombo.SelectedIndex].Id;
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
        ApplySort();
        _pageIndex = 0;
        RefreshPage();
    }

    private int PageCount => Math.Max(1, (_photos.Count + PhotosPerScreen - 1) / PhotosPerScreen);

    private void RefreshPage()
    {
        PhotoGrid.ItemsSource = _photos.Skip(_pageIndex * PhotosPerScreen).Take(PhotosPerScreen).ToList();
        PageLabel.Text = _photos.Count == 0 ? "" : $"{_pageIndex + 1} / {PageCount} ページ";
        PrevPageButton.IsEnabled = _pageIndex > 0;
        NextPageButton.IsEnabled = _pageIndex < PageCount - 1;
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex > 0) { _pageIndex--; RefreshPage(); }
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex < PageCount - 1) { _pageIndex++; RefreshPage(); }
    }

    private async Task LoadThumbnailsAsync(List<PhotoItem> items, CancellationToken ct)
    {
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested) return;
            var (image, _) = await Task.Run(() => ImageLoader.Load(item.FullPath, 320), ct);
            if (ct.IsCancellationRequested) return;
            item.Thumbnail = image;
        }
    }

    private void Photo_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PhotoItem.Text) or nameof(PhotoItem.Selected))
        {
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
            if (e.PropertyName == nameof(PhotoItem.Selected)) UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        var selected = _photos.Count(p => p.Selected);
        StatusLabel.Text = _photos.Count == 0
            ? "写真がありません"
            : $"写真 {_photos.Count} 枚　/　印刷する写真 {selected} 枚";
        PrintButton.IsEnabled = selected > 0;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _photos) p.Selected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _photos) p.Selected = false;
    }

    /// <summary>編集内容を .photonote.json に保存する。</summary>
    private void SaveNotes(bool force = false)
    {
        if (_currentFolder == null) return;
        if (!_dirty && !force) return;

        // フォルダから消えた写真のメモも捨てずに残しておく
        var known = new HashSet<string>(_photos.Select(p => p.FileName), StringComparer.OrdinalIgnoreCase);
        var orphans = _noteFile.Photos
            .Where(p => !known.Contains(p.File) && !string.IsNullOrEmpty(p.Text))
            .ToList();

        _noteFile.Photos = _photos
            .Select(p => new PhotoEntry { File = p.FileName, Text = p.Text, Selected = p.Selected })
            .Concat(orphans)
            .ToList();

        try
        {
            _noteFile.Save(_currentFolder);
            _dirty = false;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"保存できませんでした: {ex.Message}";
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _saveTimer.Stop();
        SaveNotes(force: true);
        _loadCts?.Cancel();
    }

    // ===================== 開発用: 画面キャプチャ =====================

    /// <summary>--uishot 用。フォルダを開いて両画面をPNGに保存し、終了する。</summary>
    internal async Task RunUiShotAsync(string folder, string outDir)
    {
        Directory.CreateDirectory(outDir);
        await LoadFolderAsync(folder);
        foreach (var p in _photos.Take(3)) p.Selected = true;
        await Task.Delay(2500); // サムネイル読み込み待ち
        CaptureWindow(Path.Combine(outDir, "edit_view.png"));
        Print_Click(this, new RoutedEventArgs());
        await Task.Delay(3000); // 印刷用画像とプレビューの生成待ち
        CaptureWindow(Path.Combine(outDir, "print_view.png"));
        Application.Current.Shutdown();
    }

    private void CaptureWindow(string path)
    {
        var rtb = new RenderTargetBitmap(
            (int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    // ===================== 印刷画面 =====================

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        // 一覧の並び順のまま印刷する
        _printPhotos = _photos.Where(p => p.Selected).ToList();
        if (_printPhotos.Count == 0)
        {
            MessageBox.Show(this, "印刷する写真にチェックを付けてください。", "PhotoNote",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SaveNotes();

        InitPrintSettingsUi();
        EditView.Visibility = Visibility.Collapsed;
        PrintView.Visibility = Visibility.Visible;

        // 表示されてテンプレートが組み上がってから、不要な標準UIを隠す
        await Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, TunePreviewChrome);

        await LoadPrintImagesAsync();
        UpdatePreview();
    }

    /// <summary>
    /// プレビュー(DocumentViewer)の標準UIを調整する:
    /// 下部の文字列検索バーと上部ツールバーの印刷・コピーボタンを隠し、
    /// 残ったボタンのツールチップを日本語にする。
    /// </summary>
    private void TunePreviewChrome()
    {
        Preview.ApplyTemplate();
        if (Preview.Template?.FindName("PART_FindToolBarHost", Preview) is FrameworkElement find)
            find.Visibility = Visibility.Collapsed;
        TunePreviewToolbar(Preview);
    }

    private static void TunePreviewToolbar(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Button button)
            {
                if (button.Command == ApplicationCommands.Print || button.Command == ApplicationCommands.Copy)
                    button.Visibility = Visibility.Collapsed;
                else if (button.Command == NavigationCommands.IncreaseZoom)
                    button.ToolTip = "拡大";
                else if (button.Command == NavigationCommands.DecreaseZoom)
                    button.ToolTip = "縮小";
                else if (button.Command == NavigationCommands.Zoom)
                    button.ToolTip = "100%で表示";
                else if (button.Command == DocumentViewer.FitToWidthCommand)
                    button.ToolTip = "ページの幅に合わせる";
                else if (button.Command == DocumentViewer.FitToMaxPagesAcrossCommand)
                    button.ToolTip = button.CommandParameter?.ToString() == "2"
                        ? "見開きで表示" : "ページ全体を表示";
            }
            else if (child is Separator separator)
            {
                // 印刷・コピーの両脇にある区切り線も一緒に隠す(先頭に区切りが残るのを防ぐ)
                separator.Visibility = Visibility.Collapsed;
            }
            else
            {
                TunePreviewToolbar(child);
            }
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        PrintView.Visibility = Visibility.Collapsed;
        EditView.Visibility = Visibility.Visible;
        SaveNotes(force: true); // 印刷設定の変更も保存する
    }

    /// <summary>保存されている印刷設定を画面に反映する。</summary>
    private void InitPrintSettingsUi()
    {
        _printViewReady = false;
        var s = _noteFile.PrintSettings;

        var paperIndex = Array.FindIndex(Papers, p => p.Id == s.PaperSize);
        PaperCombo.SelectedIndex = paperIndex >= 0 ? paperIndex : 0;
        if (s.Orientation == "Landscape") LandscapeRadio.IsChecked = true;
        else PortraitRadio.IsChecked = true;
        switch (s.PhotosPerPage)
        {
            case 1: Per1Radio.IsChecked = true; break;
            case 4: Per4Radio.IsChecked = true; break;
            default: Per2Radio.IsChecked = true; break;
        }
        if (s.Layout == "Horizontal") HorizontalRadio.IsChecked = true;
        else VerticalRadio.IsChecked = true;

        _printViewReady = true;
    }

    private async Task LoadPrintImagesAsync()
    {
        _printImagesLoaded = false;
        PrintNowButton.IsEnabled = false;
        PageCountLabel.Text = "写真を読み込んでいます…";
        foreach (var photo in _printPhotos)
        {
            if (_printImages.ContainsKey(photo.FullPath)) continue;
            var path = photo.FullPath;
            var (image, _) = await Task.Run(() => ImageLoader.Load(path, 1600));
            _printImages[path] = image;
        }
        _printImagesLoaded = true;
        PrintNowButton.IsEnabled = true;
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_printViewReady) return;
        ReadSettingsFromUi();
        UpdatePreview();
    }

    private void ReadSettingsFromUi()
    {
        var s = _noteFile.PrintSettings;
        s.PaperSize = (PaperCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "A4";
        s.Orientation = LandscapeRadio.IsChecked == true ? "Landscape" : "Portrait";
        s.PhotosPerPage = Per1Radio.IsChecked == true ? 1
                        : Per4Radio.IsChecked == true ? 4 : 2;
        s.Layout = HorizontalRadio.IsChecked == true ? "Horizontal" : "Vertical";

        // 並べ方の選択は2枚のときだけ意味がある
        var layoutEnabled = s.PhotosPerPage == 2;
        LayoutPanel.IsEnabled = layoutEnabled;
        LayoutLabel.Opacity = LayoutPanel.Opacity = layoutEnabled ? 1.0 : 0.4;
    }

    private void UpdatePreview()
    {
        if (!_printImagesLoaded) return;
        var s = _noteFile.PrintSettings;
        var paper = Papers.First(p => p.Id == s.PaperSize);
        var doc = PrintDocumentBuilder.Build(_printPhotos, s, _printImages, paper.WidthMm, paper.HeightMm);
        Preview.Document = doc;
        Preview.FitToMaxPagesAcross(1); // 既定は「ページ全体を表示」
        PageCountLabel.Text = $"全 {doc.Pages.Count} ページ";
    }

    private void PrintNow_Click(object sender, RoutedEventArgs e)
    {
        if (!_printImagesLoaded) return;
        ReadSettingsFromUi();
        var s = _noteFile.PrintSettings;
        var paper = Papers.First(p => p.Id == s.PaperSize);

        // プリンタドライバの印刷ダイアログを開く(画質設定や用紙種類はここで選べる)
        var dialog = new PrintDialog();
        try
        {
            var ticket = dialog.PrintTicket;
            ticket.PageOrientation = s.Orientation == "Landscape"
                ? PageOrientation.Landscape
                : PageOrientation.Portrait;
            // PageMediaSize は縦置きの寸法で指定する(1mm = 96/25.4 DIU)
            const double mm = 96.0 / 25.4;
            ticket.PageMediaSize = paper.MediaName is { } name
                ? new PageMediaSize(name, paper.WidthMm * mm, paper.HeightMm * mm)
                : new PageMediaSize(paper.WidthMm * mm, paper.HeightMm * mm);
        }
        catch
        {
            // プリンタによっては設定できないことがあるが、その場合はダイアログで選んでもらう
        }

        if (dialog.ShowDialog() != true) return;

        try
        {
            var doc = PrintDocumentBuilder.Build(_printPhotos, s, _printImages, paper.WidthMm, paper.HeightMm);
            dialog.PrintDocument(doc.DocumentPaginator, "PhotoNote");
            PageCountLabel.Text = "プリンタに送りました";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"印刷できませんでした。\n{ex.Message}", "PhotoNote",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
