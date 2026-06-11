using System.Windows;

namespace PhotoNote;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();

        // 開発用: --uishot <写真フォルダ> <出力フォルダ> で画面をPNGに保存して終了する
        if (e.Args.Length >= 3 && e.Args[0] == "--uishot")
            _ = window.RunUiShotAsync(e.Args[1], e.Args[2]);
    }
}
