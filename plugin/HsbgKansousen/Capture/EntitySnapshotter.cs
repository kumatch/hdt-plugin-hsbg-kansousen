using System;
using System.Collections.Generic;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using HsbgKansousen.Logic;
using HsbgKansousen.Model;

namespace HsbgKansousen.Capture
{
    /// <summary>Core.Game.Entities から盤面・手札・ショップの観測値を作る。HDT の型に触るのはここと GameSession だけ。</summary>
    internal static class EntitySnapshotter
    {
        /// <summary>列挙中に HDT が変更すると InvalidOperationException になるので、1 回だけやり直す。</summary>
        public static List<Entity> SafeEntities(GameV2 game)
        {
            try
            {
                return game.Entities.Values.ToList();
            }
            catch (InvalidOperationException)
            {
                return game.Entities.Values.ToList();
            }
        }

        public static List<MinionRecord> Board(IEnumerable<Entity> entities, int controllerId)
        {
            var minions = entities
                .Where(x => x.IsMinion && x.IsInPlay && x.IsControlledBy(controllerId))
                .OrderBy(x => x.ZonePosition)
                .ToList();
            return minions.Select((e, i) => HdtCards.ToMinion(e, i + 1)).ToList();
        }

        public static List<CardSnapshot> Hand(IEnumerable<Entity> entities, int controllerId)
        {
            return entities
                .Where(x => x.IsInHand && x.IsControlledBy(controllerId))
                .OrderBy(x => x.ZonePosition)
                .Select(HdtCards.ToSnapshot)
                .ToList();
        }

        /// <summary>ショップ（Bob 側の PLAY 中のミニオン / 酒場の呪文）のうち、指定した Entity を酒場の並び順で。</summary>
        public static List<CardSnapshot> ShopCards(IEnumerable<Entity> entities, IEnumerable<int> ids)
        {
            var wanted = new HashSet<int>(ids);
            return entities
                .Where(x => wanted.Contains(x.Id))
                .OrderBy(x => x.ZonePosition)
                .Select(HdtCards.ToSnapshot)
                .ToList();
        }

        /// <summary>戦闘相手のヒーロー（PLAY 中で相手側制御）。Bob やゴーストには PLAYER_ID が無い。</summary>
        public static Entity? OpponentHero(IEnumerable<Entity> entities, int opponentControllerId)
            => entities.FirstOrDefault(x => x.IsHero && x.IsInPlay && x.IsControlledBy(opponentControllerId));

        /// <summary>「悪夢の発見」ボタン（HearthDb: CardIds.NonCollectible.Neutral.DarkDiscovery）</summary>
        public const string DarkGiftButtonCardId = "BG36_Button_DarkGift";
        /// <summary>ボタンが作る発見スペル "Battlegrounds Dark Gift [DNT]"（HearthDb: BattlegroundsDarkGiftDnt）。候補ミニオンの CREATOR はこれ</summary>
        public const string DarkGiftDiscoverCardId = "BG36_MidGameEffect_010";
        /// <summary>トリプル報酬の発見スペル</summary>
        public const string TripleRewardCardId = "TB_BaconShop_Triples_01";

