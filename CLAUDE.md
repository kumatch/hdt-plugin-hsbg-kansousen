# CLAUDE.md — hdt-plugin-hsbg-kansousen

Claude Code がセッション開始時に読む、このリポジトリの規約です。詳細は `docs/`。ここには「毎回守ること」だけを書きます。

## このリポジトリは何か

Hearthstone Battlegrounds の 1 ゲームごとの記録を、HDT（Hearthstone Deck Tracker）のプラグインでローカルの JSON に保存する。
記録は分析ツール hsbg-kansousen（別リポジトリ）が読む。ログの仕様 `docs/log-schema.md` と `schema/game-log.schema.json` が両者の契約。

## 必ず読むドキュメント

- `docs/architecture.md` — 設計
- `docs/log-schema.md` — ログ仕様（これが契約。変更時は `schema_version` を上げる）
- `docs/hdt-research.md` §3 — HDT の API とログの挙動（確認済みの事実）
- `TASKS.md` — 今やるべきタスク

## 環境の前提

- OS: Windows 10/11。**WSL は使わない**（.NET Framework / WPF のビルドと HDT の実行は Windows ネイティブでしか動かない）
- シェル: Claude Code は PowerShell から起動される。ビルド・ファイル操作の手順は PowerShell / Windows パスで書く
- .NET: プラグインは `net472`（.NET Framework 4.7.2。HDT 本体が要求）。ビルドは `dotnet build`（SDK スタイル csproj。Visual Studio は不要）。
  HDT の参照先は `plugin/Directory.Build.props` の `HdtPath`（既定は `%LOCALAPPDATA%\HearthstoneDeckTracker\app-*` の最新版を自動検出。
  `-p:HdtPath=` か環境変数 `HDT_PATH` で上書き可）。ビルド → 配置は `.\scripts\deploy-plugin.ps1`（HDT 起動中は中断する）
- HDT の `GameEvents.*.Add` は呼び出し元の型を StackTrace で見てプラグインに紐付ける（`Remove` は無い）。
  **イベント登録は必ず `IPlugin` 実装クラスのメソッド内から直接呼ぶ**（docs/hdt-research.md §3.12）
