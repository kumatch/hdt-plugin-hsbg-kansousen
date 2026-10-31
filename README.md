# hdt-plugin-hsbg-kansousen

Hearthstone Battlegrounds（BG）の **1 ゲームごとの記録（棋譜）** を、ローカルの JSON ファイルに自動で保存する
[Hearthstone Deck Tracker](https://hsdecktracker.net/)（HDT）のプラグインです。
保存した記録は、分析ツール [hsbg-kansousen](https://github.com/kumatch/hsbg-kansousen)（AI と一緒に自分のプレイを振り返る「感想戦」の道具）で読みます。

> English: an HDT plugin that writes one JSON log per Battlegrounds game (turns, shops, boards, combats,
> placement, MMR) to a local folder, for the analysis tool hsbg-kansousen. Windows only, no network access.

- 記録するもの: ヒーロー、ターンごとの HP・ティア・ゴールド・購入 / 売却 / リロール、ショップ終了時の盤面と手札、ショップの提示、戦闘の相手と結果、
  Bob's Buddy の予測、順位、MMR の前後（仕様は [docs/log-schema.md](docs/log-schema.md)）
- 記録しないもの: BattleTag、プレイヤー名、アカウント ID（ログの JSON には入りません）
- 外部への通信はしません。書くのは自分の PC のフォルダだけです
- 非公式のファンメイドツールです。Blizzard Entertainment、HearthSim とは関係ありません

## インストール

1. [Releases](https://github.com/kumatch/hdt-plugin-hsbg-kansousen/releases) から最新の `HsbgKansousen-<version>.zip` をダウンロードする
   （zip ごとでよい。展開しない）
2. HDT を起動し、Options > Tracker > Plugins の画面に zip をドラッグ & ドロップする
3. HDT を再起動し、同じ画面で「HsbgKansousen」にチェックを入れる
4. BG を 1 ゲーム遊ぶと、`%USERPROFILE%\Documents\HSBG-Logs\YYYY\MM\` に記録ができます

プラグインが一覧に出ないときは、ダウンロードしたファイルのプロパティで「ブロックの解除」をするか、PowerShell で
`Unblock-File <ファイル>` を実行してください。

リリースノートに、動作を確かめた HDT のバージョンを書いています。HDT の更新で動かなくなったときは Issue で知らせてください。

## 設定

Options > Tracker > Plugins の「Settings」ボタン（または HDT の Plugins メニューの「HsbgKansousen」）で設定画面を開きます。

- **ログの保存先**（既定 `%USERPROFILE%\Documents\HSBG-Logs`）
- events.jsonl（ショップの操作の時系列）、Power.log の写し、その gzip 圧縮、ショップ終了時の手札の記録の ON / OFF

変更は次のゲームから効きます。設定ファイルは `%APPDATA%\HearthstoneDeckTracker\HsbgKansousen\config.json` です。

> **注意**: Power.log の写し（`*.power.log.gz`）には、同じロビーの全員の BattleTag が入っています。
> Issue などで人に渡さないでください。記録を共有するときは `.json` だけを、`scripts/anonymize_log.py` で日時・MMR を置き換えてから渡してください。

## 自分でビルドする

開発環境の作り方は [docs/development.md](docs/development.md)。要点:

```powershell
cd plugin
dotnet build HsbgKansousen.sln -c Release
dotnet test HsbgKansousen.sln -c Release --no-build
# HDT を終了してから、ビルドして HDT のプラグインフォルダへ配置
..\scripts\deploy-plugin.ps1
# 配布用の zip（dist\HsbgKansousen-<version>.zip）
..\scripts\package-plugin.ps1
```

HDT のインストール先（`%LOCALAPPDATA%\HearthstoneDeckTracker\app-*` の最新版）を自動で参照してビルドします。

## 文書

| 文書 | 内容 |
|---|---|
| [docs/architecture.md](docs/architecture.md) | プラグインの設計（HDT のイベント、内部構造、堅牢性、設定） |
| [docs/log-schema.md](docs/log-schema.md) | ログの仕様（分析ツールとの契約。変えるときは `schema_version` を上げる） |
| [docs/hdt-research.md](docs/hdt-research.md) | HDT の API とログの挙動の調査結果 |
| [docs/development.md](docs/development.md) | 開発環境の作り方 |
| [docs/maintenance.md](docs/maintenance.md) | リスクと保守（HDT / Hearthstone の更新後の調べ方） |

## ライセンス

MIT（[LICENSE](LICENSE)）。HDT 本体（HearthSim）のバイナリ・ソースは含みません。ビルド時に手元の HDT を参照するだけです。
