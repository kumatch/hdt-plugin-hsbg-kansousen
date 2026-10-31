# システム設計（プラグイン）

## 1. 全体像

```
┌──────────────────┐   Power.log    ┌──────────────────────────────┐
│ Hearthstone      │ ─────────────▶ │ Hearthstone Deck Tracker     │
│ (Windows)        │                │  ├─ Log parser / Entities    │
└──────────────────┘                │  ├─ Bob's Buddy              │
                                    │  └─ Plugins                  │
                                    │      └─ HsbgKansousen.dll  ◀── このリポジトリ (C#)
                                    └──────────────┬───────────────┘
                                                   │ 1 ゲーム = 1 JSON (+ 補助ファイル)
                                                   ▼
                        <出力先>（既定 %USERPROFILE%\Documents\HSBG-Logs）
                          └─ YYYY\MM\
                               ├─ YYYYMMDD-HHMMSS_<gameid>.json            ← 主ログ（docs/log-schema.md）
                               ├─ YYYYMMDD-HHMMSS_<gameid>.events.jsonl    ← 詳細イベント（任意）
                               └─ YYYYMMDD-HHMMSS_<gameid>.power.log.gz    ← Power.log の写し（任意）
                                                   │
                                                   ▼
                              分析ツール hsbg-kansousen（別リポジトリ。ログを読むだけ）
```

ログの仕様（docs/log-schema.md と `schema/game-log.schema.json`）がプラグインと分析ツールの契約。分析ツールは対応する `schema_version` の範囲を持つ。

## 2. 設計

### 2.1 責務
- BG のゲームだけを対象にする（`Core.Game.IsBattlegroundsMatch`。`OnGameStart` 時点で確定している。Solo/Duos は `IsBattlegroundsSoloMatch` / `IsBattlegroundsDuosMatch`）
- ゲーム開始〜終了のライフサイクルで状態を収集し、終了時に 1 ファイルとして書き出す
- 途中クラッシュ・HDT 終了・ゲーム強制終了でも **部分ログを失わない**（後述）
- BattleTag・プレイヤー名・アカウント ID は記録しない

### 2.2 内部構造

```
HsbgKansousen/
├── HsbgKansousenPlugin.cs        # IPlugin 実装。GameEvents の登録はここから直接行う（HDT が呼び出し元の型でプラグインに紐付けるため。docs/hdt-research.md §3.12）。ロジックは持たない
├── Config/
│   ├── PluginConfig.cs        # 出力先、補助ファイルの ON/OFF。%APPDATA%\HearthstoneDeckTracker\HsbgKansousen\config.json
│   ├── PluginConfigStore.cs   # 更新時刻を見てゲーム開始時と設定画面を開くときに再読み込み
│   └── OutputDirectory.cs     # 出力先の検証（展開・書き込み可否）と %USERPROFILE% 表記。HDT 非依存
├── UI/
│   └── SettingsWindow.cs      # 設定画面（WPF をコードで組む）
├── Capture/                   # HDT の型に触るのはここだけ
│   ├── GameSession.cs         # 1 ゲームのライフサイクル管理（開始→ショップ→戦闘→終了→MMR 後値）。戦闘フェーズの遷移検出もここ
│   ├── EntitySnapshotter.cs   # Core.Game.Entities → 盤面/手札/ショップの観測値 (MinionRecord, ShopTick)
│   ├── HdtCards.cs            # CardId / DbfId → CardRef、Entity → MinionRecord
│   ├── BobsBuddyReader.cs     # Bob's Buddy の予測値をリフレクションで読む（失敗したら null）
│   └── DeityReader.cs         # 神祇（シーズンの仕組み）の状態
├── Logic/                     # HDT 非依存の純粋ロジック（単体テスト対象）
│   ├── ShopActionTracker.cs   # 100ms ごとの観測値の差分から 購入/売却/リロール/ティアアップ/ヒーローパワー/スペル/トリプル を数える
│   ├── CombatResolver.cs      # ヒーロー被弾イベント列（重複排除）→ 勝敗と与被ダメ
│   ├── DeityTracker.cs, AbortedGame.cs, Reconnect.cs
├── Model/                     # ログの DTO（docs/log-schema.md と 1:1）。HDT の型に依存しない
├── Output/                    # HDT 非依存
│   ├── JsonSettings.cs        # snake_case / null 保持のシリアライズ設定
│   ├── GameLogWriter.cs       # JSON シリアライズ + アトミック書き込み + ファイル名規則
│   ├── EventsJsonlWriter.cs   # events.jsonl の逐次追記（キュー + バックグラウンド書き込み）
│   ├── PowerLogWriter.cs      # Core.Game.PowerLog（HDT が保持するゲーム単位の行）を書き出す（任意機能）
│   └── PartialLogJournal.cs   # 各ターン開始時に .partial\<game_id>.json へ上書き保存し、OnLoad 時に残骸を incomplete として回収
├── Diagnostics/
│   └── PluginLogger.cs        # プラグイン自身のログ（例外、警告）
└── Spike/
    └── SpikeRecorder.cs       # 全 Entity ダンプ（spike_dump。HDT / HS の更新後の調査用）
```

