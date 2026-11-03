using HsbgKansousen.Model;

namespace HsbgKansousen.Logic
{
    /// <summary>
    /// プレイヤーに付く強化エンチャント（甲虫部隊 BG31_808pe など）の 1 回分の読み取り値。
    /// <see cref="Attack"/> / <see cref="Health"/> は記録する値（甲虫なら基本の 2/2 を足した甲虫のステータス）。
    /// </summary>
    public readonly struct BuffStatsReading
    {
        public BuffStatsReading(int bonusAttack, int bonusHealth, int baseAttack = 0, int baseHealth = 0)
        {
            BonusAttack = bonusAttack;
            BonusHealth = bonusHealth;
            Attack = baseAttack + bonusAttack;
            Health = baseHealth + bonusHealth;
        }

        /// <summary>
        /// 自分の値: エンチャントの TAG_SCRIPT_DATA_NUM_1/2 とプレイヤー Entity のタグの、成分ごとの大きいほう
        /// （血の宝石は 2 か所にあり、どちらも少なく出ることがある。HDT 1.59.7 の Bob's Buddy と同じ）。どちらも無ければ null。
        /// </summary>
        public static BuffStatsReading? FromSources(int? enchantAttack, int? enchantHealth, int? tagAttack, int? tagHealth,
            int baseAttack = 0, int baseHealth = 0)
        {
            int? a = Max(enchantAttack, tagAttack), h = Max(enchantHealth, tagHealth);
            if (!a.HasValue || !h.HasValue) return null;
            return new BuffStatsReading(a.Value, h.Value, baseAttack, baseHealth);
        }

        private static int? Max(int? x, int? y) => x.HasValue && y.HasValue ? System.Math.Max(x.Value, y.Value) : x ?? y;

        /// <summary>エンチャントの TAG_SCRIPT_DATA_NUM_1（強化の量）</summary>
        public int BonusAttack { get; }
        /// <summary>エンチャントの TAG_SCRIPT_DATA_NUM_2</summary>
        public int BonusHealth { get; }
        public int Attack { get; }
        public int Health { get; }

        /// <summary>
        /// 強化が 0/0（作られた直後でまだ値が入っていない）。エンチャントは強化されたときに作られるので、値として扱わない
        /// </summary>
        public bool IsEmpty => BonusAttack <= 0 && BonusHealth <= 0;
    }

    /// <summary>
    /// 1 戦闘ぶんの強化（エンチャントかプレイヤーのタグ）の観測を集める（HDT 非依存。神祇の <see cref="DeityTracker"/> と同じ考え方）。
    /// 戦闘中は約 100ms ごとに自分と相手のエンチャントを読んで渡す。自分のエンチャントはゲーム中ずっとあり、
    /// 相手のエンチャントは戦闘ごとに複製されるので、相手は最初と最後の値を覚えておく（docs/log-schema.md「甲虫」）。
    /// </summary>
    public sealed class BuffStatsTracker
    {
        public BuffStatsReading? PlayerLast { get; private set; }
        public BuffStatsReading? OpponentFirst { get; private set; }
        public BuffStatsReading? OpponentLast { get; private set; }

        public void Reset()
        {
            PlayerLast = null;
            OpponentFirst = null;
            OpponentLast = null;
        }

        public void ObservePlayer(BuffStatsReading? reading)
        {
            if (reading.HasValue && !reading.Value.IsEmpty)
                PlayerLast = reading;
        }

        public void ObserveOpponent(BuffStatsReading? reading)
        {
            if (!reading.HasValue || reading.Value.IsEmpty) return;
            OpponentFirst ??= reading;
            OpponentLast = reading;
        }

        /// <summary>ショップ終了時の自分の値。エンチャントが無ければ null。</summary>
        public static BuffStatsRecord? AtShopEnd(BuffStatsReading? reading)
        {
            if (!reading.HasValue || reading.Value.IsEmpty) return null;
            return new BuffStatsRecord { Attack = reading.Value.Attack, Health = reading.Value.Health };
        }

        /// <summary>
        /// 戦闘の観測を自分の記録に反映する。<paramref name="fallback"/> は戦闘中に 1 度も読めなかったときの値（戦闘の確定時の直読み）。
        /// ショップ終了時にエンチャントが無く、戦闘中に初めて付いた（荒らし屋スコーピッド等）ときは attack/health が null の記録を作る。
        /// </summary>
        public BuffStatsRecord? CompletePlayer(BuffStatsRecord? record, BuffStatsReading? fallback)
        {
            var after = PlayerLast ?? (fallback.HasValue && !fallback.Value.IsEmpty ? fallback : null);
            if (!after.HasValue) return record;
            record ??= new BuffStatsRecord();
            record.AttackAfterCombat = after.Value.Attack;
            record.HealthAfterCombat = after.Value.Health;
            return record;
        }

        /// <summary>相手の値。戦闘中にエンチャントを読めなかった（相手に強化が無い）なら null。</summary>
        public BuffStatsRecord? OpponentRecord()
        {
            if (!OpponentFirst.HasValue || !OpponentLast.HasValue) return null;
            return new BuffStatsRecord
            {
                Attack = OpponentFirst.Value.Attack,
                Health = OpponentFirst.Value.Health,
                AttackAfterCombat = OpponentLast.Value.Attack,
                HealthAfterCombat = OpponentLast.Value.Health,
            };
        }
    }
}
