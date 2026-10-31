# 方式調査と決定（HDT の API と挙動）

作成日: 2026-09-15
このドキュメントの調査結果には **確認済み** と **要検証**（Claude Code の Phase 1 で実機確認する）を区別して記す。

## 1. 候補の比較

| 方式 | 概要 | 長所 | 短所 | 判定 |
|---|---|---|---|---|
| A. Power.log 直接解析 | HS が出力する Power.log を自前パーサで解析 | HDT 非依存。生データが最も豊富 | BG の状態再構築（戦闘の再現、タグの意味づけ）を自前で実装する必要があり、HS パッチ毎に追従が必要。HDT が既にやっていることの再実装 | ✕ 不採用（ただし Power.log の**保存**は補助ファイルとして採用） |
| B. 既存サードパーティ HDT プラグイン | Battlegrounds-Match-Data 等を導入 | 実装不要 | メンテ状況が不安定、出力形式（CSV）が固定、外部アップロード機能を内包しセキュリティ評価が必要、拡張しにくい | ✕ 不採用（**ソースは参考実装として活用**） |
| C. 自作 HDT プラグイン | HDT の `API.GameEvents` と `Core.Game.Entities` からログを構築 | HDT が再構築した状態をそのまま使える。出力形式を自由に設計できる。HDT 自体が活発に保守されている | C# / .NET Framework 開発が必要。HDT の内部 API 変更に追従が必要 | **◎ 採用** |
| D. HSReplay Tier7（有料） | HSReplay.net の BG 向けサブスク | メタ統計（ヒーロー tier、構成勝率）とゲーム内オーバーレイ、個人ダッシュボード | **ローカルにゲーム単位のログファイルを生成しない**。Web 上の個人統計は自由に扱えるデータ形式ではない | ✕ 要件を満たさない。分析のベンチマーク（自分の成績 vs メタ平均）として将来オプション |

### 決定
**C. 自作 HDT プラグイン**を採用し、**A の Power.log 保存**を補助ファイルとして組み合わせる。
Power.log を残しておくと、後から「プラグインが取り損ねた情報」を再抽出でき、AI に生ログを読ませる余地も残せる。

## 2. 各方式の調査詳細

### 2.1 HDT プラグイン API（確認済み: HDT Wiki "Creating Plugins", 2024-02 更新）

