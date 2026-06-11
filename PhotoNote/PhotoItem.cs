using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace PhotoNote;

/// <summary>一覧に表示する写真1枚分のデータ。</summary>
public class PhotoItem : INotifyPropertyChanged
{
    public string FullPath { get; }
    public string FileName { get; }
    public DateTime ModifiedTime { get; }

    public PhotoItem(string fullPath)
    {
        FullPath = fullPath;
        FileName = System.IO.Path.GetFileName(fullPath);
        ModifiedTime = System.IO.File.GetLastWriteTime(fullPath);
    }

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; OnPropertyChanged(); } }
    }

    private string _text = "";
    public string Text
    {
        get => _text;
        set { if (_text != value) { _text = value; OnPropertyChanged(); } }
    }

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(); }
    }

    private DateTime? _dateTaken;
    public DateTime? DateTaken
    {
        get => _dateTaken;
        set { _dateTaken = value; OnPropertyChanged(); OnPropertyChanged(nameof(DateText)); }
    }

    /// <summary>印刷サンプルと同じ「2025/5/7」形式。</summary>
    public string DateText => DateTaken?.ToString("yyyy/M/d") ?? "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
