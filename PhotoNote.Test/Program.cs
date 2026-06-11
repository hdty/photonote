using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoNote;

const string folder = @"C:\Users\hdty\projects\photonote\testphotos";
const string outDir = @"C:\Users\hdty\projects\photonote\testout";
Directory.CreateDirectory(outDir);
var failures = 0;

void Check(bool cond, string name)
{
    Console.WriteLine($"{(cond ? "OK " : "NG ")} {name}");
    if (!cond) failures++;
}

// --- 1. NoteFile 保存・読込・隠し属性・上書き ---
var notePath = Path.Combine(folder, NoteFile.FileName);
if (File.Exists(notePath)) { File.SetAttributes(notePath, FileAttributes.Normal); File.Delete(notePath); }

var nf = new NoteFile();
nf.PrintSettings.PaperSize = "A4";
nf.PrintSettings.PhotosPerPage = 2;
nf.Photos.Add(new PhotoEntry { File = "IMG_0001.jpg", Text = "入場シーン", Selected = true });
nf.Photos.Add(new PhotoEntry { File = "IMG_0002.jpg", Text = "フェアプレイフラッグ掲出", Selected = true });
nf.Save(folder);

Check(File.Exists(notePath), "JSONファイルが作成される");
Check(File.GetAttributes(notePath).HasFlag(FileAttributes.Hidden), "隠し属性が付く");

var loaded = NoteFile.Load(folder);
Check(loaded.Photos.Count == 2 && loaded.Photos[0].Text == "入場シーン", "読込でメモが復元される");
Check(loaded.PrintSettings.PhotosPerPage == 2, "印刷設定が復元される");

loaded.Photos[0].Text = "入場シーン(修正)";
loaded.Save(folder); // 隠し属性付きのままの上書き保存
Check(NoteFile.Load(folder).Photos[0].Text == "入場シーン(修正)", "隠し属性のまま上書きできる");
Console.WriteLine(File.ReadAllText(notePath));

// --- 2. 画像読み込みと撮影日 ---
var files = Directory.GetFiles(folder, "*.jpg").OrderBy(f => f).ToList();
var items = new List<PhotoItem>();
var images = new Dictionary<string, BitmapSource?>();
var captions = new[] { "入場シーン", "フェアプレイフラッグ掲出", "いよいよピッチに(縦写真)", "", "とても長い説明文はどうなるかのテスト。あいうえおかきくけこさしすせそたちつてとなにぬねの" };
for (var i = 0; i < files.Count; i++)
{
    var item = new PhotoItem(files[i]) { Text = captions[i], Selected = true };
    var (img, date) = ImageLoader.Load(files[i], 1600);
    item.DateTaken = date;
    images[files[i]] = img;
    items.Add(item);
    Check(img != null, $"画像読込 {Path.GetFileName(files[i])}");
}
Check(items[0].DateText == "2025/5/7", $"撮影日の表記 ({items[0].DateText})");

// --- 3. 印刷ページの組み立てとPNG描画 ---
void Render(string name, PrintSettings s, double wMm, double hMm, int expectedPages)
{
    var doc = PrintDocumentBuilder.Build(items, s, images, wMm, hMm);
    Check(doc.Pages.Count == expectedPages, $"{name}: ページ数 {doc.Pages.Count} (期待 {expectedPages})");
    for (var p = 0; p < doc.Pages.Count; p++)
    {
        var page = doc.Pages[p].Child;
        var size = new Size(page.Width, page.Height);
        page.Measure(size);
        page.Arrange(new Rect(size));
        page.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)page.Width, (int)page.Height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(outDir, $"{name}_p{p + 1}.png"));
        encoder.Save(fs);
    }
}

var t = new Thread(() =>
{
    Render("A4_tate_2up", new PrintSettings { PaperSize = "A4", Orientation = "Portrait", PhotosPerPage = 2, Layout = "Vertical" }, 210, 297, 3);
    Render("A4_yoko_4up", new PrintSettings { PaperSize = "A4", Orientation = "Landscape", PhotosPerPage = 4 }, 210, 297, 2);
    Render("L_yoko_1up", new PrintSettings { PaperSize = "L", Orientation = "Landscape", PhotosPerPage = 1 }, 89, 127, 5);
    Render("A4_tate_2up_yokonarabe", new PrintSettings { PaperSize = "A4", Orientation = "Portrait", PhotosPerPage = 2, Layout = "Horizontal" }, 210, 297, 3);
});
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();

Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : $"{failures} TEST(S) FAILED");
Environment.Exit(failures == 0 ? 0 : 1);
