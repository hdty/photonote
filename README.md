# PhotoNote

デジカメ写真にひとことメモを付けて、メモと撮影日入りで印刷するWindows用ソフト。
ヒデトイズ HidéToys

## ダウンロード

[Releases](https://github.com/hdty/photonote/releases) から `PhotoNote.exe` をダウンロードしてください。
インストール不要で、そのまま実行できます。

※ コード署名をしていないため、初回起動時に SmartScreen の警告が出ることがあります。
「詳細情報」→「実行」で起動できます。

## 動作環境

- Windows 10 / 11 (64bit)
- 自己完結型の exe で配布するため、.NET ランタイムのインストールは不要

## 使い方

1. 「📂 フォルダを開く」で写真の入っているフォルダを選ぶ
2. 写真が2列×4行(1画面8枚)で表示される。「◀ 前へ」「次へ ▶」でページを切り替える
3. 並び順は「撮影日時・ファイル名・更新日時」×「昇順・降順」から選べる(既定: 撮影日時の古い順)。
   日時は時刻まで含めて比較する。並び順はフォルダごとに保存され、印刷もこの順で出る
4. それぞれの写真に説明文を書き、印刷したい写真にチェックを付ける
5. 「🖨 印刷する」を押すと同じウィンドウ内で印刷画面に切り替わる
   (「◀ 写真選びに戻る」でいつでも戻れる)
6. 用紙サイズ・向き・1ページの枚数・並べ方を選び、右のプレビューを確認して「印刷する」を押す
7. プリンタドライバの印刷ダイアログが開くので、プリンタ・部数・画質などを選んで印刷する

書いた説明文と印刷設定は、フォルダごとに自動保存される(入力をやめて1.5秒後、フォルダ切替時、終了時)。

## データファイル(.photonote.json)

写真フォルダごとに、固定名の隠しファイル `.photonote.json`(UTF-8、隠し属性)を保存する。

```json
{
  "app": "PhotoNote",
  "version": 1,
  "updatedAt": "2026-06-11T12:34:56+09:00",
  "sortOrder": "DateTakenAsc",
  "printSettings": {
    "paperSize": "A4",
    "orientation": "Portrait",
    "photosPerPage": 2,
    "layout": "Vertical"
  },
  "photos": [
    { "file": "IMG_0001.JPG", "text": "入場シーン", "selected": true },
    { "file": "IMG_0002.JPG", "text": "フェアプレイフラッグ掲出", "selected": false }
  ]
}
```

- `sortOrder`: `DateTakenAsc`(撮影日時 古い順・既定) / `DateTakenDesc` / `FileNameAsc` / `FileNameDesc` / `ModifiedAsc` / `ModifiedDesc`(いずれも時刻まで比較)
- `paperSize`: `A4` / `B5` / `2L` / `L` / `Hagaki`
- `orientation`: `Portrait`(縦) / `Landscape`(横)
- `photosPerPage`: 1 / 2 / 4
- `layout`: 2枚のときの並べ方 `Vertical`(縦に並べる) / `Horizontal`(横に並べる)
- `photos`: ファイル名(フォルダからの相対名)ごとのメモと選択状態。
  フォルダから消えた写真の項目も、メモが書いてあれば消さずに残す。

## 撮影日

EXIF の撮影日時(DateTaken)を使い、取れない場合はファイルの更新日時で代用する。
印刷時の表記はサンプルに合わせて `2025/5/7` 形式。

## 印刷レイアウトの仕様

- 紙端の余白: B5以上(A4・B5)=上下左右10mm、B5未満(2L・L判・はがき)=5mm
  (フチあり印刷の必要余白は EPSON EW-M873T=四辺3mm、Canon PIXUS TS8630=左右5mm/上下3.4mm)
- キャプションのフォント: Meiryo UI
- 文字サイズ固定: B5以上=10pt、B5未満=8pt(1ページの枚数では変えない)
- 写真とキャプションの間隔: 文字サイズの2割(A4で約0.7mm)
- 「B5以上」の判定は用紙の短辺が182mm(B5の短辺)以上かどうか

## ビルド

.NET 8 SDK が必要。

```powershell
cd PhotoNote
dotnet build                 # 開発ビルド
dotnet run                   # 実行
```

配布用(単一exe、ランタイム同梱):

```powershell
dotnet publish PhotoNote -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

出力: `PhotoNote\bin\Release\net8.0-windows\win-x64\publish\PhotoNote.exe`

## 構成

- `MainWindow` — 1ウィンドウに2画面を持つ
  - 写真選び画面: 2列×4行のページ式一覧・並び順選択・メモ入力・印刷対象の選択
  - 印刷画面: 印刷設定・プレビュー(DocumentViewer、検索バーは非表示化)。
    「印刷する」でプリンタドライバの印刷ダイアログを開いてから印刷する(画質設定のため)
- `PrintDocumentBuilder` — 印刷ページ(FixedDocument)の組み立て。写真の下に左=メモ、右=撮影日
- `NoteFile` — `.photonote.json` の読み書き(隠し属性の付け外しを含む)
- `ImageLoader` — 画像読み込み(EXIF回転の反映、撮影日時の取得)
- アプリアイコンは `assets/photonoteicon01.png` から `tools/IconTool` で生成
  (市松背景を除去し、ロゴの水色→桜色の角丸グラデーション背景に合成、16/24/32/48/64/256px)
  ```powershell
  dotnet run --project tools\IconTool -- assets\photonoteicon01.png PhotoNote\Assets\PhotoNote.ico preview.png
  ```
- HidéToysロゴ(`PhotoNote/Assets/HideToysLogo2.png`)はリソースとして埋め込み済みだが、
  通常画面では未使用(将来のヘルプ画面用)

## 開発用オプション

```powershell
PhotoNote.exe --uishot <写真フォルダ> <出力フォルダ>
```

起動して写真フォルダを読み込み、写真選び画面と印刷画面をPNGに保存して終了する(UI確認用)。

## リリース手順

```powershell
dotnet publish PhotoNote -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# バージョン情報の言語欄を「日本語」にする(.NETの仕様でニュートラルになるため)
.\tools\Set-JapaneseVersionLang.ps1 PhotoNote\bin\Release\net8.0-windows\win-x64\publish\PhotoNote.exe
```

## ライセンス

ソースコードは [MIT License](LICENSE)。
ただし、リポジトリ内の画像(HidéToysロゴ・アプリアイコンなど)はMITの対象外で、
著作権は HidéToys が保持します(詳細は LICENSE 末尾の注記を参照)。