**設計原則**: `Capture/` だけが HDT の型（`Entity`, `GameV2`, `GameStats`）に触る。`Logic/`・`Model/`・`Output/`・`Config/` は HDT 非依存で、`HsbgKansousen.Tests` で単体テストする。

### 2.3 ライフサイクルとフック

1 ターンは `OnTurnStart(Player)` → (~100ms) `IsBattlegroundsCombatPhase=false` → ショップ → `OnTurnStart(Opponent)` → (~1.5s) `IsBattlegroundsCombatPhase=true` → 戦闘（`OnEntityWillTakeDamage`）→ 次の `OnTurnStart(Player)`（docs/hdt-research.md §3.15）。

| タイミング | HDT イベント（確認済み） | 処理 |
|---|---|---|
| ゲーム開始 | `GameEvents.OnGameStart` | `GameSession` 生成。`IsBattlegroundsMatch` で判定（この時点で `Entities` は空） |
| 再接続 | `GameEvents.OnGameStart`（記録中のゲームがあるのに来る） | `MetaData.ServerInfo.GameHandle` が記録中のゲームと同じなら同じ記録を続ける（Entity に依存する追跡だけ捨て、次の `OnUpdate` / `OnTurnStart` で再開）。見られなかった戦闘は次のショップ開始時に HP + アーマーの差から推定。違えば前のゲームを `incomplete` で書き出してから新しく始める（docs/log-schema.md「再接続」） |
| ヒーロー選択中 | `OnUpdate`（`GetTurnNumber()==0` の間、数秒おき） | `Player.PlayerEntities` の `IsHero && IsInHand && BACON_SKIN` を選択肢として記録（無料リロールで差し替わる） |
| ショップ開始 | `OnTurnStart(ActivePlayer.Player)` | ターン番号、HP/アーマー（前戦闘の結果反映後）を記録。前ターンの**戦闘結果を確定**（被弾イベント + `BACON_WON_LAST_COMBAT`）。前ターンの Bob's Buddy 出力を読む。ロビー他プレイヤーの HP/ティア/順位を更新し、HP ≤ 0 になった相手を敗退扱い |
| ショップ中 | `OnUpdate`（100ms） | 購入/売却/リロール/レベルアップ等のイベント検出（ショップ Entity の入れ替わり、手札/盤面の zone 遷移、`RESOURCES_USED` の増減）。差分ベースで軽量に |
| ショップ終了 | `OnTurnStart(ActivePlayer.Opponent)` | **自盤面・手札のスナップショット**、タヴァーンティア、`RESOURCES`（所持）/`RESOURCES_USED`（消費）を確定。相手はまだ Bob なので相手盤面は取らない |
| 戦闘開始 | `OnUpdate` で `IsBattlegroundsCombatPhase` の false→true を検出 | 相手ヒーロー（`IsHero && IsInPlay && IsControlledBy(Opponent.Id)`）と相手盤面のスナップショット。`NEXT_OPPONENT_PLAYER_ID` と照合 |
| 戦闘中 | `OnEntityWillTakeDamage`（ヒーロー対象。**同一ダメージが 2 回来るので重複排除**） | 与ダメ/被ダメを記録 |
| ゲーム終了 | `GameEvents.OnGameEnd` | 順位（`PlayerEntity.HERO_ENTITY` が指すヒーローの `PLAYER_LEADERBOARD_PLACE`）、MMR 前、所要時間、最終盤面を確定して **JSON を書き出す**。`Core.Game.PowerLog` を書き出す。部分ログを削除。以後 `OnUpdate` で `BattlegroundsRatingAfter` をポーリング（実測 0.2 秒で確定、最大 30 秒）して JSON を上書き |
| メニュー復帰 | `GameEvents.OnInMenu` | セッションを閉じる（この後 HDT が `Entities` をクリアし `IsBattlegroundsMatch=false` に戻る。以後の `IsBattlegroundsCombatPhase` 遷移は無視） |
| HDT 終了/プラグイン無効化 | `IPlugin.OnUnload` | 進行中セッションがあれば部分ログを `status: "incomplete"` で書き出す（ハンドラ自体は外せないので `Enabled` フラグで無害化） |

