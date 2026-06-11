using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;

namespace PhotoNote;

public static class ImageLoader
{
    public static readonly string[] Extensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".heic" };

    public static bool IsSupported(string path)
        => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// 画像を読み込む。EXIFの回転情報を反映し、撮影日時(なければファイル更新日時)も返す。
    /// 返すビットマップは Freeze 済みなのでどのスレッドからでも使える。
    /// </summary>
    public static (BitmapSource? Image, DateTime? DateTaken) Load(string path, int decodePixelWidth)
    {
        var (orientation, dateTaken) = ReadMetadata(path);
        BitmapSource? bmp = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodePixelWidth;
            image.Rotation = orientation switch
            {
                3 => Rotation.Rotate180,
                6 => Rotation.Rotate90,
                8 => Rotation.Rotate270,
                _ => Rotation.Rotate0,
            };
            image.EndInit();
            image.Freeze();
            bmp = image;
        }
        catch
        {
            // 読めない画像(壊れたファイル・未対応形式)はサムネイルなしで続行
        }
        return (bmp, dateTaken);
    }

    /// <summary>撮影日時だけを読む(並べ替え用。画像本体はデコードしない)。</summary>
    public static DateTime? ReadDateTaken(string path) => ReadMetadata(path).DateTaken;

    /// <summary>EXIFの回転フラグと撮影日時だけを読む(画像本体はデコードしない)。</summary>
    private static (ushort Orientation, DateTime? DateTaken) ReadMetadata(string path)
    {
        ushort orientation = 1;
        DateTime? date = null;
        try
        {
            using var fs = File.OpenRead(path);
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (frame.Metadata is BitmapMetadata meta)
            {
                try
                {
                    if (!string.IsNullOrEmpty(meta.DateTaken)
                        && (DateTime.TryParse(meta.DateTaken, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)
                            || DateTime.TryParse(meta.DateTaken, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)))
                        date = d;
                }
                catch { }
                try
                {
                    if (meta.GetQuery("System.Photo.Orientation") is ushort o) orientation = o;
                }
                catch { }
            }
        }
        catch { }

        // 撮影日時が取れないときはファイルの更新日時で代用する
        date ??= File.GetLastWriteTime(path);
        return (orientation, date);
    }
}
