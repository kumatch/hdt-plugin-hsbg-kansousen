# リスク・運用・保守

## 1. リスク一覧

| # | リスク | 影響 | 対策 |
|---|---|---|---|
| R1 | HDT のアップデートで内部 API（`GameV2`, `GameStats`, `GameEvents`）が変わりプラグインが読み込めない/落ちる | ログが取れない | HDT の自動更新を許容しつつ、起動時にプラグインが例外を出したら `plugin.log` に HDT バージョンと例外を残す。インストール済みの版と同じコミットの HDT ソース（docs/hdt-research.md §3 の探し方）で再確認して修正。`Capture/` に HDT 依存を閉じ込めているので修正範囲は限定的 |
| R2 | Hearthstone のパッチで GameTag の意味・Power.log の形式が変わる | 一部フィールドが `null` になる、または誤値 | フィールド単位で `null` 許容。`notes[]` に警告。Power.log を補助ファイルとして残しているので後から再抽出可能 |
| R3 | 新ヒーロー・新メカニクス（新トリンケット等）の未対応 | 該当フィールドが `null` | `card_id` は未知でも記録する。名前解決は HDT の HearthDb に任せる |
| R4 | プラグインが HDT のパフォーマンスを落とす | ゲーム体験の悪化 | `OnUpdate` は差分検出のみ。I/O は非同期。Phase 3 の受け入れ基準に処理時間を含める |
| R5 | ゲーム中のクラッシュでログ消失 | データ欠損 | 部分ログジャーナル + events.jsonl 逐次書き込み |
| R6 | MMR の試合後値が取れない | Q1（MMR 推移）の欠損 | 遅延ポーリング。取れなければ次ゲームの `mmr_before` から逆算（分析側で補完、`estimated_` 扱い） |
| R7 | Blizzard の利用規約 | アカウントリスク | HDT が公式に許容されている「ログファイル読み取り」の範囲内に留める。メモリ読み取り・自動操作は絶対にしない |
| R8 | 個人情報 | 公開時の懸念 | ログには BattleTag 等を含めない（プレイヤー ID は HDT 内部の数値のみ）。相手プレイヤー名も記録しない |
| R9 | Windows 環境の不慣れによる作業停滞 | 開発の遅延 | docs/development.md のトラブルシュート。Claude Code への指示を PowerShell ベースで統一 |
| R10 | Duos モードでのスキーマ不一致 | ログの解釈ミス | `game.variant` で区別。MVP は solo のみ対象、Duos は `status` を complete にしつつ `notes` に未対応を記す |
| R11 | ダッシュボードが意図せず LAN に公開される / 外部へ通信する | ログの露出 | `.streamlit/config.toml` で `127.0.0.1` bind と利用統計 OFF を固定。Phase 5b の受け入れ基準で LISTEN アドレスを確認 |
| R12 | HDT の `CardDefs\CardDefs.jaJP.xml` / `CardDefs.base.xml` が無い・形式が変わる（HDT の内部ファイルで公式 API ではない） | カード名が英語（ログの名前）に戻る、スキンの正規化が正規表現フォールバックになる | `cards.py` は読めなければ空の表を返し、表示は英語で続行する。ダッシュボードのサイドバーに「カード名: 英語」と出る。形式が変わったら `analysis/tests/fixtures/carddefs/` を作り直してテストで検出する |

## 2. 運用ルーチン

### 日常
- HDT を起動してから Hearthstone を起動する（順序が逆でも HDT は追従するが、ゲーム開始前に起動しておく）
- 週に一度 `uv run hsbg validate` で incomplete の件数と警告を確認

### Hearthstone のパッチ後
1. BG を 1 ゲームプレイ
2. 最新ログを `hsbg validate` で検証。`null` が増えたフィールドがないか `notes[]` を確認（`hsbg stats --last 5` の表に `-` が増えていないかも見る）
3. 異常があれば Claude Code に「最新ログの notes と plugin.log を見て、パッチで壊れた箇所を特定して」と依頼
4. 原因がタグや Entity の変化にありそうなら、`%APPDATA%\HearthstoneDeckTracker\HsbgKansousen\config.json` で `"spike_dump": true` にして HDT を再起動し 1 ゲームプレイ →
   `python scripts\analyze_spike_dump.py "<出力先>\.spike\<ゲームのフォルダ>"`（出力先の既定は `%USERPROFILE%\Documents\HSBG-Logs`） で全 Entity のタグを確認する
   （Phase 1 と同じ手順。docs/hdt-research.md §3.15 が正常時の基準値）。終わったら `spike_dump` を false に戻す（1 ゲームで約 50 MB 出る）

