using System.Collections.Generic;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using HsbgKansousen.Model;

namespace HsbgKansousen.Capture
{
    /// <summary>HDT / HearthDb のカード情報から Model の DTO を作るヘルパー（HDT 依存はここに閉じ込める）。</summary>
    internal static class HdtCards
    {
        public static CardRef FromCardId(string? cardId)
        {
            var r = new CardRef { CardId = cardId };
            if (!string.IsNullOrEmpty(cardId) && HearthDb.Cards.All.TryGetValue(cardId!, out var card))
            {
                r.Name = card.Name;
                r.DbfId = card.DbfId;
            }
            return r;
        }

        public static CardRef FromDbfId(int? dbfId)
        {
            var r = new CardRef { DbfId = dbfId };
            if (dbfId.HasValue && dbfId.Value > 0 && HearthDb.Cards.AllByDbfId.TryGetValue(dbfId.Value, out var card))
            {
                r.CardId = card.Id;
                r.Name = card.Name;
            }
            else if (dbfId.HasValue && dbfId.Value <= 0)
            {
                r.DbfId = null;
            }
            return r;
        }

        public static void Fill(CardRef target, CardRef source)
        {
            target.CardId = source.CardId;
            target.Name = source.Name;
            target.DbfId = source.DbfId;
        }

        public static string RaceName(Race race) => race.ToString().ToLowerInvariant();

        /// <summary>
        /// Entity の今のカード ID。HDT は CHANGE_ENTITY（ゴールデン・タッチでゴールデンになる、鍵付きの箱が開く、変身など）で
        /// <c>CardId</c> を基本的に書き換えず <c>Info.LatestCardId</c> にだけ入れる（PowerHandler。HDT 1.58.10 / 1.59.3 で同じ）。
        /// Bob's Buddy もミニオンはこちらで読む。schema 1.6.1 / プラグイン 0.9.4 から
        /// </summary>
        public static string? LatestCardId(Entity e) => e.Info.LatestCardId ?? e.CardId;

        /// <summary>手札・ショップのカード 1 枚（schema 1.5.0）。ミニオンは現在の攻撃力・体力と金色も持つ。</summary>
        public static CardSnapshot ToSnapshot(Entity e)
        {
            var cardId = LatestCardId(e);
            var s = new CardSnapshot { CardId = cardId };
            if (!string.IsNullOrEmpty(cardId) && HearthDb.Cards.All.TryGetValue(cardId!, out var card))
            {
                s.Name = card.Name;
                s.DbfId = card.DbfId;
            }
            var tier = e.HasTag(GameTag.TECH_LEVEL) ? e.GetTag(GameTag.TECH_LEVEL) : e.LatestCard.TechLevel;
            s.Tier = tier > 0 ? tier : (int?)null;
            if (e.IsMinion)
            {
                s.CardType = "minion";
                s.Attack = e.Attack;
                s.Health = e.Health;
                s.Golden = e.GetTag(GameTag.PREMIUM) > 0;
            }
            else
            {
                s.CardType = e.IsBattlegroundsSpell || e.IsSpell ? "spell" : "other";
            }
            return s;
        }

        public static MinionRecord ToMinion(Entity e, int position)
        {
            var m = new MinionRecord
            {
                Position = position,
                CardId = LatestCardId(e),
                Name = e.LatestCard.Name,
                Tier = e.HasTag(GameTag.TECH_LEVEL) ? e.GetTag(GameTag.TECH_LEVEL) : e.LatestCard.TechLevel,
                Attack = e.Attack,
                Health = e.Health,
                Golden = e.GetTag(GameTag.PREMIUM) > 0,
            };
            if (e.GetTag(GameTag.TAUNT) > 0) m.Keywords.Add("taunt");
            // DIVINE_SHIELD は枚数（闇のパラドックスは重ねられる。HDT 1.58.4 も Bob's Buddy に枚数で渡す）
            m.DivineShields = System.Math.Max(0, e.GetTag(GameTag.DIVINE_SHIELD));
            if (m.DivineShields > 0) m.Keywords.Add("divine_shield");
            if (e.GetTag(GameTag.POISONOUS) > 0) m.Keywords.Add("poisonous");
            if (e.GetTag(GameTag.VENOMOUS) > 0) m.Keywords.Add("venomous");
            if (e.GetTag(GameTag.REBORN) > 0) m.Keywords.Add("reborn");
            if (e.GetTag(GameTag.WINDFURY) > 0) m.Keywords.Add("windfury");
            if (e.GetTag(GameTag.STEALTH) > 0) m.Keywords.Add("stealth");
            // schema 1.5.0: 断末魔と勝鬨（HDT 1.58.7 から BG のミニオンに勝鬨アイコンを出す。タイタスや天空孵りの脱走者が倍にする効果の対象）
            if (e.GetTag(GameTag.DEATHRATTLE) > 0) m.Keywords.Add("deathrattle");
            if (e.GetTag(GameTag.BACON_RALLY) > 0) m.Keywords.Add("rally");

            var races = new List<Race>();
            var latest = e.LatestCard;
            if (latest.RaceEnum.HasValue && latest.RaceEnum.Value != Race.INVALID) races.Add(latest.RaceEnum.Value);
            if (latest.SecondaryRaceEnum.HasValue && latest.SecondaryRaceEnum.Value != Race.INVALID) races.Add(latest.SecondaryRaceEnum.Value);
            if (races.Count == 0 && e.HasTag(GameTag.CARDRACE)) races.Add((Race)e.GetTag(GameTag.CARDRACE));
            m.MinionTypes = races.Distinct().Select(RaceName).ToList();
            return m;
        }
    }
}