- プラグインは **.NET Framework 4.7.2 対象のクラスライブラリ**として作り、`HearthstoneDeckTracker.exe` を参照に追加し、`Plugins.IPlugin` を実装した public クラスを置く
- DLL（または zip）を `%APPDATA%\HearthstoneDeckTracker\Plugins\` に置く。HDT は zip も読み込める
- `IPlugin` の要点: `OnLoad`, `OnUnload`, `OnUpdate`（約 100ms ごとに呼ばれる）, `ButtonText`, `MenuItem`（null 可）
- `API.GameEvents`: ゲーム開始/終了、ターン開始、ドロー/プレイ等のイベント
- `Hearthstone.Game.Entities`: ゲーム内すべての Entity。位置（手札/盤面）、体力、タグ等
- `Enums.Hearthstone.GAME_TAG`（現在は HearthDb の `GameTag` 列挙）でタグを参照
- **デバッグ**: HDT の options > tracker > settings に Debug Window があり、`Game` と `Game.Entities` をライブ表示できる
- HDT が同梱している依存（MahApps.Metro, Newtonsoft.Json 等）をプラグイン側で同梱してはいけない
- 開発 Tips: Post-Build で Plugins フォルダにコピー、HDT プロセスにアタッチしてデバッグ。ソロ（ボット）対戦だとタイマーがなく検証しやすい
- プラグインが起動時にクラッシュすると HDT に無効化される
- テンプレート: VeXHarbinger の HDT Plug-in Template（VS Marketplace）。例: andburn/hdt-plugin-example

### 2.2 BG 固有データの取得可能性（**Phase 1 で実プレイダンプにより確定**。詳細は §3.15）

凡例: ○ = 実ダンプで値を確認した / △ = 取得経路はあるが実装に工夫が要る、または今回のゲームでは発生せず未確認 / ✕ = 不可

| データ | 取得可否 | 確定した取得方法 |
|---|---|---|
| 自分のヒーロー | ○ | 自プレイヤー Entity（`Core.Game.PlayerEntity`）の `HERO_ENTITY` タグが指す Entity の `CardId`（スキン ID のまま。例 `TB_BaconShop_HERO_34_SKIN_C`）。`Card.DbfId` も併記。**`Entities` から「自分制御のヒーロー」を検索する方法は NG**（ゲーム終了時に結果画面用の複製ヒーローが SETASIDE に現れ、順位タグが古い） |
| 最終順位 | ○ | 自ヒーロー Entity の `PLAYER_LEADERBOARD_PLACE`（ゲーム中はリーダーボードの現在順位、`OnGameEnd` 時点で最終順位。ヒーローは GRAVEYARD に移っている）。HDT 自身の `BattlegroundsDetails.FinalPlacement` は `OnGameEnd` の後に埋まるので照合用 |
| MMR（試合前/後） | ○ | `CurrentGameStats.BattlegroundsRating`（`OnGameEnd` 時点で確定）/ `BattlegroundsRatingAfter`（`OnGameEnd` 時点は 0、実測では 0.2 秒後に確定。ゲーム直後に HS を終了しても取れた）。フォールバック: `OnInMenu` 後の `Core.Game.CurrentBattlegroundsRating`（試合後の値に更新されていた）、`BgsLastGames.xml` |
| タヴァーンティア（ターン毎） | ○ | 自ヒーロー Entity の `PLAYER_TECH_LEVEL`（ターン 1 の開始直後だけ 0 なので、その場合はプレイヤー Entity の同タグ or 1）。到達ターンは `Core.Game.GetBattlegroundsHeroLatestTavernUpTurn(heroId)` が `{tier: turn}` を返す（実測 `{2:2, 3:4, 4:7, 5:9}`） |
| 自分の HP / アーマー | ○ | 自ヒーロー Entity の `HEALTH - DAMAGE`（`Entity.Health`）と `ARMOR`。開始 HP はヒーローにより異なる（今回 60）。アーマーはトリンケット等で途中付与される |
| 自分の盤面ミニオン（ショップ終了時） | ○ | 戦闘開始時（下記）に `Entities` のうち `IsMinion && IsInPlay && IsControlledBy(Player.Id)`。`ATK`, `HEALTH`, `TECH_LEVEL`, `PREMIUM`（ゴールデン）, `TAUNT`, `DIVINE_SHIELD`, `POISONOUS`, `VENOMOUS`, `REBORN`, `WINDFURY`, `STEALTH`, `CARDRACE`, `ZONE_POSITION`。手札（`Player.Hand`）も同時に取れる |
| 戦闘相手のヒーローと盤面 | ○ | **`Core.Game.IsBattlegroundsCombatPhase` が false→true になった瞬間**（`OnUpdate` で検出）に、`Entities` のうち `IsHero && IsInPlay && IsControlledBy(Opponent.Id)` が相手ヒーロー（`PLAYER_ID`, `HEALTH`, `ARMOR` 付き）、`IsMinion && IsInPlay && IsControlledBy(Opponent.Id)` が相手盤面。HDT の `GetBattlegroundsBoardStateFor(相手ヒーローId)` と一致した。**`OnTurnStart(Opponent)` の時点では相手はまだ Bob のままで盤面も無い**（約 1.5 秒早い）。次の相手は自ヒーロー Entity の `NEXT_OPPONENT_PLAYER_ID`（ショップ開始時点で確定、実測で全ターン一致） |
| 戦闘結果（勝/負/引分、与ダメ/被ダメ） | ○ | `GameEvents.OnEntityWillTakeDamage` でヒーロー Entity への被ダメを記録（自分 → 負け、相手 → 勝ち、どちらも無し → 引き分け）。**同じダメージが 2 回発火するので (entity, value, 時刻) で重複排除**。実測でターン毎の HP 差分と全一致。次ショップ開始時の自ヒーロー `BACON_WON_LAST_COMBAT`（1/0）でも裏取りできる。ダメージ上限は GameEntity の `BACON_COMBAT_DAMAGE_CAP`（今回 15） |
| ヒーロー選択肢（提示された 4 体） | ○ | ゲーム開始 4 秒後以降、`Player.PlayerEntities` のうち `IsHero && IsInHand && HasTag(BACON_SKIN)`（HDT は `BACON_HERO_CAN_BE_DRAFTED \|\| BACON_SKIN` かつ `!BACON_LOCKED_MULLIGAN_HERO`）。各 Entity に `HEALTH`, `ARMOR`（開始アーマー）あり。または `Core.Game.BattlegroundsHeroPickState.OfferedHeroDbfIds` / `PickedHeroDbfId`。**ヒーローの無料リロール**を使うと 1 体が差し替わり、差し替わった Entity に `BACON_NUM_MULLIGAN_REFRESH_USED=1`、プレイヤー Entity に `BACON_NUM_FREE_REROLLS_USED=1` が付く。`OnGameStart` 直後は `Entities` が空なので数秒待つ |
| ロビーの他プレイヤー（ヒーロー、HP、ティア、順位、トリンケット） | ○ | `Entities` のうち `IsHero && HasTag(PLAYER_ID) && IsInSetAside` かつ自分以外。各ターン `HEALTH`/`ARMOR`/`PLAYER_TECH_LEVEL`/`PLAYER_LEADERBOARD_PLACE`/`PLAYER_TRIPLES`/`BACON_FIRST_TRINKET_DATABASE_ID`/`BACON_SECOND_TRINKET_DATABASE_ID` が更新される。**HP が 0 以下になったターン = 敗退ターン**。自分より後に生き残った相手の最終順位は自分の敗退時点では未確定（リーダーボード順位を推定値として保存） |
| ゴールド | ○ | プレイヤー Entity の `RESOURCES`（そのターンの所持ゴールド。上限 10 を超える値も出る）と `RESOURCES_USED`（消費）。**戦闘開始時に読むと「そのターンの所持/消費」**。ショップ開始時（`OnTurnStart(Player)`）はまだ前ターンの値のまま。売却は `RESOURCES_USED` を減らす |
| 購入/売却/リロール/レベルアップの回数 | △ | 専用タグは無い。ショップ Entity（`CONTROLLER == Opponent.Id` かつ `ZONE == PLAY` のミニオン）の入れ替わり、手札への移動（`OnPlayerGet`?）、`PLAYER_TECH_LEVEL` の変化、`RESOURCES_USED` の増減を `OnUpdate` の差分検出で数える（HDT-FinalStatsPlugin の方式）。Phase 3 で実装・検証 |
| Bob's Buddy の勝率予測 | ○（リフレクション） | `BobsBuddyInvoker.GetInstance(gameId, turn, false).Output`。**戦闘開始時点ではそのターンの Output はまだ null**。次のショップ開始時（または終了時）に前ターン分を読むと `winRate/tieRate/lossRate/myDeathRate/theirDeathRate/avDamage/simulationCount` が入っている（実測 12 ターン分すべて取得、State は `Shopping`、最終ターンは `GameOver`） |
| アノマリー | △ | `BattlegroundsUtils.GetBattlegroundsAnomalyDbfId(GameEntity)`（`BACON_GLOBAL_ANOMALY_DBID`）。今回のゲームはアノマリー無し（0）で値の確認は未 |
| 旧神（Global Old God） | ○ | GameEntity の `BACON_GLOBAL_OLD_GOD_DBID`（今回 130610）。`BattlegroundsUtils.GetBattlegroundsDeityDbfId` |
| トリンケット | ○ | `Player.Trinkets`（`BACON_TRINKET` を持つ PLAY 中の `BATTLEGROUND_TRINKET` Entity。今回 `BG36_MagicItem_400`, `BG36_MagicItem_602`）。獲得ターンは出現したターン。自ヒーロー Entity の `BACON_FIRST/SECOND_TRINKET_DATABASE_ID` にも DbfId が入る |
| クエスト | △ | `Player.QuestRewards` / `QUEST_REWARD_DATABASE_ID`。今シーズンはクエスト非稼働（`BACON_QUESTS_ACTIVE` 無し）で未確認 |
| ヒーローパワー | ○ | `Player.Board` のうち `IsHeroPower`（`TB_BaconShop_HP_035` 等） |
| ロビーの種族 | ○ | `BattlegroundsUtils.GetAvailableRaces()`（今回 5 種族） |
| ゲーム単位の Power.log | ○ | `Core.Game.PowerLog`（終了時 16,795 行）。**HDT が読み込んだ時点でカード名の日本語が文字化けしている**（CardId は正常） |
| 他プレイヤーのショップ盤面、Discover の選択肢、正確な MMR 変動の内訳 | ✕ | 構造的に不可（クライアントに送られてこない） |

### 2.3 参考にするサードパーティ実装（**インストールはしない**。ソースを読む）

| リポジトリ | 何が参考になるか | 注意 |
|---|---|---|
| benluiwj/Battlegrounds-Match-Data（jawslouis からの引き継ぎフォーク） | ヒーロー、順位、試合後 MMR、ティアアップ到達ターン、戦闘ごとの両者ミニオン stats/keyword、相手ヒーロー、戦闘結果を CSV に出す。**やりたいことにもっとも近い** | Google Spreadsheet / 外部ダッシュボードへのアップロード機能を持つ（ネットワーク送信あり）。最終更新時期を確認すること |
| Reign-in-blood/HDT-FinalStatsPlugin | ゴールド消費、リロール数、購入数、ヒーローダメージ、最終盤面、アノマリー、トリンケット。2026 年に活発 | オーバーレイ中心。`Build.bat` でローカル HDT を参照してビルドする方式が参考になる |
| IBM5100o/HDT_BGrank | 相手 MMR をリーダーボードから取得 | 外部 API 呼び出しあり |
| andburn/hdt-plugin-example | 最小構成のプラグイン | 古いが構造理解に十分 |

#### サードパーティプラグインの安全性評価チェックリスト（ソースを読む際も、万一導入する際も適用）
1. ソースが公開されているか。公開バイナリがそのソースから再現ビルドできるか
2. 最終コミット日、HDT の最新版で動く報告があるか（Issues を見る）
3. ネットワーク送信の有無（`HttpClient`, `WebClient`, `WebRequest` を grep）。送信先ドメインと送る内容
4. ファイルシステムアクセスの範囲（自分のフォルダ以外に書いていないか）
5. 追加の依存 DLL（署名の有無、出所）
6. プロセス起動・レジストリ操作・キーボード/マウスフック（`Process.Start`, `Registry`, `SetWindowsHookEx`）の有無
7. HDT が同梱する DLL を上書きしていないか（zip の中身を確認）

Phase 1 で Claude Code が上記 1〜7 を benluiwj/Battlegrounds-Match-Data と HDT-FinalStatsPlugin に対して実施し、結果をこのファイルに追記する。

### 2.4 HSReplay Tier7（確認済み: hsreplay.net/battlegrounds/tier7）

- ゲーム内機能（ヒーローピック時の tier 表示、構成統計）は **HDT 併用で週 2 回まで無料**。それ以上はサブスク
- Web 機能（メタ統計、個人ダッシュボード）はサブスクで利用。HDT からアップロードされたゲームが集計対象
- 提供されるのは **集計済み統計と Web UI** であり、ユーザーが自由に扱えるゲーム単位のログファイルではない
- 結論: 本プロジェクトの「ファイルシステムにログを書く」要件は満たさない。
  一方「自分のヒーロー別平均順位が、同レート帯のメタ平均と比べてどうか」を見るベンチマークとしては価値があるため、
  Phase 5 以降で「自前ログから算出した指標を Tier7 の数値と並べる」ことをオプションとして検討する（手動転記で十分）

### 2.5 Hearthstone のログ出力（要検証）

- HDT は起動時に `%LOCALAPPDATA%\Blizzard\Hearthstone\log.config` を書き換えて Power.log 等の出力を有効化する
- ログの出力先は `%LOCALAPPDATA%\Blizzard\Hearthstone\Logs\` 配下。近年のパッチではセッション毎のサブフォルダ
  （`Hearthstone_YYYY_MM_DD_HH_MM_SS\Power.log` 形式）になっている **← Phase 1 で実機確認**
- HDT は各ゲームの Power.log の範囲を切り出して HSReplay へアップロードし、ローカルにも保存する設定がある（`Replays` フォルダ、`.hdtreplay` 形式） **← 要検証**。
  これが使えれば、自前で Power.log を切り出す必要がなくなる
- HDT はゲームごとの `GameStats` を `%APPDATA%\HearthstoneDeckTracker\Games\` 等に XML で保存している **← 要検証**。
  含まれる項目（BattlegroundsRating, Turns, Result, StartTime 等）を確認し、プラグインで取れない値の補完に使えるか判断する

## 3. Phase 1 の検証結果（2026-09-25、Claude Code が実機・ソースで確認）

凡例: **確認済み** = インストール済み HDT 1.58.3 のバイナリ、同バージョンのソース、実機のファイルで確認したもの。
「要ダンプ確認」= 1-8 の実プレイ後に Entity ダンプで裏付けを取るもの。

### 3.1 環境

| 項目 | 結果 |
|---|---|
| インストール済み HDT | **1.58.3**（`%LOCALAPPDATA%\HearthstoneDeckTracker\app-1.58.3\HearthstoneDeckTracker.exe`, FileVersion 1.58.3.8362）。`app-1.58.2` も残っている（Squirrel の旧版） |
| HDT ソース | GitHub のタグは `v1.56.1` で止まっており **1.58.x のタグは無い**。`master` の先頭コミット `509bb0b`（2026-09-24, メッセージ "v1.58.3", csproj `<Version>1.58.3</Version>`）が同一バージョン。作業用の別フォルダ（例: `%USERPROFILE%\src\ref\Hearthstone-Deck-Tracker`）に `--depth 400` で clone した（HEAD = 509bb0b）。**今後は「バージョンを上げたコミット」を `git log -S"1.xx.y" -- "Hearthstone Deck Tracker/Hearthstone Deck Tracker.csproj"` で探す** |
| その後の HDT 更新 | 2026-09-28 に **1.58.4** へ自動更新（ソース未公開のためバイナリで差分を確認。§3.16） |
| その後の HDT 更新（2） | 2026-10-07 に 1.58.9、2026-10-10 に **1.58.10** へ自動更新。今回は GitHub の master に v1.58.10（`3e4ba05d`）までソースが公開されていたのでソースで確認（§3.17） |
| HDT 本体のビルド設定 | `net472`, `UseWPF`, **`PlatformTarget x64`**, `LangVersion 10`, `Nullable enable`（SDK スタイル csproj） |
| HDT 同梱 DLL（プラグインは参照のみ、同梱禁止） | Newtonsoft.Json **13.0.3**, HearthDb **36.6.0**, HearthMirror 32.4.2, BobsBuddy 1.78.1, MahApps.Metro 1.6.5, System.Text.Json 8.0 |
| ツール | git 2.55.0, dotnet SDK 9.0.318（8.0.425 も有）, Python 3.12.10, uv 0.12.14, Claude Code 2.1.282, Visual Studio 2022 Community, .NET Framework 4.7.2 Targeting Pack あり |
| HDT の稼働状態 | 調査時点で HDT は起動していない。プラグインフォルダ `%APPDATA%\HearthstoneDeckTracker\Plugins\` は空、`plugins.xml` も空 |

### 3.2 `IPlugin` のシグネチャ（確認済み: `Plugins/IPlugin.cs`）

```csharp
namespace Hearthstone_Deck_Tracker.Plugins
{
    public interface IPlugin
    {
        string Name { get; }
        string Description { get; }
        string ButtonText { get; }      // options > tracker > plugins のボタン文言
        string Author { get; }
        Version Version { get; }
        MenuItem MenuItem { get; }      // System.Windows.Controls.MenuItem。null 可
        void OnLoad();                  // 有効化時
        void OnUnload();                // 無効化時 / HDT 終了時
        void OnButtonPress();
        void OnUpdate();                // 約 100ms ごと（PluginManager.StartUpdateAsync の Task.Delay(100)）
    }
}
```

プラグインの読み込み挙動（`Plugins/PluginManager.cs`, `PluginWrapper.cs`）:

- HDT は起動時に **`%APPDATA%\HearthstoneDeckTracker\Plugins\` を `<app-x.y.z>\Plugins\` へ同期（コピー）** してから、後者の `*.dll` を **サブフォルダ込み** で `Assembly.LoadFrom` する。同期は「APPDATA 側に無いファイル/フォルダを app 側から削除」も行う。→ 配置先は `%APPDATA%\HearthstoneDeckTracker\Plugins\HsbgKansousen\HsbgKansousen.dll` でよい
- zip は **ローダーは読まない**。Options 画面へ zip をドラッグ&ドロップしたときだけ `ZipFile.ExtractToDirectory` で展開される（`TrackerPlugins.xaml.cs`）。配布形式 zip は「展開すると DLL が出てくる」構造にする
- `IPlugin` を実装した **public・非抽象** クラスを `Activator.CreateInstance` する（引数なしコンストラクタ必須）
- 有効/無効は `%APPDATA%\HearthstoneDeckTracker\plugins.xml` に `Name` + 相対パスで保存される
- `OnLoad` が例外を投げると HDT がエラーダイアログを出し、そのプラグインは読み込まれない
- `OnUpdate` の例外は `hdt_log.txt` に記録され、**累計 100 回を超えると自動的に無効化**される。1 回の `OnUpdate` が 2000ms を超えると警告ログ
- `Name` が Reconnector 系の文字列、または `iphlpapi` を `DllImport` するアセンブリは読み込み拒否
- HDT のログ API は public: `Hearthstone_Deck_Tracker.Utility.Logging.Log.Info/Warn/Error(string)` → `%APPDATA%\HearthstoneDeckTracker\Logs\hdt_log.txt`

### 3.3 `API.GameEvents` で利用可能なイベント一覧（確認済み: `API/GameEvents.cs`）

すべて `public static readonly ActionList<T>`（`.Add(Action)` / `.Remove(Action)`）。**BG 専用イベントは存在しない**。

| 分類 | イベント | 引数 |
|---|---|---|
| ゲーム | `OnGameStart`, `OnGameEnd`, `OnGameWon`, `OnGameLost`, `OnGameTied`, `OnInMenu`, `OnMouseOverOff` | なし |
| ゲーム | `OnTurnStart` | `ActivePlayer`（`Player` / `Opponent`） |
| ゲーム | `OnModeChanged` | `Mode`（HUB, BACON, GAMEPLAY, ...） |
| 自分 | `OnPlayerDraw`, `OnPlayerGet`, `OnPlayerPlay`, `OnPlayerHandDiscard`, `OnPlayerMulligan`, `OnPlayerDeckDiscard`, `OnPlayerPlayToDeck`, `OnPlayerPlayToHand`, `OnPlayerPlayToGraveyard`, `OnPlayerCreateInDeck`, `OnPlayerCreateInPlay`, `OnPlayerJoustReveal`, `OnPlayerDeckToPlay`, `OnPlayerMinionMouseOver`, `OnPlayerHandMouseOver` | `Card` |
| 自分 | `OnPlayerHeroPower` | なし |
| 自分 | `OnPlayerFatigue` | `int` |
| 自分 | `OnPlayerMinionAttack` | `AttackInfo { Card Attacker; Card Defender; }` |
| 相手 | `OnOpponentPlay`, `OnOpponentHandDiscard`, `OnOpponentDeckDiscard`, `OnOpponentPlayToDeck`, `OnOpponentHandToDeck`, `OnOpponentPlayToHand`, `OnOpponentPlayToGraveyard`, `OnOpponentSecretTriggered`, `OnOpponentCreateInDeck`, `OnOpponentCreateInPlay`, `OnOpponentJoustReveal`, `OnOpponentDeckToPlay`, `OnOpponentMinionMouseOver` | `Card` |
| 相手 | `OnOpponentDraw`, `OnOpponentGet`, `OnOpponentMulligan`, `OnOpponentHeroPower` | なし |
| 相手 | `OnOpponentFatigue` | `int` |
| 相手 | `OnOpponentMinionAttack` | `AttackInfo` |
| 共通 | `OnEntityWillTakeDamage` | `PredamageInfo { Entity Entity; int Value; }`（ヒーローへの戦闘ダメージ検出に使える。両参考プラグインが使用） |

BG では `OnTurnStart(ActivePlayer.Player)` = **ショップフェーズ開始**、`OnTurnStart(ActivePlayer.Opponent)` = **戦闘フェーズ開始**（Battlegrounds-Match-Data はこれで戦闘開始を検出している）。

### 3.4 `GameV2`（`API.Core.Game`）の BG 関連メンバー（確認済み: `Hearthstone/GameV2.cs`）

| メンバー | 用途 |
|---|---|
| `bool IsBattlegroundsMatch` / `IsBattlegroundsSoloMatch` / `IsBattlegroundsDuosMatch` | BG 判定（`CurrentGameType` から判定）。**設計（docs/architecture.md）の "IsBattlegroundsMatch 相当" はこの名前で存在** |
| `GameType CurrentGameType` / `GameMode CurrentGameMode` | `GT_BATTLEGROUNDS`, `GT_BATTLEGROUNDS_FRIENDLY`, `GT_BATTLEGROUNDS_DUO` 等 / `GameMode.Battlegrounds` |
| `bool IsBattlegroundsCombatPhase` | 戦闘フェーズ中 true。HDT が TagChange で set/reset する（§3.7） |
| `int GetTurnNumber()` | 自分視点のターン番号 = `(GameEntity.TURN + 1) / 2`。マリガン前は 0 |
| `Dictionary<int, Entity> Entities` | 全 Entity |
| `Entity? PlayerEntity` / `OpponentEntity` / `GameEntity` | プレイヤー Entity（PLAYER_ID 持ち）/ 現在の相手プレイヤー Entity / GameEntity |
| `Player Player` / `Player Opponent` | `Id`, `Name`, `Board`, `Minions`, `Hero`, `Trinkets`, `QuestRewards`, `Hand`, `PlayerEntities`, `SetAside` |
| `GameStats? CurrentGameStats` | §3.5 |
| `int? CurrentBattlegroundsRating` | **試合開始時にキャッシュされた MMR**（HearthMirror 経由。Duos なら DuosRating） |
| `BattlegroundRatingInfo? BattlegroundsRatingInfo` | HearthMirror の型（`Rating`, `DuosRating`）。参照する場合は `HearthMirror.dll` も参照に加える |
| `BoardSnapshot? GetBattlegroundsBoardStateFor(int entityId)` | 指定ヒーロー Entity の **最後に戦った時の盤面**（`Entity[] Entities`, `int Turn`）。HDT が戦闘開始時に相手盤面を保存したもの |
| `Dictionary<int,int>? GetBattlegroundsHeroLatestTavernUpTurn(int heroEntityId)` | ヒーローごとの「ティア → 到達ターン」 |
| `Dictionary<int,int>? GetBattlegroundsHeroTriplesByTier(int heroEntityId)` | ヒーローごとの「ティア → トリプル数」 |
| `BattlegroundsHeroPickState BattlegroundsHeroPickState` | `int[]? OfferedHeroDbfIds`, `int? PickedHeroDbfId`（**ヒーロー選択肢は取れる**） |
| `Card? BattlegroundsPlayerDeity` / `BattlegroundsGlobalOldGod` | 旧神・デイティ関連 |
| `List<string> PowerLog` | **このゲームの Power.log 行（HDT が保持）**。`SaveReplays` はこれを使ってアップロード/保存している。→ 自前で Power.log を切り出す必要がない |
| `GameMetaData MetaData` | `ServerInfo`（GameHandle 等）, `AccountId` |
| `bool Spectator` | 観戦中 |

`Entity`（`Hearthstone/Entities/Entity.cs`）の主な public メンバー: `Id`, `CardId`, `Name`, `Tags`（`Dictionary<GameTag,int>`）, `GetTag/HasTag`, `Card`（`Name`, `DbfId`, `LocalizedName`, `Race`...）, `IsHero`, `IsMinion`, `IsInPlay`, `IsInSetAside`, `IsInHand`, `IsControlledBy(id)`, `IsPlayer`, `Attack`, `Health`（HEALTH − DAMAGE）, `ZonePosition`, `IsBattlegroundsTrinket`（BACON_TRINKET）, `IsBattlegroundsQuest`（QUEST_REWARD_DATABASE_ID）, `IsBgsQuestReward`, `IsBattlegroundsSpell`, `Clone()`。

`BattlegroundsUtils`（public static）: `GetBattlegroundsAnomalyDbfId(GameEntity)`, `GetBattlegroundsDeityDbfId(GameEntity)`, `GetOriginalHeroId(cardId)`（スキン → 元ヒーロー ID）, `GetAvailableRaces()`。

### 3.5 `GameStats` の Battlegrounds 関連プロパティ（確認済み: `Stats/GameStats.cs`）

| プロパティ | 内容 | `OnGameEnd` 時点で有効か |
|---|---|---|
| `Guid GameId` | HDT が振る GUID | ○ |
| `DateTime StartTime` / `EndTime` | ローカル時刻 | ○（EndTime は `GameEnd()` で確定。`OnGameEnd` の直前に呼ばれる） |
| `int Turns` | `GetTurnNumber()` | ○ |
| `GameResult Result` | BG では 1 位以外 `Loss` | ○ |
| `int BattlegroundsRating` | **試合前 MMR**（`CurrentBattlegroundsRating` から） | ○（`OnGameEnd.Execute()` より前に代入） |
| `int BattlegroundsRatingAfter` | **試合後 MMR** | **✕ 未設定（0）**。`OnGameEnd` の後に `SaveReplays` → `UpdatePostGameBattlegroundsRating` が HearthMirror を最大 5 回 × 500ms リトライして代入する（STATE COMPLETE 待ちが最大 +5.5 秒）。取れないと 0 のまま |
| `BattlegroundsLobbyDetails? BattlegroundsDetails` | `LobbyRawHeroDbfIds`, `FriendlyPlayerEntityId`, `FriendlyRawHeroDbfId`, `FinalPlacement`, `AnomalyDbfId`, `DeityDbfId` | **✕ 未設定**。オブジェクトは `OnGameStart` で生成されるが、中身は `OnGameEnd.Execute()` の **後** に埋められる |
| `HashSet<Race>? BattlegroundsRaces` | ロビーの種族 | 要ダンプ確認 |
| `int? HearthstoneBuild` | Power.log から読む（1.58.3 で変更） | ○ |
| `GameType GameType`, `int ScenarioId`（BG は 3459）, `Region Region`, `int FriendlyPlayerId`, `string? PlayerHeroCardId`, `string? PlayerName`（BattleTag。**ログには出さない**） | | ○ |

HDT の `HandleGameEnd` の順序（`GameEventHandler.cs` 788〜）:
1. `CurrentGameStats.Turns`, `GameMode`, `BattlegroundsRating`, `GameType`, `ServerInfo` などを埋める
2. `CurrentGameStats.GameEnd()`（EndTime 確定）→ **`GameEvents.OnGameEnd.Execute()`**
3. BG なら `PLAYER_LEADERBOARD_PLACE` から順位を取り `BattlegroundsDetails` を埋め、最終盤面等を退避（`CaptureBattlegroundsGame`）
4. `await SaveReplays()`（STATE COMPLETE 待ち → `BattlegroundsRatingAfter` 取得 → HSReplay アップロード）
5. `RecordBattlegroundsGame()` → **`BgsLastGames.xml` に追記**（§3.10）

→ **プラグインの方針**: `OnGameEnd` で `CurrentGameStats` の参照を保持し、順位は自分で `PLAYER_LEADERBOARD_PLACE` から取る。`BattlegroundsRatingAfter` は同じオブジェクトが後から更新されるので、`OnUpdate` で最大 ~20 秒ポーリングして 0 以外になったら JSON を上書きする（docs/architecture.md §2.4 の設計どおり）。それでも 0 なら `BgsLastGames.xml` を `StartTime` で引く（フォールバック）。

### 3.6 BG 関連 GameTag の実名と数値（確認済み: HearthDb.dll 36.6.0 をリフレクションで列挙 + Power.log 実物）

| 用途 | GameTag | 値 | 備考 |
|---|---|---|---|
| 最終順位 | `PLAYER_LEADERBOARD_PLACE` | 1373 | **ヒーロー Entity**（自分は `zone=PLAY`、他は `SETASIDE`）に付く。Power.log で敗退のたびに全ヒーローの値が更新されるのを確認。HDT も `Entities.Values.First(x => x.HasTag(PLAYER_LEADERBOARD_PLACE) && x.IsControlledBy(Player.Id))` で取っている |
| タヴァーンティア | `PLAYER_TECH_LEVEL` | 1377 | ヒーロー Entity に付く（Power.log で確認）。HDT は TagChange を `HandleBattlegroundsPlayerTechLevel` で受け、ティア到達ターンを記録 |
| 次の対戦相手 | `NEXT_OPPONENT_PLAYER_ID` | 1360 | 自ヒーロー Entity と自プレイヤー Entity の両方に付く（Power.log で確認）。値は相手の PLAYER_ID |
| 戦闘中フラグ | `BACON_IN_COMBAT_PHASE` | 1522 | 要ダンプ確認（HDT 自身はこれを使わず、§3.7 の 2022/3533 を使う） |
| 戦闘相手 ID | `BACON_CURRENT_COMBAT_PLAYER_ID` | 2989 | 要ダンプ確認 |
| 前戦闘の勝敗 | `BACON_WON_LAST_COMBAT` / `BACON_DIED_LAST_COMBAT` | 1422 / 2483 | 要ダンプ確認（HP 差分の補助に使える可能性） |
| ヒーロー選択肢 | `BACON_HERO_CAN_BE_DRAFTED` / `BACON_SKIN` / `BACON_LOCKED_MULLIGAN_HERO` | 1491 / 2038 / — | HDT: `Player.PlayerEntities.Where(x => x.IsHero && (HasTag(BACON_HERO_CAN_BE_DRAFTED) or HasTag(BACON_SKIN)) && !HasTag(BACON_LOCKED_MULLIGAN_HERO))`。または `BattlegroundsHeroPickState.OfferedHeroDbfIds` |
| アノマリー | `BACON_GLOBAL_ANOMALY_DBID` | — | GameEntity に付く。`BattlegroundsUtils.GetBattlegroundsAnomalyDbfId(GameEntity)`（0 = 無し）。`BACON_ANOMALY` という名前は **存在しない** |
| 旧神 | `BACON_GLOBAL_OLD_GOD_DBID` | 4902 | GameEntity。Power.log の CREATE_GAME で確認 |
| トリンケット | `BACON_TRINKET`（Entity 種別）, `BACON_FIRST_TRINKET_DATABASE_ID` / `BACON_SECOND_TRINKET_DATABASE_ID` | 3407, 3741 / 3742 | `Player.Trinkets` = Board のうち `BACON_TRINKET` を持つもの。DBID タグはヒーロー Entity 側（要ダンプ確認） |
| クエスト | `QUEST_REWARD_DATABASE_ID`, `BACON_HERO_QUEST_REWARD_DATABASE_ID` / `_COMPLETED`, `QUEST_PROGRESS` / `QUEST_PROGRESS_TOTAL` | —, 2713 / 2715, 534 / 535 | `Player.QuestRewards`, `Entity.IsBattlegroundsQuest` |
| ゴールド | `RESOURCES` / `RESOURCES_USED` | 26 / 25 | プレイヤー Entity。HDT-FinalStatsPlugin は `RESOURCES_USED` の増分で「消費ゴールド」を出している（売却で減る点に注意） |
| リロール | `BACON_FREE_REFRESH_COUNT`, `BACON_NUM_FREE_REROLLS_USED` | 4536, 3904 | 有料リロール回数のタグは無い。FinalStatsPlugin は「ショップ Entity の入れ替わり + RESOURCES_USED の増分」で推定 |
| トリプル | `PLAYER_TRIPLES`（HDT が `HandleBattlegroundsPlayerTriples` で処理）, `BACON_TRIPLE_CANDIDATE` | — / 1460 | `GetBattlegroundsHeroTriplesByTier` |
| ミニオン属性 | `TECH_LEVEL`(1440), `PREMIUM`(12, ゴールデン), `ATK`(47), `HEALTH`(45), `DAMAGE`(44), `ARMOR`(292), `TAUNT`(190), `DIVINE_SHIELD`(194), `POISONOUS`(363), `VENOMOUS`(2853), `REBORN`(1085), `WINDFURY`(189), `STEALTH`(191), `CARDRACE`(200), `ZONE`(49), `ZONE_POSITION`(263), `CONTROLLER`(50), `CARDTYPE`(202), `IS_BACON_POOL_MINION`(1456) | | `BgsLastGames.xml` の FinalBoard に実際にこれらの数値タグで保存されているのを確認 |
| その他 | `TURN`(20, GameEntity), `PLAYER_ID`(30), `HERO_ENTITY`(27), `STEP`(19), `PLAYSTATE`(17), `BACON_MAX_PLAYER_TECH_LEVEL`(1494), `BACON_DUMMY_PLAYER`(1349), `BACON_HERO_POWER_ACTIVATED`(1398) | | |

存在しない名前: `BACON_ANOMALY`, `IS_BACON_DUO`, `PLAYER_BASE_SHIELD_RESERVE`。
HearthDb の `GameTag` は 1325 メンバー。Power.log 実物に現れた `BACON_*` タグはすべて HearthDb 36.6.0 に定義があった（未定義値は `(GameTag)数値` でキャストして扱える）。

### 3.7 戦闘フェーズの検出方法（確認済み: `LogReader/Handlers/TagChangeActions.cs`）

HDT 自身は **HearthDb に名前の無いタグ `(GameTag)2022` と `(GameTag)3533`** の `1 → 0` 遷移で戦闘開始を検出している:

- `2022`（ソロの "setup" タグ）: `1→0` で `IsBattlegroundsCombatPhase = true`、Bob's Buddy の `StartCombat()`
- `3533`（combat setup）: `1→0` で `IsBattlegroundsCombatPhase = true`、**`SnapshotBattlegroundsBoardState()`（相手盤面の保存）**
- ショップ開始（`OnTurnStart(ActivePlayer.Player)`）で `IsBattlegroundsCombatPhase = false`
- GameEntity の `TURN` は **奇数 = ショップ、偶数 = 戦闘**

→ **プラグインの方針**: `OnUpdate`（100ms）で `Core.Game.IsBattlegroundsCombatPhase` の false→true を監視して「戦闘開始」とし、その時点で自盤面と相手盤面（`Entities` のうち `IsMinion && IsInPlay && IsControlledBy(Opponent.Id)`、または `GetBattlegroundsBoardStateFor(相手ヒーローId)`）をスナップショットする。`OnTurnStart(Opponent)` でも代替可能。戦闘結果は次のショップ開始時の HP/ARMOR 差分 + `OnEntityWillTakeDamage`（ヒーロー対象）で判定。

### 3.8 Bob's Buddy（確認済み: `BobsBuddy/BobsBuddyInvoker.cs` + リフレクション）

- `BobsBuddyInvoker` は **`internal class`**（リフレクションで `IsPublic=False` を確認）。public API は無い
- 取得経路（リフレクション）: `typeof(Core).Assembly.GetType("Hearthstone_Deck_Tracker.BobsBuddy.BobsBuddyInvoker")` → `static GetInstance(Guid gameId, int turn, bool createInstanceIfNoneFound)` を `createInstanceIfNoneFound=false` で呼ぶ → `Output` プロパティ
- `Output` の型 `BobsBuddy.Simulation.Output`（BobsBuddy.dll、**public**）のフィールド: `float winRate, tieRate, lossRate, myDeathRate, theirDeathRate, avDamage, medianDamage; int simulationCount, friendlyHealth, opponentHealth; List<> damageResults`
- インスタンスのキーは `"{gameId}_{turn}"`、`turn` は `GetTurnNumber()`。ゲームが変わると全インスタンス破棄
- 判定: **△ 取得可能だが internal 依存で HDT 更新に弱い**。Phase 3 でリフレクション + try/catch のオプション機能として実装し、失敗時は `null`（docs/log-schema.md の `bobs_buddy: null` 許容と整合）

### 3.9 Hearthstone Logs フォルダの実構成（確認済み: 実機）

- **`%LOCALAPPDATA%\Blizzard\Hearthstone\Logs` は存在しない**。ログの実体は **`C:\Program Files (x86)\Hearthstone\Logs\`**（HDT の `config.xml` の `HearthstoneDirectory` + `HearthstoneLogsDirectoryName`。`hdt_log.txt` にも "Using Hearthstone log directory 'C:\Program Files (x86)\Hearthstone\Logs'" と記録）。`%LOCALAPPDATA%\Blizzard\Hearthstone\` にあるのは `log.config`, `options.txt`, `Cache`, `Exceptions` のみ
- 構成は **セッション毎のサブフォルダ** `Hearthstone_YYYY_MM_DD_HH_MM_SS\`（起動時刻）。中に `Power.log`, `LoadingScreen.log`, `Achievements.log`, `Gameplay.log`, ほか 10 個前後の `.log`。HDT は `CreationTime` が最新のサブフォルダを監視する（`HearthWatcher/LogReader/LogFileWatcher.cs`）
- サイズ: 1 セッション（約 1 時間、BG 2 ゲーム）で **Power.log 171.8 MB**。6 セッションで合計 1.5 GB。**Power.log をそのままコピーする設計は不可**（ゲーム単位の切り出しが必須）。切り出しは `Core.Game.PowerLog`（HDT がゲーム単位で保持している行リスト）を使えば自前パース不要
- `Program Files (x86)` 配下だが、現在のユーザーで書き込み可能だった（テストファイル作成成功）。ただしプラグインはここへ書かない
- `log.config`（`%LOCALAPPDATA%\Blizzard\Hearthstone\log.config`）は HDT が管理し、`[Power] LogLevel=1 FilePrinting=True Verbose=True` 等が設定済み
- Power.log の先頭は `GameState.DebugPrintGame() - GameType=GT_BATTLEGROUNDS`, `ScenarioID=3459` などで、CREATE_GAME 単位でゲームを区切れる

### 3.10 HDT の Replays / Games フォルダ（確認済み: 実機 + ソース）

- **`Replays` フォルダも `Games` フォルダも存在しない**（`%APPDATA%\HearthstoneDeckTracker\` 直下には `Backups`, `CardDefs`, `Images`, `Logs`, `Plugins` のみ）
- 理由: `.hdtreplay` の保存は `Config.RecordReplays && RecordCurrentGameMode` が条件で、`RecordCurrentGameMode` は Ranked/Casual/Arena/Brawl/Friendly/Practice/Spectator/Other の設定しか見ておらず **Battlegrounds は含まれない**。→ **HDT の Replays 機能は BG では使えない**。docs/log-schema.md の「HDT の Replays が使えるならそれを参照」は不成立。`Core.Game.PowerLog` を自前で書き出す（Phase 4）
- ゲーム単位の `GameStats` は `Games` フォルダではなく `DefaultDeckStats.xml`（クラス別デッキ統計）に入っていたが、**HDT 1.58.2（2026-09-18 コミット 458b8a2 "stop recording phantom Battlegrounds deck stats"）以降、BG は記録されない**（`GetDeckStats("BaconPHhero")` が null を返す）。実機の `DefaultDeckStats.xml` は 2026-09-21 の 1 件が最後で、それ以前も 1 日 1 件程度しか残っていない（`DataIssueResolver` 等で間引かれた模様）。→ **補完ソースとして当てにしない**
- 代わりに **`%APPDATA%\HearthstoneDeckTracker\BgsLastGames.xml`**（`Utility/Battlegrounds/BattlegroundsLastGames.cs`, public クラス）が BG 専用の戦績ファイル。1 ゲーム 1 `<Game>` 要素で属性 `Player`（アカウント ID）, `StartTime`, `EndTime`（ISO 8601 "o" 形式）, `Hero`（スキンを元ヒーローに正規化した CardId）, `Rating`, `RatingAfter`（取れなかったときは属性自体が無い）, `Placemenent`（typo のまま）, `FriendlyGame`, `Duos`、子要素 `FinalBoard/Minion/{CardId, Tags[@Tag=数値,@Value]}`。実機には 2026-09-19 以降の **106 ゲーム** が入っており、ゲーム中に更新される。→ `mmr_after` のフォールバック、および **過去ログのバックフィル**（Phase 5 以降のオプション）に使える。ただし `LobbyRawHeroDbfIds` や順位以外のターン情報は無い
- HDT の `Backups` フォルダには `config.xml` 等のバックアップ zip が入る（プラグインとは無関係）

### 3.11 参考プラグイン 2 件の安全性評価（docs/hdt-research.md §2.3 チェックリスト）

いずれも **ソースを読むだけ。バイナリはインストールしない**。clone 先は作業用の別フォルダ（例: `%USERPROFILE%\src\ref\`）。

| # | 観点 | benluiwj/Battlegrounds-Match-Data | Reign-in-blood/HDT-FinalStatsPlugin |
|---|---|---|---|
| 1 | ソース公開 / 再現ビルド | 公開。旧式 csproj（HDT 1.11.9 + HearthDb 16.0.5 を参照）。**現行 HDT では参照解決からやり直しが必要** | 公開。SDK スタイル csproj（`Microsoft.NET.Sdk.WindowsDesktop`, net472, x64）。`Build.bat` が `HearthstoneDeckTracker.exe` と `HearthDb.dll` を `lib\` にコピーしてから MSBuild する |
| 2 | 最終コミット | **2022-03-03**（42 コミット）。HDT の API 変更（GameV2 の nullable 化等）を経ているため、そのままでは動かない可能性が高い | **2026-08-03**（33 コミット）。HDT 1.58 系と同時期。MIT ライセンス |
| 3 | ネットワーク送信 | **あり**。`GraphqlConnector.cs` が `HttpClient` で `http://bgstats.cintrest.com/graphql` に **平文 HTTP** で戦績を POST（設定で ON/OFF）。`SpreadsheetConnector.cs` が Google Sheets API（OAuth）へ送信。`Helper.TryOpenUrl` でブラウザを開く | **なし**（`HttpClient`/`WebClient`/`WebRequest`/URL の grep ヒット無し） |
| 4 | ファイル書き込み範囲 | 設定 JSON（プラグイン設定フォルダ）、CSV（設定した任意パス）、`GameLogManager` のテキストログ | `Pictures\Hearthstone final board\*.png`（スクリーンショット）、テキストログの `File.AppendAllText`。一時ファイル→`File.Move` でアトミック書き込みしている |
| 5 | 追加依存 DLL | **Google.Apis.* 1.42（6 本）、Newtonsoft.Json 10.0.2、MahApps.Metro 1.5.0 を NuGet で取り込み**。後者 2 つは HDT 同梱版と衝突しうる（docs/hdt-research.md §2.1 の禁止事項に抵触） | なし（HDT 同梱の HearthstoneDeckTracker.exe と HearthDb.dll のみ、`Private=false`） |
| 6 | Process.Start / Registry / フック / DllImport | なし | なし |
| 7 | HDT 同梱 DLL の上書き | 上記 5 の通り、配布 zip に Newtonsoft.Json / MahApps.Metro を同梱している可能性が高い（配布 zip は未取得のため未確認） | なし |

