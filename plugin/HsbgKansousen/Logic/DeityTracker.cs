using System.Collections.Generic;
using System.Linq;
using HsbgKansousen.Model;

namespace HsbgKansousen.Logic
{
    /// <summary>Sigil（Secret Deity [DNT]）の 1 回分の読み取り値。</summary>
    public readonly struct DeityReading
    {
        public DeityReading(int dbfId, int attack, int health)
        {
            DbfId = dbfId;
            Attack = attack;
            Health = health;
        }

        /// <summary>BACON_EVOLUTION_CARD_ID（目覚める神祇のカード）</summary>
        public int DbfId { get; }
        /// <summary>BACON_EVOLUTION_CARD_OVERWRITE_ATK（現在の合計値）</summary>
        public int Attack { get; }
        public int Health { get; }

        /// <summary>戦闘後の後片付けで 0 に戻された Sigil は値として扱わない</summary>
        public bool IsEmpty => Attack <= 0 && Health <= 0;
    }

    /// <summary>戦闘中に生まれたミニオンの観測値（覚醒判定用）。</summary>
    public readonly struct SpawnedMinion
    {
        public SpawnedMinion(int entityId, int creatorId, int controllerId, bool golden = false)
        {
            EntityId = entityId;
            CreatorId = creatorId;
            ControllerId = controllerId;
            Golden = golden;
        }

        public int EntityId { get; }
        public int CreatorId { get; }
        public int ControllerId { get; }
        /// <summary>金色のカード（CardId が "_G" で終わる / PREMIUM）</summary>
        public bool Golden { get; }
    }

    /// <summary>
    /// 1 戦闘ぶんの神祇の観測を集める（HDT 非依存）。戦闘中は約 100ms ごとに自分と相手の Sigil を読んで渡す。
    /// 相手の Sigil は戦闘ごとに複製され、戦闘の後片付けで値が 0 に戻されるので、最初と最後の非 0 値を覚えておく。
    /// 覚醒は「戦闘中に作られた、CREATOR が Sigil のミニオン」で判定する（覚醒しない戦闘では作られない。docs/log-schema.md）。
    /// </summary>
    public sealed class DeityTracker
    {
        public DeityReading? PlayerLast { get; private set; }
        public DeityReading? OpponentFirst { get; private set; }
        public DeityReading? OpponentLast { get; private set; }

        public void Reset()
        {
            PlayerLast = null;
            OpponentFirst = null;
            OpponentLast = null;
        }

        public void ObservePlayer(DeityReading? reading)
        {
            if (reading.HasValue && !reading.Value.IsEmpty)
                PlayerLast = reading;
        }

        public void ObserveOpponent(DeityReading? reading)
        {
            if (!reading.HasValue || reading.Value.IsEmpty) return;
            OpponentFirst ??= reading;
            OpponentLast = reading;
        }

        /// <summary>覚醒した側のコントローラー ID。<paramref name="sigilIds"/> は全 Sigil の Entity ID。</summary>
        public static HashSet<int> AwakenedControllers(IEnumerable<SpawnedMinion> spawned, ICollection<int> sigilIds)
            => new HashSet<int>(spawned.Where(m => sigilIds.Contains(m.CreatorId)).Select(m => m.ControllerId));

        /// <summary>金色で覚醒した側のコントローラー ID（覚醒した神祇のミニオンのどれかが金色）。</summary>
        public static HashSet<int> GoldenControllers(IEnumerable<SpawnedMinion> spawned, ICollection<int> sigilIds)
            => new HashSet<int>(spawned.Where(m => m.Golden && sigilIds.Contains(m.CreatorId)).Select(m => m.ControllerId));

        /// <summary>ショップ終了時の自分の神祇。Sigil が無ければ null。</summary>
        public static DeityRecord? AtShopEnd(DeityReading? reading, CardRef card)
        {
            if (!reading.HasValue) return null;
            var r = NewRecord(card);
            r.Attack = reading.Value.Attack;
            r.Health = reading.Value.Health;
            r.Golden = IsGoldenCard(card);
            return r;
        }

        /// <summary>
        /// 古なる者の仮面を取ると Sigil の BACON_EVOLUTION_CARD_ID が金色版（BGFYM_000 → BGFYM_000_G）に切り替わる
        /// （2026-09-26 18:28 のゲームで確認）ので、覚醒前でもカードで分かる
        /// </summary>
        public static bool IsGoldenCard(CardRef card) => card.CardId != null && card.CardId.EndsWith("_G");

        /// <summary>戦闘の観測を自分の神祇に反映する。<paramref name="fallback"/> は戦闘中に 1 度も読めなかったときの値（次ショップ開始時の直読み）。</summary>
        public void CompletePlayer(DeityRecord record, DeityReading? fallback, bool? awakened, bool awakenedGolden = false)
        {
            var after = PlayerLast ?? (fallback.HasValue && !fallback.Value.IsEmpty ? fallback : null);
            if (after.HasValue)
            {
                record.AttackAfterCombat = after.Value.Attack;
                record.HealthAfterCombat = after.Value.Health;
            }
            record.Awakened = awakened;
            record.Golden = IsGoldenCard(record) || awakenedGolden;
        }

        /// <summary>相手の神祇。戦闘中に Sigil を読めなかった（ゴースト戦など）なら null。</summary>
        public DeityRecord? OpponentRecord(CardRef card, bool? awakened, bool awakenedGolden = false)
        {
            if (!OpponentFirst.HasValue || !OpponentLast.HasValue) return null;
            var r = NewRecord(card);
            r.Attack = OpponentFirst.Value.Attack;
            r.Health = OpponentFirst.Value.Health;
            r.AttackAfterCombat = OpponentLast.Value.Attack;
            r.HealthAfterCombat = OpponentLast.Value.Health;
            r.Awakened = awakened;
            r.Golden = IsGoldenCard(card) || awakenedGolden;
            return r;
        }

        private static DeityRecord NewRecord(CardRef card)
            => new DeityRecord { CardId = card.CardId, Name = card.Name, DbfId = card.DbfId };
    }
}
