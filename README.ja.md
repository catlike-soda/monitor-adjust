# モニター調整 · monitor-adjust

**DDC/CI 経由でモニターの明るさ・コントラスト・入力ソースを直接操作するネイティブ Windows GUI。**

[English](README.md) | [中文](README.zh-CN.md) | **日本語**

Windows 用の小さなデスクトップツールで、モニターの**明るさ・コントラスト・入力ソース**を
DDC/CI プロトコル経由で直接操作します。モニターのハードウェアそのものを制御するもので、
Windows 標準の明るさスライダーとは別物です。そのため、外付けモニターや、OS のスライダーでは
届かないコントラスト・入力ソースも調整できます。

C# WinForms のネイティブ単体実行ファイルで、スクリプトホストは一切使いません
（bat / ps1 / PowerShell 不要）。

> このリポジトリには**ソースコード**のみを置いています。ビルド済みの `MonitorAdjust.exe` は
> [**Releases**](https://github.com/catlike-soda/monitor-adjust/releases) で配布しています（[ダウンロード](#ダウンロード)参照）。
> 実行には別途 `winddcutil.exe` が必要です（[動作要件](#動作要件)を参照）。

## ダウンロード

最新版は [**Releases**](https://github.com/catlike-soda/monitor-adjust/releases) から：

1. Releases から `MonitorAdjust.exe` をダウンロード
2. [scottaxcell/winddcutil](https://github.com/scottaxcell/winddcutil) から `winddcutil.exe` をダウンロード（同リポジトリの `dist/winddcutil.exe` です）
3. 2 つのファイルを同じフォルダーに置き、`MonitorAdjust.exe` を実行

## 機能

- UI は**中国語 / English を実行中に切り替え**できます（下記参照）
- 接続されているモニターの台数を自動検出 —— **N 台挿せば N 台表示**。ウィンドウは画面サイズに
  合わせて自動レイアウトし、台数が多い場合は列分割・スクロールします
- モニターごとに明るさ・コントラストを個別に調整
- 入力ソースの切り替え（HDMI / DP / DVI / VGA / コンポーネント など）。確認ダイアログ付き
- **「適用」は明るさとコントラストのみを書き込みます** —— 誤って入力ソースを切り替えることはありません
- 「元に戻す」でウィンドウを開いた時点の値に戻します
- 「再読み込み」で全項目を読み直し、現在値を新しい「元の値」として記録します
- 明るさの範囲は 0-100 / 0-255 に対応。既定では自動判定、手動指定も可能

## 言語

UI は**中国語と English を実行中に切り替え**られます。ウィンドウ下部の「言語 / Language」
ボックスで選ぶと即座に反映され、選択内容は記憶されます。

> ⚠️ **現在 UI は中国語と英語の 2 言語のみで、日本語 UI は未対応です。**
> この日本語 README は説明用のドキュメントです。

- **初回起動時**はシステム言語に従います（中国語環境 → 中国語、それ以外 → English）
- 選択内容は exe と同じフォルダーの `monitor-adjust.ini` に保存されます。
  そのフォルダーに書き込めない場合（Program Files 以下にインストールした場合など）は
  `%LOCALAPPDATA%\monitor-adjust\settings.ini` にフォールバックします
- コマンドラインで強制することもできます:

```powershell
.\MonitorAdjust.exe --lang en
```

## 動作要件

[**winddcutil**](https://github.com/scottaxcell/winddcutil)（ddcutil の Windows 移植版、
PyInstaller 製の単体実行ファイル）が必要です。**`MonitorAdjust.exe` と同じフォルダーに置いてください。**

プログラムは次の順に探します: exe と同じフォルダー → exe 隣の `winddcutil\` サブフォルダー →
カレントディレクトリ → `PATH`。

システム要件: Windows 7 SP1 以降 + 同梱の .NET Framework 4.x。管理者権限は不要です。

## 使い方

1. `MonitorAdjust.exe` と `winddcutil.exe` を同じフォルダーに置く
2. `MonitorAdjust.exe` をダブルクリック
3. スライダーを動かして「適用」をクリック

注意:

- **入力ソースの切り替えは慎重に**: 何も接続されていない端子に切り替えると、画面が真っ暗になるか
  「信号なし」と表示され、ソフトウェアでは元に戻せなくなることがあります。
  その場合はモニター本体のボタンで戻してください
- モニター側の OSD メニューで DDC/CI を有効にする必要があります（多くのモニターは初期状態で有効）
- ノート PC 内蔵ディスプレイは通常 DDC/CI に非対応のため一覧に表示されません。これは正常です
- プログラムは自身と同じフォルダーに `gui-log.txt` という実行ログを作ります。いつでも削除できます

## ソースからのビルド

Windows 同梱の .NET Framework コンパイラーを使います。**Visual Studio は不要です**:

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:winexe /codepage:65001 `
  /out:"MonitorAdjust.exe" `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  "MonitorAdjust.cs"
```

- `/target:winexe` — コンソールウィンドウを出さない
- `/codepage:65001` — ソースは UTF-8 です。**この指定は必須**で、付けないと中国語文字列が文字化けします
- 出力ファイル名は `MonitorAdjust.exe` のままにしてください（アセンブリ名がファイル名から取られます）

## 隠しオプション（デバッグ用）

```powershell
# セルフチェック: 読み取った値をテキストに書き出す（ウィンドウを表示しない）
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--dump','dump.txt' -Wait

# オフスクリーン描画: 画面外に描いて PNG 保存。レイアウト確認用
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--render','preview.png' -Wait

# ダミーデータモード: winddcutil を一切呼ばず、ダミーデータで UI を描画（1〜16 台）
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--fake','6','--render','n6.png' -Wait

# 画面が小さいふりをして、多モニター時のレイアウトを確認
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--fake','8','--screen','1024x600','--render','n8.png' -Wait

# UI 言語を強制（zh / en）。2 種類のスクリーンショット比較用
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--lang','en','--render','en.png' -Wait
```

`--fake` は書き込みもすべてブロックするため、**実際のモニターを書き換えることはありません**。

## 既知の制限

- **範囲（最大値）は推測です**: `winddcutil getvcp` は現在値のみを返し、最大値は返しません。
  コマンドラインオプションも一切ありません。そのためプログラムは「読み取った値が 100 超なら
  0-255」と判断します。誤判定した場合は範囲を手動で指定してください
- 入力ソースの一覧が時々読み取れません（DDC/CI 通信自体が不安定なため）。その場合は一般的な
  入力ソースのフォールバック一覧を使います
- スライダーの下限は常に 0 です（ほぼすべてのモニターで明るさ / コントラストの最小値は 0 のため）

## ライセンス

コードは自由に使用・改変・配布できます。
`winddcutil` は各作者による独立したプロジェクトであり、独自のライセンスに従います。
本リポジトリには含まれていません。
