# 開発環境の作り方（Windows）

プラグインをソースからビルドし、自分の HDT で動かすまでの手順です。使うだけなら README のインストール手順（Releases の zip）で足ります。

> **WSL は使いません。** HDT プラグインは .NET Framework 4.7.2 / WPF のアセンブリを参照するため、ビルドも実行も Windows ネイティブでしか成立しません。

## 0. macOS / Linux に慣れた人向けの Windows メモ

| macOS / Linux | Windows | 補足 |
|---|---|---|
| Terminal | **Windows Terminal**（PowerShell がデフォルト） | `Win + X` → 「ターミナル」 |
| zsh / bash | **PowerShell 7**（推奨）または Windows PowerShell 5.1 | プロンプトが `PS C:\...>` なら PowerShell |
| Homebrew / apt | **winget**（標準搭載） | `winget install <id>` |
| `~` | `$env:USERPROFILE`（`C:\Users\<名前>`） | |
| `~/Library/Application Support` | `%APPDATA%`（`C:\Users\<名前>\AppData\Roaming`） | エクスプローラーのアドレスバーに `%APPDATA%` と打つと開く |
| 同上（ローカル） | `%LOCALAPPDATA%`（`...\AppData\Local`） | HDT 本体はこちら |
| `open .` | `explorer .` または `ii .` | |
| `which` | `Get-Command` / `where.exe` | |

改行コード: リポジトリの `.gitattributes`（`* text=auto eol=lf`）に加えて、`git config --global core.autocrlf false` にしておくと崩れません。

## 1. 必須ソフト

すべて PowerShell で（管理者権限は不要。SmartScreen で聞かれたら「実行」）。

```powershell
winget install Microsoft.WindowsTerminal
winget install Microsoft.PowerShell          # PowerShell 7
winget install Git.Git
winget install Microsoft.DotNet.SDK.9         # dotnet build（net472 もビルドできる）
```

新しいターミナルで `git --version`、`dotnet --version` を確認します。

- **.NET Framework 4.7.2 の参照アセンブリ**: SDK スタイルの csproj では、.NET SDK が参照アセンブリ（NuGet の
  `Microsoft.NETFramework.ReferenceAssemblies`）を自動で使うので、Targeting Pack は通常いりません（Visual Studio の無い PC での確認は未了）。
  ビルドで「参照アセンブリが見つからない」と言われたら Visual Studio Installer で「.NET Framework 4.7.2 Targeting Pack」を入れてください
- **Visual Studio は必須ではありません**。HDT のプロセスにアタッチしてデバッグしたいときだけ、Visual Studio 2022 Community（「.NET デスクトップ開発」）を入れます
- **Python 3.12**（`winget install Python.Python.3.12`）: `scripts/analyze_spike_dump.py` などの補助スクリプトを使うとき

## 2. Hearthstone Deck Tracker

- https://hsdecktracker.net/ からインストールして一度起動し、Hearthstone を起動してオーバーレイが出ることを確かめる
- ビルドは `%LOCALAPPDATA%\HearthstoneDeckTracker\app-<version>\` の最新版を自動で参照する（`plugin/Directory.Build.props`）。
  別の版を使うときは `dotnet build -p:HdtPath="<フォルダ>"` か環境変数 `HDT_PATH`
- HDT のソース（API の確認用）: `git clone https://github.com/HearthSim/Hearthstone-Deck-Tracker`。
  タグが無いことがあるので、インストールした版のコミットを `git log -S"1.xx.y" -- "Hearthstone Deck Tracker/Hearthstone Deck Tracker.csproj"` で探す

## 3. ビルド・テスト・配置

```powershell
git clone https://github.com/kumatch/hdt-plugin-hsbg-kansousen
cd hdt-plugin-hsbg-kansousen\plugin
dotnet build HsbgKansousen.sln -c Release      # 警告ゼロ
dotnet test HsbgKansousen.sln -c Release --no-build
cd ..
# HDT を終了してから
.\scripts\deploy-plugin.ps1                 # ビルドして %APPDATA%\HearthstoneDeckTracker\Plugins\HsbgKansousen\ に配置
```

HDT を起動し、Options > Tracker > Plugins で「HsbgKansousen」を有効にします。プラグイン自身のログは
`%APPDATA%\HearthstoneDeckTracker\HsbgKansousen\plugin.log` です。

## 4. Claude Code（任意）

このリポジトリは Claude Code での開発を前提に `CLAUDE.md` / `TASKS.md` を置いています。

```powershell
irm https://claude.ai/install.ps1 | iex
claude --version
```

Git for Windows が入っていれば Claude Code は Git Bash を Bash ツールとして使います。リポジトリのフォルダで `claude` を起動してください。

## 5. Windows 固有のハマりどころ

- **DLL のブロック**: ダウンロードした zip から展開した DLL は「ブロック」属性が付き、HDT に読み込まれない。ファイルのプロパティ → 「ブロックの解除」、
  または `Unblock-File <path>`（自分でビルドした DLL には付かない）
- **実行ポリシー**: `.ps1` が「実行できません」と言われたら `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`
- **パスの長さ**: 260 文字制限に当たったら `git config --global core.longpaths true`
- **ファイルロック**: HDT 起動中はプラグイン DLL を上書きできない。配置の前に HDT を終了する（`deploy-plugin.ps1` は HDT 起動中なら止まる）
- **デバッガのアタッチ**: Visual Studio で「デバッグ > プロセスにアタッチ」→ `HearthstoneDeckTracker.exe`。ブレークポイントには `.pdb` も Plugins フォルダに必要
  （`deploy-plugin.ps1` は .pdb があれば一緒に置く。`-Configuration Debug` でデバッグ用にビルドできる）
- **Windows PowerShell 5.1**: `Set-Content -Encoding utf8NoBOM` のような PowerShell 7 だけの指定は使えない。`[IO.File]::WriteAllText` を使う
