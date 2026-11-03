# TASKS.md

今どこにいて、次に何をするか。完了したら `[x]` にし、判明したことは docs に追記する。

## 新しいセッションの始め方（Claude Code 向け）

1. `CLAUDE.md` → この `TASKS.md` → 作業に関係する docs（設計は docs/architecture.md、ログ仕様は docs/log-schema.md、HDT の挙動は docs/hdt-research.md §3）の順に読む
2. 検証コマンド（`CLAUDE.md`）が緑であることを確かめてから始める
3. 作業の区切りで、変更したファイルと推奨するコミットメッセージを報告して止まる。コミットは許可を得てから

## 現在の状態

- プラグイン v0.13.0（0.10.x までの名前は HsbgLogger）、ログ schema 1.8.0。動作確認した HDT: 1.59.x
- 公開の準備中。GitHub Releases に v0.11.0（2026-10-31）と v0.13.0（2026-11-04）を置いた（どちらも HDT 1.59.7 でビルド。0.12.0 は配布していない）。
  リポジトリの public 化はまだ
- 対応する hsbg: プラグイン 0.11.0 ↔ hsbg 0.1.0、0.13.0 ↔ hsbg 0.2.0（どちらもリリース済み。ログの版が変わるときは hsbg を先にリリースする）

## タスク

- [x] 最初のリリース: `package-plugin.ps1` の zip、`SHA256SUMS.txt`、`game-log.schema.json` を GitHub Releases に置く。
      リリースノートに「ビルドに使った HDT のバージョン」「schema_version」「変更点」「インストール手順」を書く
      （v0.11.0。zip を README の手順で入れて 1 ゲーム確認済み。手順は docs/development.md「リリースの手順」）
- [x] CI（`.github/workflows/checks.yml`）: check_private と schema の JSON の検査（ビルドは下のタスク）
- [ ] リリースの CI 化: GitHub Actions（windows-latest）で HDT の配布物を取得して参照し、build + test + package。
      **要検証**: HDT-Releases の `*-full.nupkg` が各版にあり、中に `HearthstoneDeckTracker.exe` と参照 DLL が揃うか。
      HDT のバイナリはリポジトリにもリリースにも入れない（HDT のライセンスは All Rights Reserved）
- [ ] Visual Studio の無い Windows で、docs/development.md の手順だけで build / test が通るか確かめる（net472 の参照アセンブリ）
- [x] Issue テンプレート: HDT とプラグインの版、`plugin.log` の該当部分。**`*.power.log.gz` は貼らない**（ロビー全員の BattleTag が入る）。
      ログを添えるときは `scripts/anonymize_log.py` を通した `.json` だけ
      （`.github/ISSUE_TEMPLATE/`: 不具合・要望のフォーム。`plugin.log` のパスにはユーザー名が入るので伏せてもらう。空の Issue は無効）
- [ ] 公開: リポジトリを public にする（最初のリリースと、README の手順の確認の後）。HDT Wiki の Available Plugins への掲載依頼は任意
- [x] 甲虫のステータス（schema 1.7.0、プラグイン 0.12.0）: `turns[].beetle`（ショップ終了時・戦闘後）と `combat.opponent_beetle`（戦闘中に見えたとき）。
      events.jsonl の `shop_end` / `combat_end` にも。読み方は docs/log-schema.md「甲虫」、調査は docs/hdt-research.md §3.20
- [x] 甲虫の実機確認: 甲虫を育てた 1 ゲームで `turns[].beetle` が power.log からの再抽出（hsbg の `powerlog.parse_beetle`）と一致するか、`plugin.log` に警告が無いか
      （2026-11-03 18:08 のゲーム、HDT 1.59.7: 自分 11 ターン・相手 2 戦闘の全ての値が一致。警告なし。OnUpdate 平均 0.377ms）
- [x] 他の「この対戦中」の強化（schema 1.8.0、プラグイン 0.13.0）: `blood_gem`・`tavern_spell`・`tavern_minion`・`tavern_elemental` と `opponent_*`。
      甲虫と同じ形・同じ仕組み（`Capture/PlayerBuffs.cs`）。読み方は docs/log-schema.md「この対戦中の強化」
- [x] 0.13.0 の実機確認（2026-11-03 22:50 のゲーム。キルボアの構成、9 ターン）: 自分の `blood_gem`（4 ターン）と相手の `opponent_blood_gem` /
      `opponent_tavern_spell` / `opponent_tavern_minion`（4 戦闘）が power.log からの再抽出（`powerlog.parse_buffs`）とすべて一致。
      `plugin.log` に警告なし（OnUpdate 平均 0.170 ms）。自分の `tavern_spell` / `tavern_minion` / `tavern_elemental` はこのゲームでは出なかった（読み方は甲虫・血の宝石と同じ）
- [x] v0.13.0 のリリース（2026-11-04）: 先に hsbg 0.2.0 をリリースしてから。zip を README の手順で入れて 1 ゲーム確認済み
      （zip・Plugins・ビルドの DLL の SHA-256 が一致、ログは schema 1.8.0 で hsbg 0.2.0 が読める。戦闘中に初めて付いた血の宝石も power.log と一致）
- [ ] 設定画面に「リリースページを開く」リンク（更新の確認。自動の照会は外部通信になるので入れるなら既定 OFF）

## 判明したこと・決めたこと

- HDT の挙動（イベントの順序、MMR 後値の遅延、再接続、Bob's Buddy の読み方など）は docs/hdt-research.md §3 と docs/architecture.md §2 に集約している