**結論**: 両方とも導入はしない（docs/hdt-research.md §1 の決定どおり）。参考にする点:
- Battlegrounds-Match-Data: `OnTurnStart(ActivePlayer)` でショップ/戦闘を区別し、戦闘開始時に自分と相手の盤面を文字列化して保存する流れ。`OnEntityWillTakeDamage` で「どちらのヒーローが被弾したか」から戦闘結果を出す。MMR は `OnInMenu` の後に `BattlegroundsRatingInfo.Rating` をポーリング
- HDT-FinalStatsPlugin: csproj の書き方（`HintPath` + `Private=false`）、`Core.Game.PowerLog` を逐次パースして購入/プレイ/バトルクライ等を数える方式、`RESOURCES_USED` の増分によるゴールド消費、ショップ Entity の入れ替わり検出によるリロール推定、一時ファイル→Move のアトミック書き込み。4000 行超の単一ファイルなので構造は真似しない

### 3.12 `dotnet build`（net472 + HDT 参照）の結果（確認済み）

- **`dotnet build` で成功**（.NET SDK 9.0.318、警告 0、エラー 0、約 1 秒）。MSBuild / Visual Studio は不要だった
- 構成: `plugin/HsbgKansousen/HsbgKansousen.csproj` は `Microsoft.NET.Sdk` + `net472` + `UseWPF=true` + `PlatformTarget=x64`。HDT 同梱アセンブリ（`HearthstoneDeckTracker.exe`, `HearthDb.dll`, `Newtonsoft.Json.dll`）を `HintPath` + `Private=false` で参照。`HdtPath` は `plugin/Directory.Build.props` が `-p:HdtPath=` → 環境変数 `HDT_PATH` → 既定 `app-1.58.3` の順で決め、無ければビルドエラーで案内する
  （2026-09-28 のプラグイン 0.8.0 から、既定は `%LOCALAPPDATA%\HearthstoneDeckTracker\app-*` のうち最大バージョンの自動検出。§3.16）