### HDT のアップデート後
1. HDT の Plugins 画面で HsbgKansousen が有効のままか確認
2. `%APPDATA%\HearthstoneDeckTracker\HsbgKansousen\plugin.log` に読み込みエラーがないか確認
3. エラーがあれば、新しい版と同じコミットの HDT ソース（docs/hdt-research.md §3 の探し方）を取得して修正
   （ビルドの参照先は `plugin/Directory.Build.props` が最新の `app-*` を自動で選ぶので、設定の変更は不要）
4. エラーが無くても、神祇・Bob's Buddy・盤面の読み方に関わる変更が無いか差分を確認する。HDT の GitHub は更新が遅れることがある
   （1.58.4 はリリース当日に master が 1.58.3 のままで、HDT-Releases のリリースノートも空だった）ので、その場合は旧版と新版の `app-*` を逆コンパイルして比べる
   （HDT は 1 つ前の `app-*` を残しているので、更新直後なら両方ある。docs/hdt-research.md §3.16 が実例）:
   ```powershell
   $h = "$env:LOCALAPPDATA\HearthstoneDeckTracker"; $old = '1.58.3'; $new = '1.58.4'; $w = "$env:TEMP\hdt-diff"
   dotnet tool install ilspycmd --version 9.1.0.7988 --tool-path "$w\tools"   # 最新版は SDK 9 で入らないので版を固定。グローバルには入れない
   foreach ($v in $old, $new) { & "$w\tools\ilspycmd.exe" -p -o "$w\src-$v" -r "$h\app-$v" "$h\app-$v\HearthstoneDeckTracker.exe" }
   git diff --no-index --stat -- "$w\src-$old" "$w\src-$new"   # .baml と XAML のバージョン文字列だけの 1 行差分は無視してよい
   Get-Content "$w\src-$new\resources\changelog.md" -TotalCount 15   # リリースノート（exe に埋め込まれている）
   ```
   ファイル単位の差分は `Compare-Object` で `app-*` 内の各ファイルの `VersionInfo.FileVersion` を比べれば分かる（BobsBuddy.dll が変わっていたら同じ手順で逆コンパイルし、
   `BobsBuddy.Simulation.Output` のフィールドが残っているか確認する）。見る型（逆コンパイル結果は名前空間ごとのフォルダ）: `BobsBuddyInvoker` / `BobsBuddyUtils`、
   `BattlegroundsDeityState`、`GameEvents`、`GameV2`。結果は docs/hdt-research.md §3 に追記する
5. ダッシュボードのサイドバーに「カード名: 英語（HDT の CardDefs が見つかりません）」と出たら、`%APPDATA%\HearthstoneDeckTracker\CardDefs\` の
   ファイル名（`CardDefs.base.xml`, `CardDefs.jaJP.xml`）が変わっていないか確認する（R12）

### 新ヒーロー・新ミニオンの日本語名
HDT が起動時に CardDefs を更新するので、プロジェクト側の作業は不要。新カードの名前が英語のまま表示されるときは HDT を一度起動し直す
（`hsbg` は CardDefs をプロセス起動時に 1 回読む。ダッシュボードは `hsbg serve` の再起動で反映）。

## 3. バックアップ
- `Documents\HSBG-Logs\` はクラウド同期フォルダ（OneDrive 等）に置くか、定期的に zip する。プラグインはファイルを追記/新規作成しかしないので同期との相性は良い

## 4. スキーマ変更の手順
1. docs/log-schema.md を更新し `schema_version` を上げる（互換性のない変更は major）
2. `schema/game-log.schema.json` を更新
3. `analysis/hsbg/loader.py` に旧バージョンからの変換を追加
4. プラグインの `plugin_version` も上げる
5. `HsbgKansousen.Tests` と `analysis/tests` の fixtures に新旧両方を置く

## 5. 意思決定の記録（ADR 風）

| 日付 | 決定 | 理由 |
|---|---|---|
| 2026-09-15 | 自作 HDT プラグインを採用 | 既存プラグインは保守状況と出力形式の制約、Tier7 はローカルログ非生成 |
| 2026-09-15 | 出力は JSON（1 ゲーム 1 ファイル）+ events.jsonl + power.log | AI 入力と機械処理の両立、再抽出の余地 |
| 2026-09-15 | Windows ネイティブ + Claude Code ネイティブ、WSL 不使用 | .NET Framework / WPF と HDT の制約 |
| 2026-09-15 | 分析は Python、レポートは単一 HTML | DB 不要、オフラインで完結 |
| 2026-09-15 | ローカル Web ダッシュボード（Streamlit）を追加。静的 HTML と併存し、集計層を共有 | 日常閲覧での絞り込み・掘り下げのため。スコープ外は「外部公開」であり、ローカル起動は要件に抵触しない |
