using System.Collections.Generic;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using HsbgKansousen.Logic;

namespace HsbgKansousen.Capture
{
    /// <summary>
    /// 神祇の Sigil（Secret Deity [DNT]）を HDT の Entity から読む。読み方は HDT 1.58.4 の
    /// BattlegroundsDeityState / BobsBuddyUtils.GetDeityFromSigil と同じ（docs/log-schema.md §turns[].deity）。
    /// </summary>
    internal static class DeityReader
    {
        /// <summary>HearthDb: CardIds.NonCollectible.Neutral.SecretDeityDnt</summary>
        public const string SigilCardId = "BG_OldGod";

        /// <summary>
        /// 自分の Sigil。ゲーム中ずっと SECRET にある。相手の Sigil の戦闘用コピーは作られた直後だけ
        /// 自分の制御で SETASIDE にあるので、SECRET に限って除外する。
        /// </summary>
        public static Entity? PlayerSigil(IEnumerable<Entity> entities, int playerId)
            => entities
                .Where(x => x.CardId == SigilCardId && x.IsInSecret && x.IsControlledBy(playerId))
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

        /// <summary>
        /// 戦闘相手の Sigil（戦闘ごとに複製されるので最新のもの）。<paramref name="minId"/> 以下の ID は前の戦闘の残骸。
        /// 前の戦闘のコピーは後片付けで REMOVEDFROMGAME に移ってから値が 0 に、カードがクトゥーンに戻されるので除外する
        /// （HDT 1.58.4 の BattlegroundsDeityState.GetSigil と同じ。値 0 は DeityTracker でも捨てている）。
        /// </summary>
        public static Entity? OpponentSigil(IEnumerable<Entity> entities, int opponentId, int minId)
            => entities
                .Where(x => x.Id > minId && x.CardId == SigilCardId && x.IsControlledBy(opponentId)
                            && !x.IsInZone(Zone.REMOVEDFROMGAME))
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

        public static DeityReading? Read(Entity? sigil)
        {
            if (sigil == null) return null;
            return new DeityReading(
                sigil.GetTag(GameTag.BACON_EVOLUTION_CARD_ID),
                sigil.GetTag(GameTag.BACON_EVOLUTION_CARD_OVERWRITE_ATK),
                sigil.GetTag(GameTag.BACON_EVOLUTION_CARD_OVERWRITE_HEALTH));
        }

        /// <summary>ID が <paramref name="minId"/> より大きい（= 戦闘中に作られた）ミニオンと、全 Sigil の ID。</summary>
        public static (List<SpawnedMinion> Spawned, HashSet<int> SigilIds) CombatSpawns(IEnumerable<Entity> entities, int minId)
        {
            var spawned = new List<SpawnedMinion>();
            var sigils = new HashSet<int>();
            foreach (var e in entities)
            {
                if (e.CardId == SigilCardId)
                    sigils.Add(e.Id);
                else if (e.Id > minId && e.IsMinion)
                    spawned.Add(new SpawnedMinion(e.Id, e.GetTag(GameTag.CREATOR), e.GetTag(GameTag.CONTROLLER),
                        (e.CardId?.EndsWith("_G") ?? false) || e.GetTag(GameTag.PREMIUM) > 0));
            }
            return (spawned, sigils);
        }
    }
}