- 出力は `bin\Release\HsbgKansousen.dll`（57 KB）と `.pdb` のみ。HDT 同梱 DLL はコピーされない（同梱禁止の規約を満たす）
- リフレクションで確認: 参照アセンブリは mscorlib / System.Core / HearthstoneDeckTracker / HearthDb / Newtonsoft.Json / PresentationFramework 系のみ。`HsbgKansousen.HsbgKansousenPlugin` は public で `IPlugin` を実装、引数なしで生成できる
- `scripts/deploy-plugin.ps1` が「HDT 起動中なら中断 → 最新 `app-*` を検出 → `dotnet build` → `%APPDATA%\HearthstoneDeckTracker\Plugins\HsbgKansousen\` にコピー」を行う

ビルド中に判明した **HDT の API 上の落とし穴**（`API/ActionList.cs`）:
- `GameEvents.*` の `ActionList` には **`Remove` が無い**。`Add(action)` は `new StackTrace().GetFrame(1)` で **呼び出し元メソッドの型** を取り、それが `IPlugin` 実装クラスと一致したときだけ「そのプラグインのハンドラ」として記録する。プラグインが無効化されると、次のイベント実行時に HDT がそのハンドラを外す
- したがって **`GameEvents.*.Add(...)` は `IPlugin` 実装クラスのメソッド内から直接呼ぶ**。別クラス（`Capture/GameSession` 等）に登録を委譲すると紐付かず、無効化後も呼ばれ続ける。docs/architecture.md §2.2 の「`HsbgKansousenPlugin.cs` はイベント登録/解除のみ」はこの制約とも整合する
- `OnUnload` ではハンドラを外せないので、ロジック側に `Enabled` フラグを持たせて無害化する
- 各ハンドラの実行時間が 2000ms を超えると警告ログが出る（`PluginManager.MaxPluginExecutionTime`）。ハンドラ内の例外は HDT が捕捉してログに書くが、自前でも捕捉する

### 3.13 docs/architecture.md・docs/log-schema.md への反映事項（Phase 1 で確定したこと）

| 対象 | 変更 |
|---|---|
| docs/architecture.md §2.1 | BG 判定は `Core.Game.IsBattlegroundsMatch`（Solo/Duos の区別は `IsBattlegroundsSoloMatch` / `IsBattlegroundsDuosMatch`） |
| docs/architecture.md §2.3 | 戦闘開始 = `IsBattlegroundsCombatPhase` の false→true（`OnUpdate` 監視）または `OnTurnStart(ActivePlayer.Opponent)`。戦闘終了 = `OnTurnStart(ActivePlayer.Player)`。ヒーロー確定 = `BattlegroundsHeroPickState.PickedHeroDbfId` または `Player.Hero` |
| docs/architecture.md §2.4 | `mmr_after` は `OnGameEnd` 時点では 0。同じ `GameStats` オブジェクトをポーリング（最大 ~20 秒）→ それでも 0 なら `BgsLastGames.xml` から補完 |
| docs/architecture.md §2.2 `PowerLogExtractor` | Power.log ファイルを切り出すのではなく `Core.Game.PowerLog`（List<string>）を書き出す。HDT の Replays 機能は BG で使えない |
| docs/log-schema.md `power.log` | 「HDT の Replays 機能で同等物が得られるなら」の但し書きを削除し、`Core.Game.PowerLog` 由来と明記（1-9 で反映） |
| docs/log-schema.md `bobs_buddy` | リフレクション経由のオプション。null 許容のまま |
| docs/log-schema.md `player.hero_choices` | 取得可能（`OfferedHeroDbfIds` は DbfId なので CardId へは `HearthDb.Cards.GetFromDbfId` 等で変換） |
| docs/log-schema.md `game.anomaly` | `BACON_GLOBAL_ANOMALY_DBID`（DbfId）→ CardId 変換 |
| docs/log-schema.md `game.hearthstone_build` | `GameStats.HearthstoneBuild` で取れる |
| CLAUDE.md | Hearthstone のログ場所を `C:\Program Files (x86)\Hearthstone\Logs\Hearthstone_<起動時刻>\` に修正（HDT の `config.xml` `HearthstoneDirectory` を参照するのが正） |
| docs/maintenance.md R8 | `GameStats.PlayerName` と Player Entity の `Name` は BattleTag なので、ダンプ以外のログには出さない |

### 3.14 チェックリスト

- [x] インストール済み HDT のバージョン: **1.58.3**（ソースは master `509bb0b`。タグ無し）
- [x] `IPlugin` のシグネチャ（実ソースで確認）: §3.2
- [x] `API.GameEvents` で利用可能なイベント一覧: §3.3
- [x] `GameStats` の Battlegrounds 関連プロパティ名: `BattlegroundsRating`, `BattlegroundsRatingAfter`, `BattlegroundsDetails`（`FinalPlacement`, `LobbyRawHeroDbfIds`, `AnomalyDbfId`, `DeityDbfId`, `FriendlyRawHeroDbfId`）, `BattlegroundsRaces`（§3.5）
- [x] BG 関連 GameTag の実名: `PLAYER_LEADERBOARD_PLACE`(1373), `PLAYER_TECH_LEVEL`(1377), `NEXT_OPPONENT_PLAYER_ID`(1360), アノマリー `BACON_GLOBAL_ANOMALY_DBID`, トリンケット `BACON_TRINKET`(3407) / `BACON_FIRST_TRINKET_DATABASE_ID`(3741) / `BACON_SECOND_TRINKET_DATABASE_ID`(3742), クエスト `QUEST_REWARD_DATABASE_ID` / `BACON_HERO_QUEST_REWARD_DATABASE_ID`(2713)（§3.6）
- [x] Hearthstone Logs フォルダの実構成: `C:\Program Files (x86)\Hearthstone\Logs\Hearthstone_YYYY_MM_DD_HH_MM_SS\Power.log`（§3.9）
- [x] HDT の Replays / Games フォルダの実構成と内容: **どちらも存在しない**。BG は `.hdtreplay` 対象外。BG 戦績は `BgsLastGames.xml`（§3.10）
- [x] `dotnet build` で net472 + HDT 参照のクラスライブラリがビルドできたか: **できた**（SDK 9.0.318、警告 0。§3.12）
- [x] 参考プラグイン 2 件の安全性評価結果: §3.11

### 3.15 実プレイダンプによる確定（2026-09-25、1 ゲーム / 12 ターン / 6 位）

ダンプ: `Documents\HSBG-Logs\.spike\20260925-155622_c4802939\`（58 ファイル、最大 2.5 MB）。プラグインログにエラー無し、HDT ログに実行時間警告無し。
§2.2 の表を確定させた根拠と、設計に影響する挙動:

- **イベントの順序（1 ターン）**: `OnTurnStart(Player)`（ショップ開始。この時点で `IsBattlegroundsCombatPhase` はまだ true、`RESOURCES*` は前ターンの値）→ 約 100ms 後に `IsBattlegroundsCombatPhase=false` → …ショップ… → `OnTurnStart(Opponent)`（相手はまだ Bob、`RESOURCES_USED` はこのターンの消費で確定）→ 約 1.5 秒後に `IsBattlegroundsCombatPhase=true`（**相手ヒーローと相手盤面がここで揃う**）→ 戦闘中に `OnEntityWillTakeDamage`（ヒーローへの最終ダメージ。**2 回発火**）→ 次の `OnTurnStart(Player)`
- **ゲーム終了**: `OnGameEnd` 時点で `BattlegroundsRating` 確定、`BattlegroundsRatingAfter=0`、`BattlegroundsDetails` 空。次の `OnUpdate`（0.2 秒後）で `BattlegroundsRatingAfter`（終了後の値） と `BattlegroundsDetails`（`FinalPlacement=6`, `LobbyRawHeroDbfIds` 9 件で自分が重複）が埋まっていた。約 8 秒後に `OnInMenu`、その後 HDT が `Entities` をクリアし `IsBattlegroundsMatch=false` に戻る（この後に `IsBattlegroundsCombatPhase` の遷移が 1 回来るので、ゲーム終了後は監視を止める）
- **自ヒーロー Entity の探し方**: `OnGameEnd` 時点で `IsControlledBy(Player.Id) && HasTag(PLAYER_LEADERBOARD_PLACE)` に一致する Entity が **2 つ**あった（本物 id=97 GRAVEYARD place=6、結果画面用の複製 id=18365 SETASIDE place=5）。`PlayerEntity.GetTag(HERO_ENTITY)` で本物を引く
- **ターン番号**: `GetTurnNumber()` は 1〜12 で GameEntity `TURN` の奇数=ショップ/偶数=戦闘と整合。`GameStats.Turns=12`
- **HP/ダメージの整合**: 被ダメ 3/8/12/15/15/15、与ダメ 2/5/5/10/10/15 が各ターンの自 HP 差分・相手 HP と一致。ターン 10 のアーマー 5 は被ダメ 15 のうち 5 を吸収（HP 37→27）
- **順位タグ**: `PLAYER_LEADERBOARD_PLACE` はゲーム中「現在のリーダーボード順位」（HP 順）。自分は 11 ターン目まで 1 位表示、12 ターン目開始で 5 位、敗退で 6 位確定
- **Entity 数**: ターン 12 で約 2,500（REMOVEDFROMGAME が 2,100）。全 Entity ダンプはスパイク限定とし、本番は盤面・ヒーローだけをスナップショットする
- **Bob's Buddy**: 12 ターンすべてで前ターン分の Output を取得できた。戦闘開始時点ではまだ null（シミュレーション中）
- **未定義タグ**: `TAG_1037`, `TAG_1068`, `TAG_4901`, `TAG_3085`, `TAG_479` など HearthDb 36.6.0 に名前の無いタグが多数あるが、必要な情報はすべて名前付きタグで足りた
- **プレイヤー名**: プレイヤー Entity の `Name` は BattleTag、ヒーロー Entity の `Name` は日本語カード名（クライアントのロケール）。ログには `card_id` と HDT の `Card.Name`（英語）を書き、BattleTag は書かない
- **Power.log**: `Core.Game.PowerLog` の行に含まれる日本語カード名は HDT 側で文字化けしている（CardId は無傷）。補助ファイルとして保存する価値はある（再抽出は CardId ベースで行う）

### 3.16 HDT 1.58.4 の差分（2026-09-28、バイナリ比較で確認）

HDT 1.58.4（FileVersion 1.58.4.9001、2026-09-28 リリース）は自動更新で入った。GitHub の master は `v1.58.3`（`509bb0b`）のままでソース未公開、
HDT-Releases のリリースノートも空だったので、`app-1.58.3` と `app-1.58.4` のバイナリを比べた（手順は docs/maintenance.md「HDT のアップデート後」）。

- リリースノートは exe に埋め込まれている（逆コンパイル結果の `resources/changelog.md`。HDT の更新通知と同じ内容）。1.58.4 は
  「Smart App Control（Windows 11）で機能がブロックされたときのクラッシュ修正」「異形の種族表示で神祇が時々違っていた件の修正」「戦闘の勝率計算の修正」
- 変わったファイルは `HearthstoneDeckTracker.exe`（+1 KB）と BobsBuddy（`BobsBuddy.dll` / `BobsBuddy.Common.dll` 1.78.1 → 1.80.1）だけ。HearthDb / Newtonsoft.Json は同一
- `HearthstoneDeckTracker.exe` の型・メンバー・文字列リテラルは同一（公開 API、`GameEvents` に変更なし）。IL の長さが変わったメソッドは 4 つで、逆コンパイルした差分:
  - `BattlegroundsDeityState.GetSigil`: `!x.IsInZone(Zone.REMOVEDFROMGAME)` を追加（戦闘の後片付けで REMOVEDFROMGAME に移った相手 Sigil のコピーを読まない。docs/log-schema.md「神祇」）
  - `DeitySizeCounter.HandleTagChange`: REMOVEDFROMGAME の Sigil の `BACON_EVOLUTION_CARD_ID` 変化を無視（後片付けでクトゥーンに戻る件）
  - `BobsBuddyUtils.GetDeityFromSigil`: Sigil の神祇カードが金色版（`Cards.TripleToNormalCardIds` にある）なら金色のミニオンにする。プラグインの `golden` 判定と同じ考え方
  - `BobsBuddyUtils.GetMinionFromEntity`: `minion.div = entity.GetTag(DIVINE_SHIELD)`（有無 → 枚数）。闇のパラドックス（`BG36_360t4`）は `DIVINE_SHIELD=3` になる
- BobsBuddy 1.80.1: 古なる者の仮面（`MaskOfAncientOnes`）、`TrousersOff` エンチャント、闇のパラドックスの処理を追加・変更。
  プラグインがリフレクションで読む `BobsBuddyInvoker.GetInstance` / `Output` と `BobsBuddy.Simulation.Output` のフィールドは変わっていない
- プラグイン 0.7.0 は 1.58.4 でそのまま読み込まれ（plugin.log にエラーなし）、1.58.4 を参照したビルドも警告 0。これを受けた 0.8.0 の変更:
  相手 Sigil の探索で REMOVEDFROMGAME を除外（HDT と同じ。値 0 は従来から捨てていたので記録値は変わらない）、ミニオンに `divine_shields`（schema 1.4.0）、
  `Directory.Build.props` の `HdtPath` 既定値を最新の `app-*` の自動検出に変更（HDT は更新のたびに古い `app-*` を消すため）
- Bob's Buddy の予測は 1.58.4 から金色の神祇と重ねた聖なる盾を正しく扱う。予測の当たり方を比べるときは `generator.hdt_version` で前後を分ける

### 3.17 HDT 1.58.5〜1.58.10 の差分（2026-10-11、ソースで確認）

GitHub の master に `v1.58.4`（`964e069d`）〜`v1.58.10`（`3e4ba05d`）のコミットが揃っていたので、`git log` / `git diff 964e069d 3e4ba05d` で確認した。
同梱 DLL: HearthDb 36.6.0 → **36.6.3**（1.58.9 と 1.58.10 は同一）、BobsBuddy 1.80.1 → 1.88.6（1.58.9）→ **1.90.0**（1.58.10）。Newtonsoft.Json / HearthMirror / MahApps.Metro は同一。

- **プラグインに影響する変更は無い**: `API/` の変更は `LogEvents.OnPowerGamestateLogLine` の追加だけ（`GameEvents` は同一）。`Hearthstone/Entities/`・`Plugins/`・`GameStats` も同一。
  プラグインが使う `GameV2` のメンバー（`OfferedEntityIds`、`BattlegroundsHeroPickState`、`Trinkets`、`CurrentBattlegroundsRating` 等）はそのままで、1.58.10 を参照したビルドも警告 0。
  1.58.9 / 1.58.10 で遊んだ 10-07 以降のゲームで `plugin.log` に警告・エラーは無く、Bob's Buddy もリフレクションで毎ターン取れている（`simulations` = 10000。`BobsBuddy.Simulation.Output` の
  `winRate` / `tieRate` / `lossRate` / `theirDeathRate` / `myDeathRate` / `avDamage` / `simulationCount` は残っている）
- MMR: `fc3c8424`（1.58.9）で `BattlegroundsRatingInfo` のキャッシュを試合後に更新、`63cc322b`（1.58.6）でヒーロー選択中の投了でも試合後 MMR を読む。
  プラグインは `GameStats.BattlegroundsRatingAfter` を読むだけなので変更不要（取れる場面が増える方向）
- 表示の変更: BG のミニオンに勝鬨（`GameTag.BACON_RALLY`）のアイコン（`d8c6516f`）、`DIVINE_SHIELD > 1` を「強化された聖なる盾」として表示（`61ce4957`。プラグインは 0.8.0 から枚数を記録済み）。
  → **プラグイン 0.9.0 でミニオンの `keywords` に `rally` と `deathrattle` を追加**（schema 1.5.0。タイタス・天空孵りの脱走者など、断末魔・勝鬨を倍にするカードの分析用）
- `LogEvents.OnPowerGamestateLogLine`（`c0d59d64`、1.58.7）: Power.log の GameState 行を 1 行ずつ渡すイベント。ショップ行動を「推定」ではなく `BLOCK_START BlockType=PLAY` などから正確に取れる可能性があるが、
  今の差分検出は受け入れ基準（±1）を満たしており、power.log はゲームごとに保存しているので後から解析できる。**今は使わない**（登録は `IPlugin` 実装クラスから。§3.12）
- 対象外: 酒場のプール（`BattlegroundsMinionPool`）は HearthMirror（メモリ読み取り）から取るので使わない（CLAUDE.md「やらないこと」）。
  「Tavern Markers / ピン留め」は HDT の HSReplay 連携の表示機能でゲームの仕組みではない。Sir Finley のヒーローパワー選択の統計表示、悪夢の恵み（Golemancy / Sacrifice）の
  Bob's Buddy 側の補正、トレイメニュー等は記録に関係しない

### 3.18 HDT 1.59.3 の差分（2026-10-17、P-20 / P-21 に必要な範囲だけ確認）

HDT 1.59.3（FileVersion 1.59.3.9015）は 2026-10-14 に自動更新で入った。GitHub の master は `v1.59.2`（`555499e6`）まで。
同梱 DLL: HearthDb 36.6.3 → **37.0.0**、BobsBuddy / BobsBuddy.Common 1.90.0 → **1.92.7**、Newtonsoft.Json 13.0.3 → **13.0.4**（アセンブリ版は 13.0.0.0 のまま）。HearthMirror / MahApps.Metro は同一。

- カード ID の扱い: ソース（`3e4ba05d..555499e6`）の `LogReader/`・`Hearthstone/Entities/` に差分なし。1.59.3 は `app-1.58.10` と `app-1.59.3` を逆コンパイルして比べ、
  `PowerHandler` / `TagChangeHandler` / `Entity` / `EntityInfo` が同一（CHANGE_ENTITY は今も `CardId` を変えず `Info.LatestCardId` に入れる。P-20）
- Newtonsoft.Json 13.0.4: 1.59.3 を参照してビルドしたプラグインは 13.0.4 にしか無いオーバーロード（`JToken.ToString(Formatting)`）に束縛されるので、
  テストが 13.0.3 のままだと `MissingMethodException` で 6 件落ちた。テストの `PackageReference` を 13.0.4 に上げた（P-21）。実機は HDT の 13.0.4 を読むので影響なし。
  13.0.4 は 13.0.3 のメソッドも持つので、1.58.10 を参照したビルドのテストもそのまま通る
- 0.9.3 / 0.9.4 は 1.59.3 でそのまま読み込まれ、10-14 以降の 8 ゲームで `plugin.log` に警告・エラーなし。`API/`・`GameEvents`・BobsBuddy 1.92.7 の変更はまだ通して見ていない

### 3.19 Hearthstone のクラッシュ / 切断からの再接続（2026-10-18、P-22。ソース v1.59.2 + 実機 1.59.3 のログで確認）

2026-10-17 17:45 のゲームで Hearthstone が T11 の戦闘中に落ち（`ExceptionReporter.log` に `NullReferenceException` at `AttackSpellController.ActivateSpellDeathState`）、
再起動して同じゲームに戻った。HDT は落ちていない（`plugin.log` にプラグインの読み込みが無い）。10-18 00:57 のゲームでも同じことが起きた。

- `GameEventHandler.HandleGameStart`: 再接続でも `--- Game start ---` になり、`_game.Reset()`（`PowerLog.Clear()`。新しい `GameStats` で `GameId` も新しい Guid）→
  `MetaData.ServerInfo = Reflection.Client.GetServerInfo()` → `GameEvents.OnGameStart.Execute()` の順。続いて LoadingScreen の「Joined after mulligan」から
  `HandleGameReconnect` が `CurrentGameStats.IsReconnect = true` にする（非同期で、`OnGameStart` より後）。`OnGameEnd` は落ちたゲームについては来ない
- `MetaData.ServerInfo` は HearthMirror の `GameServerInfo`（`GameHandle` UInt32、`Resumable`、`ClientHandle` 等。1.59.3 同梱の HearthMirror.dll をリフレクションで確認）。
  `GameHandle` はサーバー側のゲーム番号で、Hearthstone の `GameNetLogger.log` の `Network.GotoGameServe() - ... game=<n> ... reconnecting=<bool>` と同じもの。
  再接続でも変わらない（17:45 のゲームは `game=1936 reconnecting=False` → 18:05:51 `game=1936 reconnecting=True`）。
  プラグインは `HearthMirror.dll` を参照に加えた（HDT 同梱。zip には入れない）。`OnGameStart` の時点で 0 でない値が取れるかは 0.9.5 の `plugin.log`（`OnGameStart: ... handle=`）で確認する（要検証）
- `GameV2.StoredPowerLogs`（GameHandle ごとの PowerLog）と `StoreGameState()` は internal で、v1.59.2 のソースに呼び出し元が無い。再接続前の Power.log は HDT に残らないので、
  0.9.4 の power.log.gz は再接続後の行だけだった（`CREATE_GAME` から始まらない）。再接続前の行は Hearthstone 本体の `Logs\Hearthstone_<起動日時>\Power_old.log` に残っていた
- 再接続後、HDT は普通に `OnTurnStart` を出す（17:45 のゲームは再接続 6 秒後に T12 の `OnTurnStart(Player)`）。再接続で全 Entity が作り直されるので、
  `UpdateLobby` は再接続中に脱落した相手を見つける（そのときの eliminated_turn は「今のターン − 1」。再接続前に脱落を見ていた相手は前の値のまま）
- Bob's Buddy の結果は `BobsBuddyInvoker.GetInstance(gameId, turn)` で HDT のゲーム ID ごとに持たれるので、再接続前のターンは前の `GameId` で引く