- HDT のインストール先: `%LOCALAPPDATA%\HearthstoneDeckTracker\app-<version>\`、プラグイン置き場: `%APPDATA%\HearthstoneDeckTracker\Plugins\`、
  データ置き場: `%APPDATA%\HearthstoneDeckTracker\`（config.xml, CardDefs など）
- Hearthstone のログ: HDT の `config.xml` の `HearthstoneDirectory` 配下の `Logs\Hearthstone_<日時>\Power.log`
- HDT のソース: GitHub `HearthSim/Hearthstone-Deck-Tracker`。タグが無いことがあるので master の "v1.xx.y" コミットを使う。
  API シグネチャは **インストール済み HDT と同じバージョンのソース**で確認する（Web の古い記事より優先）

## 構成

```
plugin/
├── HsbgKansousen.sln, Directory.Build.props（HdtPath）
├── HsbgKansousen/
│   ├── HsbgKansousenPlugin.cs   # IPlugin。GameEvents の登録はここだけ
│   ├── Capture/              # HDT の型に触る唯一の層: GameSession, EntitySnapshotter, HdtCards, BobsBuddyReader, DeityReader, PlayerBuffs
│   ├── Logic/                # HDT 非依存の純粋ロジック: ShopActionTracker, CombatResolver, DeityTracker, BuffStatsTracker, AbortedGame, Reconnect
│   ├── Model/                # docs/log-schema.md の DTO
│   ├── Output/               # GameLogWriter, EventsJsonlWriter, PowerLogWriter, PartialLogJournal, JsonSettings
│   ├── Config/               # PluginConfig, PluginConfigStore, OutputDirectory（出力先の検証。HDT 非依存）
│   ├── UI/                   # SettingsWindow（WPF をコードで組む）
│   ├── Diagnostics/          # PluginLogger（%APPDATA%\HearthstoneDeckTracker\HsbgKansousen\plugin.log）
│   └── Spike/                # 全 Entity ダンプ（config の spike_dump。HDT / HS の更新後の調査用）
└── HsbgKansousen.Tests/         # xUnit（Logic / Output / Model / Config のみ。HDT の型に触るテストは書かない）
schema/game-log.schema.json   # docs/log-schema.md と同期する JSON Schema
scripts/                      # deploy-plugin.ps1, package-plugin.ps1, analyze_spike_dump.py, anonymize_log.py, check_private.py, check_cli_sync.py
```

## コーディング規約

### C#
- 名前空間 `HsbgKansousen`。HDT の API 参照は `Hearthstone_Deck_Tracker.API.*` と `Hearthstone_Deck_Tracker.Hearthstone.*`
- **HDT 依存コードと純粋ロジックを分離する**。HDT の型は `Capture/` だけが触り、変換ロジックは軽量 DTO を経由させて単体テスト可能にする
- 例外は必ず捕捉して `PluginLogger` に書く。**プラグインの例外で HDT を落とさない**
- ゲーム中のイベントハンドラは軽量に。重い処理（ファイル書き込み、シリアライズ）は Task で逃がす
- JSON は Newtonsoft.Json（HDT 同梱の版。自前で同梱しない）。ファイル書き込みはアトミックに（一時ファイル → 置き換え）
- HDT 同梱のアセンブリ（MahApps.Metro, Newtonsoft.Json, HearthDb など）は配布 zip に含めない
- ログの形を変えたら `docs/log-schema.md`、`schema/game-log.schema.json`、テストを揃え、`schema_version` を上げる（下の「ログの形を変えたとき」）

### PowerShell
- `.ps1` は **BOM 付き UTF-8** で保存する（BOM が無いと Windows PowerShell 5.1 が日本語を含む行で構文エラーになる）。
  Write ツールは BOM なしで書くので、作成後に `[IO.File]::WriteAllText($p, $text, [Text.UTF8Encoding]::new($true))` で保存し直す
- 作成後は `[System.Management.Automation.Language.Parser]::ParseFile` で pwsh と `powershell.exe` の両方で構文チェックする

### その他のテキスト
- BOM なし UTF-8、改行 LF（`.gitattributes`）。Markdown の編集は Edit / Write ツールで行う
  （Bash の Python ヒアドキュメントにバックスラッシュ付きのパスを書くと制御文字に化けることがある）

## ログの形を変えたとき（hsbg-kansousen にも同じ作業で反映する）

ログ（主ログ JSON の DTO・`schema/game-log.schema.json`・events.jsonl の `type` やフィールド）を変えたら、分析ツール hsbg-kansousen
（隣のフォルダ `..\hsbg-kansousen` に置く。別の場所なら環境変数 `HSBG_KANSOUSEN_DIR`）にも **同じ作業の中で** 反映する。片方だけで止めない。

1. このリポジトリ: `docs/log-schema.md`（改訂履歴と該当の節）、`schema/game-log.schema.json`、`GameLog.CurrentSchemaVersion`、テスト（`SampleLogs.cs` など）
2. hsbg-kansousen: `hsbg/game-log.schema.json` と `docs/log-schema.md` にこのリポジトリのものをそのままコピーする
3. hsbg-kansousen: 読み込みを合わせる。`hsbg/loader.py` の `KNOWN_SCHEMA_VERSION` を新しい `schema_version` にし、新しいキーは `normalize.py` の列（`TURN_COLUMNS` / `COMBAT_COLUMNS` など）とテスト、
   列を変えたら `cache.py` の `CACHE_FORMAT` を上げる。分析で注意が要るものは `hsbg/templates/skills/hsbg-analysis/log-caveats.md`、
   過去のログを power.log から埋められるなら補完コマンド（`powerlog.py` + `cli.py` の `backfill-*`）。キーの意味が変わるなら互換処理
4. 両方の検証コマンドを緑にする（hsbg-kansousen は その `CLAUDE.md` の検証コマンド）。`python scripts\check_cli_sync.py --strict` も緑にする
5. 作業の区切りの報告では、**両方のリポジトリ**について変更したファイルと推奨するコミットメッセージを出す

hsbg-kansousen の CI も、プラグインの main の schema / `docs/log-schema.md` とコピーが違えば失敗する（push 後の最後の確認）。

リリースでは、ログの版が前のリリースから変わっていれば **hsbg-kansousen を先に** リリースする（docs/development.md「リリースの手順」）。
番号は両者でそろえず、対応はログの版で決まる（hsbg-kansousen の README「プラグインと hsbg の版」）。

## 検証コマンド（変更後は必ず緑にする）

- `cd plugin; dotnet build HsbgKansousen.sln -c Release; dotnet test HsbgKansousen.sln -c Release --no-build`（警告ゼロ）
- `python scripts\check_cli_sync.py --strict`（hsbg-kansousen のコピーが最新か。`schema_version` の改訂履歴があるか。hsbg-kansousen が無ければ `--strict` を外す）
- 公開前: `python scripts\check_private.py`（個人の情報が入っていないか。deny list は各自がリポジトリの外に置く）
- プラグインを変えたら `plugin/HsbgKansousen/HsbgKansousen.csproj` の `<Version>` を上げる
- 実機確認は人間に依頼する（`.\scripts\deploy-plugin.ps1` は HDT 起動中は中断する）。手順を PowerShell / GUI 操作として書いて止まる
- Hearthstone / HDT の更新後の調査: `config.json` の `spike_dump: true` で全 Entity ダンプを取り、`scripts/analyze_spike_dump.py` で読む（docs/maintenance.md §2）

## 作業の進め方

- `TASKS.md` の順に進める。コミットはユーザーの許可を得てから
- 「要検証」とされた事項は推測で実装せず、HDT のバイナリ・ソースで確かめて docs に追記する

## やらないこと

- Hearthstone のメモリ読み取り、パケット解析、ゲームクライアントの改変（利用規約違反のリスク）
- 外部サーバへの送信（プラグインはローカルのファイルにだけ書く）
- BattleTag・プレイヤー名・アカウント ID をログの JSON に書くこと
- HDT のバイナリやソースをリポジトリ・配布物に含めること
