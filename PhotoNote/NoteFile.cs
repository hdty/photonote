using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoNote;

/// <summary>
/// フォルダごとに保存する隠しファイル「.photonote.json」の中身。
/// 印刷設定と、写真ファイル名ごとのメモを保持する。
/// </summary>
public class NoteFile
{
    public string App { get; set; } = "PhotoNote";
    public int Version { get; set; } = 1;
    public string? UpdatedAt { get; set; }
    /// <summary>
    /// 写真の並び順: DateTakenAsc / DateTakenDesc / FileNameAsc / FileNameDesc / ModifiedAsc / ModifiedDesc
    /// </summary>
    public string SortOrder { get; set; } = "DateTakenAsc";
    public PrintSettings PrintSettings { get; set; } = new();
    public List<PhotoEntry> Photos { get; set; } = new();

    public const string FileName = ".photonote.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static NoteFile Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return new NoteFile();
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<NoteFile>(json, JsonOptions) ?? new NoteFile();
        }
        catch
        {
            // 壊れていても起動は続行する(新規扱い)
            return new NoteFile();
        }
    }

    public void Save(string folder)
    {
        UpdatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        var path = Path.Combine(folder, FileName);
        var json = JsonSerializer.Serialize(this, JsonOptions);

        // 隠し属性が付いたままだと上書きに失敗するので、一度外してから書き戻す
        if (File.Exists(path))
        {
            var attr = File.GetAttributes(path);
            if (attr.HasFlag(FileAttributes.Hidden))
                File.SetAttributes(path, attr & ~FileAttributes.Hidden);
        }
        File.WriteAllText(path, json, new UTF8Encoding(false));
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
    }
}

public class PrintSettings
{
    /// <summary>A4 / B5 / 2L / L / Hagaki</summary>
    public string PaperSize { get; set; } = "A4";
    /// <summary>Portrait / Landscape</summary>
    public string Orientation { get; set; } = "Portrait";
    /// <summary>1 / 2 / 4</summary>
    public int PhotosPerPage { get; set; } = 2;
    /// <summary>2枚のときの並べ方: Vertical / Horizontal</summary>
    public string Layout { get; set; } = "Vertical";
}

public class PhotoEntry
{
    public string File { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Selected { get; set; }
}