### 2.4 堅牢性
- **部分ログジャーナル**: 各ターン開始時に `<出力先>\.partial\<gameid>.json` に現状を上書き保存。`OnLoad` 時に残骸があれば `status: "incomplete"` として正規の場所へ移動する
- **Hearthstone だけのクラッシュ / 切断**: HDT は動いたままで、再接続すると `OnGameStart` を出し直す。GameHandle で同じゲームと分かれば 1 本のログに続ける。
  HDT は再接続時に `Core.Game.PowerLog` を空にするので、power.log の再接続前の行はプラグインがターン開始ごとにコピーしておく
- **アトミック書き込み**: `<name>.json.tmp` に書き、置き換えで確定（config.json も同じ）
- **例外の隔離**: すべてのハンドラで try/catch。例外は `PluginLogger` に記録し、セッションは可能な限り継続。`OnUpdate` の例外は 100 回を超えると HDT がプラグインを無効化するので、必ず止める
- **MMR 後値の遅延**: `GameStats.BattlegroundsRatingAfter` は `OnGameEnd` 時点では 0 で、HDT が `SaveReplays` の中で HearthMirror から取得して同じオブジェクトに代入する（実測 0.2 秒）。`OnGameEnd` で `CurrentGameStats` の参照を保持し、`OnUpdate` で最大 30 秒ポーリングして 0 以外になったら JSON を上書き。取れなければ `BgsLastGames.xml` を `StartTime` で引き、それでも無ければ `null`
- **BG 以外のモード**: 何もしない

### 2.5 設定

`%APPDATA%\HearthstoneDeckTracker\HsbgKansousen\config.json`（無ければ初回起動時に既定値を書き出す）
```json
{
  "output_dir": "%USERPROFILE%\\Documents\\HSBG-Logs",
  "write_events_jsonl": true,
  "write_power_log": true,
  "power_log_gzip": true,
  "snapshot_hand": true,
  "spike_dump": false,
  "mmr_after_wait_sec": 30
}
```
- `power_log_gzip`: power.log を gzip で書く（1 ゲーム 29 MB → 約 3 MB）。**Power.log の写しには同じロビー全員の BattleTag が入る**
- `snapshot_hand`: ショップ終了時の手札を `turns[].hand_end_of_shop` に記録する
- `spike_dump`: 全 Entity ダンプ（`<出力先>\.spike\`）。挙動の再確認やパッチ後の調査に使う（HDT の再起動が必要）
- `mmr_after_wait_sec`: ゲーム終了後に MMR 後値を待つ最大秒数
- 環境変数は展開される
- 0.11.0 で `HsbgLogger` から改名した。新しい設定フォルダに config.json が無く、`%APPDATA%\HearthstoneDeckTracker\HsbgLogger\config.json` があれば
  起動時に写して引き継ぐ（古いフォルダは消さない）。`scripts/deploy-plugin.ps1` は古い `Plugins\HsbgLogger` を取り除く（残ると 2 つのプラグインが同じゲームを記録する）

設定画面（`UI/SettingsWindow`）は Options > Tracker > Plugins の「Settings」ボタンと Plugins メニューの「HsbgKansousen」から開く。
出力先フォルダ（参照 / 既定に戻す。プロファイル配下は `%USERPROFILE%\...` で保存）、events.jsonl / power.log / gzip / 手札記録を変えられ、
保存時に出力先を作って書き込めるか確かめる（`Config/OutputDirectory`）。`spike_dump` と `mmr_after_wait_sec` は JSON を直接編集。
設定画面を開くときとゲーム開始時に、ファイルの更新時刻が変わっていれば読み直す（ゲーム中に保存しても、そのゲームは開始時の設定のまま）。

## 3. ログファイルの命名と配置

- ルート: 設定の出力先（既定 `%USERPROFILE%\Documents\HSBG-Logs\`）
- サブフォルダ: `YYYY\MM\`（ゲーム開始時刻のローカル日時）
- ファイル名: `YYYYMMDD-HHMMSS_<game_id>.json`（`game_id` は HDT の GameStats.GameId（GUID）が取れればそれ、無ければ開始時刻ベースの自前 ID）
- 補助ファイルは同名で拡張子違い（`.events.jsonl`, `.power.log.gz`）
- ルート直下の `.` で始まるフォルダはプラグインと分析ツールの作業用: `.partial\`（ジャーナル）、`.spike\`（調査ダンプ）、`.cache\`（分析ツールのキャッシュ）、
  `.excluded\`（利用者が分析から外したゲーム）。分析ツールはこれらを読まない
- 不完全なログ: 同じ規則で `status: "incomplete"` を JSON 内に持つ

## 4. 将来の拡張点（設計上考慮するが実装しない）

- 出力先の抽象化（`ILogSink`）
- スキーマのマイグレーション（`schema_version` を見て分析側で旧形式を変換）