        public static ShopTick Tick(IEnumerable<Entity> entities, int playerId, int opponentId, Entity? playerEntity, Entity? playerHero, Entity? heroPower,
            IEnumerable<int>? offeredIds = null, IDictionary<int, string?>? sourceCache = null)
        {
            var tick = new ShopTick
            {
                Resources = playerEntity != null && playerEntity.HasTag(GameTag.RESOURCES) ? playerEntity.GetTag(GameTag.RESOURCES) : (int?)null,
                // HDT の HasTag は「値 > 0」なので、0 になり得るタグは GetTag で読む（RESOURCES_USED が 0 の tick を null にすると、
                // 1 → 0 の売却を取り逃す。2026-10-12 実機）
                ResourcesUsed = playerEntity?.GetTag(GameTag.RESOURCES_USED),
                TempResources = playerEntity?.GetTag(GameTag.TEMP_RESOURCES),
                ResourcesSpent = playerEntity?.GetTag(GameTag.NUM_RESOURCES_SPENT_THIS_GAME),
                TechLevel = playerHero?.GetTag(GameTag.PLAYER_TECH_LEVEL) ?? 0,
                Triples = playerHero?.GetTag(GameTag.PLAYER_TRIPLES) ?? 0,
            };
            if (offeredIds != null)
                tick.OfferedIds.UnionWith(offeredIds);

            var hand = new List<Entity>();
            foreach (var e in entities)
            {
                if (e.IsHeroPower && e.IsControlledBy(playerId))
                {
                    // ゾーンを問わず EXHAUSTED を集める（使うと SETASIDE / REMOVEDFROMGAME に移るヒーローパワーがある）
                    if (e.IsInPlay) tick.HeroPowerIds.Add(e.Id);
                    if (e.GetTag(GameTag.EXHAUSTED) > 0) tick.ExhaustedHeroPowerIds.Add(e.Id);
                    continue;
                }
                if (e.IsInPlay && e.IsControlledBy(opponentId) && (e.IsMinion || e.IsBattlegroundsSpell))
                    tick.ShopIds.Add(e.Id);
                else if (e.IsInPlay && e.IsMinion && e.IsControlledBy(playerId))
                    tick.BoardIds.Add(e.Id);
                else if (e.IsInHand && e.IsControlledBy(playerId))
                {
                    tick.HandIds.Add(e.Id);
                    hand.Add(e);
                    if (e.IsBattlegroundsSpell || e.IsSpell)
                        tick.HandSpellIds.Add(e.Id);
                }
            }
            // 作成元の解決は手札に初めて現れた Entity だけ行う（Entities は終盤 1 万を超えるので毎 tick の辞書化を避ける）。
            // 判定に使うタグ（DISPLAYED_CREATOR / CREATOR）は ZONE=HAND と同じログバッチで確定するので、ID ごとの結果はゲーム中ずっと有効
            Dictionary<int, Entity>? byId = null;
            foreach (var e in hand)
            {
                string? source;
                if (sourceCache == null || !sourceCache.TryGetValue(e.Id, out source))
                {
                    if (byId == null)
                    {
                        byId = new Dictionary<int, Entity>();
                        foreach (var x in entities) byId[x.Id] = x;
                    }
                    source = ResolveSource(e, byId, playerId, heroPower?.Id ?? 0);
                    if (sourceCache != null) sourceCache[e.Id] = source;
                }
                if (source != null)
                    tick.HandSources[e.Id] = source;
            }
            return tick;
        }

        /// <summary>
        /// 手札のカードの **直接の** 作成元（DISPLAYED_CREATOR、次に CREATOR）が 悪夢の発見 / 自分のヒーローパワー / トリプル報酬なら
        /// <see cref="CardSource"/> の値を返す。ショップから買ったカードや作成元がそれ以外なら null。
        /// 悪夢の発見で選んだミニオンは、選択時に DISPLAYED_CREATOR = ボタンが付き、生成時の CREATOR = 発見スペル "[DNT]"。
        /// 作成元を何段も辿ってはいけない: 悪夢の発見で得たミニオンが生成したスペルや、悪夢の恵み（ミニオンに付く効果 Dark Gift）が作るコピーまで
        /// dark_gift になってしまう（2026-09-26 の実機で Gearfin → Leaf Through the Pages、Bramble Tunneler → Gem Day を誤検出）
        /// </summary>
        public static string? ResolveSource(Entity card, IReadOnlyDictionary<int, Entity> byId, int playerId, int heroPowerId)
        {
            foreach (var tag in new[] { GameTag.DISPLAYED_CREATOR, GameTag.CREATOR })
            {
                var creatorId = card.GetTag(tag);
                if (creatorId == 0 || !byId.TryGetValue(creatorId, out var creator)) continue;
                if (heroPowerId > 0 && creator.Id == heroPowerId) return CardSource.HeroPower;
                var cid = creator.CardId ?? "";
                if (cid == DarkGiftButtonCardId || cid == DarkGiftDiscoverCardId) return CardSource.DarkGift;
                if (cid == TripleRewardCardId) return CardSource.Triple;
                if (creator.IsHeroPower && creator.IsControlledBy(playerId)) return CardSource.HeroPower;
            }
            return null;
        }
    }
}
