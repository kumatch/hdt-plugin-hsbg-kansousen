using HsbgKansousen.Logic;
using HsbgKansousen.Model;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class BuffStatsTrackerTests
    {
        /// <summary>甲虫: エンチャントの強化の量 + 基本の 2/2</summary>
        private static BuffStatsReading B(int bonusAttack, int bonusHealth) => new BuffStatsReading(bonusAttack, bonusHealth, 2, 2);

        [Fact]
        public void Reading_AddsBase()
        {
            var r = B(40, 38);
            Assert.Equal(42, r.Attack);
            Assert.Equal(40, r.Health);
            Assert.Equal(40, r.BonusAttack);
            Assert.False(r.IsEmpty);
        }

        [Fact]
        public void Reading_ZeroBonus_IsEmpty()
        {
            // 作られた直後でまだ値が入っていないエンチャント
            Assert.True(B(0, 0).IsEmpty);
            Assert.False(B(4, 0).IsEmpty);
        }

        [Fact]
        public void FromSources_BloodGem_TakesLargerPerStat_AndAddsBase()
        {
            // 2026-10-12 14:53 のゲームの T9: プレイヤーのタグ 4/5、エンチャント 2/4（エンチャントが少なく出ていた）
            var r = BuffStatsReading.FromSources(2, 4, 4, 5, 1, 1)!.Value;
            Assert.Equal(5, r.Attack);
            Assert.Equal(6, r.Health);
            var mixed = BuffStatsReading.FromSources(6, 1, 3, 3, 1, 1)!.Value;
            Assert.Equal((7, 4), (mixed.Attack, mixed.Health));
        }

        [Fact]
        public void FromSources_OnlyOneSource()
        {
            // 酒場の呪文はタグだけ、甲虫・酒場のミニオンはエンチャントだけ
            Assert.Equal(3, BuffStatsReading.FromSources(null, null, 3, 2)!.Value.Attack);
            Assert.Equal(6, BuffStatsReading.FromSources(4, 4, null, null, 2, 2)!.Value.Attack);
            Assert.Null(BuffStatsReading.FromSources(null, null, null, null));
            // タグが 0（強化なし）なら空。AtShopEnd で null になる
            Assert.Null(BuffStatsTracker.AtShopEnd(BuffStatsReading.FromSources(null, null, 0, 0)));
        }

        [Fact]
        public void AtShopEnd_NoEnchant_IsNull()
        {
            Assert.Null(BuffStatsTracker.AtShopEnd(null));
            Assert.Null(BuffStatsTracker.AtShopEnd(B(0, 0)));
        }

        [Fact]
        public void Player_AfterCombat_IsLastReadingInCombat()
        {
            // 2026-11-02 15:18 のゲームの T10: 40/40 の強化で戦闘に入り、荒らし屋スコーピッドで 81/81 まで伸びた
            var t = new BuffStatsTracker();
            var rec = BuffStatsTracker.AtShopEnd(B(40, 40))!;
            Assert.Equal(42, rec.Attack);
            t.ObservePlayer(B(40, 40));
            t.ObservePlayer(B(60, 60));
            t.ObservePlayer(B(81, 81));
            var done = t.CompletePlayer(rec, fallback: B(90, 90))!;
            Assert.Same(rec, done);
            Assert.Equal(83, done.AttackAfterCombat);
            Assert.Equal(83, done.HealthAfterCombat);
        }

        [Fact]
        public void Player_NoReadingInCombat_UsesFallback()
        {
            var t = new BuffStatsTracker();
            var rec = t.CompletePlayer(BuffStatsTracker.AtShopEnd(B(4, 4)), fallback: B(4, 4))!;
            Assert.Equal(6, rec.AttackAfterCombat);
        }

        [Fact]
        public void Player_CreatedInCombat_HasOnlyAfterValues()
        {
            // ショップ終了時にエンチャントが無く、戦闘中に初めて甲虫が強化された
            var t = new BuffStatsTracker();
            t.ObservePlayer(B(0, 0));
            t.ObservePlayer(B(4, 4));
            var rec = t.CompletePlayer(null, fallback: null)!;
            Assert.Null(rec.Attack);
            Assert.Null(rec.Health);
            Assert.Equal(6, rec.AttackAfterCombat);
        }

        [Fact]
        public void Player_NeverSeen_StaysNull()
        {
            var t = new BuffStatsTracker();
            Assert.Null(t.CompletePlayer(null, fallback: null));
        }

        [Fact]
        public void Opponent_FirstAndLast()
        {
            // 2026-11-02 23:14 のゲームの T12: 相手の強化は 270/268 で始まり、戦闘中に 383/381 まで伸びた
            var t = new BuffStatsTracker();
            t.ObserveOpponent(B(0, 0));
            t.ObserveOpponent(B(270, 268));
            t.ObserveOpponent(B(383, 381));
            var r = t.OpponentRecord()!;
            Assert.Equal(272, r.Attack);
            Assert.Equal(270, r.Health);
            Assert.Equal(385, r.AttackAfterCombat);
            Assert.Equal(383, r.HealthAfterCombat);
        }

        [Fact]
        public void Opponent_NotSeen_IsNull_AndResetClears()
        {
            var t = new BuffStatsTracker();
            Assert.Null(t.OpponentRecord());
            t.ObserveOpponent(B(4, 2));
            t.ObservePlayer(B(4, 2));
            t.Reset();
            Assert.Null(t.OpponentRecord());
            Assert.Null(t.PlayerLast);
        }
    }
}
