using System.Collections.Generic;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using HsbgKansousen.Logic;

namespace HsbgKansousen.Capture
{
    /// <summary>
    /// 甲虫のステータスを HDT の Entity から読む（docs/log-schema.md「甲虫」）。読み方は HDT 1.59.7 の
    /// BeetlesSizeCounter / BobsBuddyInvoker（BeetlesAtkBuff）と同じ: プレイヤー Entity に付く
    /// 「甲虫部隊 プレイヤー付与効果[DNT]」の TAG_SCRIPT_DATA_NUM_1 / _2 が強化の量で、甲虫のステータスは基本の 2/2 との和。
    /// </summary>
    internal static class BeetleReader
    {
        /// <summary>HearthDb: CardIds.NonCollectible.Neutral.RunedProgenitor_BeetleArmyPlayerEnchantDnt</summary>
        public const string EnchantCardId = "BG31_808pe";
        /// <summary>HearthDb: CardIds.NonCollectible.Neutral.BoonofBeetles_BeetleToken1（2/2 の甲虫）</summary>
        private const string BeetleCardId = "BG28_603t";

        private static readonly (int Attack, int Health) Base = ReadBase();

        private static (int, int) ReadBase()
        {
            // HDT の BeetlesSizeCounter と同じく甲虫のカードから読む（取れなければ 2/2。保存済みの power.log で確認した値）
            try
            {
                if (HearthDb.Cards.All.TryGetValue(BeetleCardId, out var card) && card.Attack > 0 && card.Health > 0)
                    return (card.Attack, card.Health);
            }
            catch (System.Exception ex)
            {
                Diagnostics.PluginLogger.Warn("BeetleReader: " + ex.Message);
            }
            return (2, 2);
        }

        /// <summary>
        /// 自分のエンチャント。最初に甲虫が強化されたときに作られ、以後ゲーム中ずっとプレイヤー Entity に付いている。
        /// 相手のエンチャントの戦闘用コピーは相手の制御なので含まれない。
        /// </summary>
        public static Entity? PlayerEnchant(IEnumerable<Entity> entities, int playerId, int? playerEntityId)
            => entities
                .Where(x => x.CardId == EnchantCardId && x.IsInPlay && x.IsControlledBy(playerId)
                            && (!playerEntityId.HasValue || x.GetTag(GameTag.ATTACHED) == playerEntityId.Value))
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

        /// <summary>
        /// 戦闘相手のエンチャント（戦闘ごとに相手のプレイヤー Entity に複製される）。<paramref name="minId"/> 以下の ID は前の戦闘の残骸。
        /// 前の戦闘のコピーは後片付けで REMOVEDFROMGAME に移る（値は残る）ので除外する。
        /// </summary>
        public static Entity? OpponentEnchant(IEnumerable<Entity> entities, int opponentId, int minId)
            => entities
                .Where(x => x.Id > minId && x.CardId == EnchantCardId && x.IsControlledBy(opponentId)
                            && !x.IsInZone(Zone.REMOVEDFROMGAME))
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

        public static BuffStatsReading? Read(Entity? enchant)
        {
            if (enchant == null) return null;
            return new BuffStatsReading(
                enchant.GetTag(GameTag.TAG_SCRIPT_DATA_NUM_1),
                enchant.GetTag(GameTag.TAG_SCRIPT_DATA_NUM_2),
                Base.Attack, Base.Health);
        }
    }
}
