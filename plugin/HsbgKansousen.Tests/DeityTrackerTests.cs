using System.Collections.Generic;
using HsbgKansousen.Logic;
using HsbgKansousen.Model;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class DeityTrackerTests
    {
        private static readonly CardRef Cthun = new CardRef { CardId = "BGFYM_000", Name = "C'Thun", DbfId = 130610 };

        private static DeityReading R(int attack, int health) => new DeityReading(130610, attack, health);

        [Fact]
        public void AtShopEnd_UsesSigilTotals()
        {
            var r = DeityTracker.AtShopEnd(R(3703, 3983), Cthun)!;
            Assert.Equal("BGFYM_000", r.CardId);
            Assert.Equal(3703, r.Attack);
            Assert.Equal(3983, r.Health);
            Assert.Null(r.AttackAfterCombat);
            Assert.Null(r.Awakened);
        }

        [Fact]
        public void AtShopEnd_NoSigil_IsNull()
        {
            Assert.Null(DeityTracker.AtShopEnd(null, Cthun));
        }

        [Fact]
        public void Player_AfterCombat_IsLastReadingInCombat()
        {
            // 実ゲームの T14: 3703/3983 で戦闘に入り、戦闘中に 6187/6467 まで伸びた
            var t = new DeityTracker();
            t.ObservePlayer(R(3703, 3983));
            t.ObservePlayer(R(4500, 4800));
            t.ObservePlayer(R(6187, 6467));
            var rec = DeityTracker.AtShopEnd(R(3703, 3983), Cthun)!;
            t.CompletePlayer(rec, fallback: R(6197, 6477), awakened: true);
            Assert.Equal(6187, rec.AttackAfterCombat);
            Assert.Equal(6467, rec.HealthAfterCombat);
            Assert.True(rec.Awakened);
        }

        [Fact]
        public void Player_NoReadingInCombat_UsesFallback()
        {
            var t = new DeityTracker();
            var rec = DeityTracker.AtShopEnd(R(3, 3), Cthun)!;
            t.CompletePlayer(rec, fallback: R(3, 3), awakened: false);
            Assert.Equal(3, rec.AttackAfterCombat);
            Assert.False(rec.Awakened);
        }

        [Fact]
        public void Opponent_FirstAndLastNonZero_ZeroedCopyIgnored()
        {
            // 相手の Sigil のコピーは戦闘準備中は 0、戦闘開始で 3578/3496、後片付けで 0 に戻る
            var t = new DeityTracker();
            t.ObserveOpponent(R(0, 0));
            t.ObserveOpponent(R(3578, 3496));
            t.ObserveOpponent(R(4102, 4020));
            t.ObserveOpponent(R(4626, 4544));
            t.ObserveOpponent(R(0, 0));
            var opp = t.OpponentRecord(Cthun, awakened: true)!;
            Assert.Equal(3578, opp.Attack);
            Assert.Equal(3496, opp.Health);
            Assert.Equal(4626, opp.AttackAfterCombat);
            Assert.Equal(4544, opp.HealthAfterCombat);
            Assert.True(opp.Awakened);
        }

        [Fact]
        public void Opponent_NeverRead_IsNull()
        {
            var t = new DeityTracker();
            t.ObserveOpponent(null);
            Assert.Null(t.OpponentRecord(Cthun, awakened: false));
        }

        [Fact]
        public void Reset_ClearsCombatObservations()
        {
            var t = new DeityTracker();
            t.ObservePlayer(R(7, 15));
            t.ObserveOpponent(R(16, 22));
            t.Reset();
            Assert.Null(t.PlayerLast);
            Assert.Null(t.OpponentFirst);
            Assert.Null(t.OpponentLast);
        }

        [Fact]
        public void AwakenedControllers_OnlyMinionsCreatedBySigils()
        {
            // 自分の Sigil 445 から覚醒（本体 21806 と一時コピー 21807）、相手（13）の Sigil コピー 21333 から覚醒 21853。
            // 21900 は Sigil 以外が作ったミニオン
            var spawned = new List<SpawnedMinion>
            {
                new SpawnedMinion(21806, 445, 5),
                new SpawnedMinion(21807, 445, 5),
                new SpawnedMinion(21853, 21333, 13),
                new SpawnedMinion(21900, 20163, 5),
            };
            var awakened = DeityTracker.AwakenedControllers(spawned, new HashSet<int> { 445, 21333 });
            Assert.Equal(new HashSet<int> { 5, 13 }, awakened);

            var onlyOther = DeityTracker.AwakenedControllers(new[] { new SpawnedMinion(21900, 20163, 5) }, new HashSet<int> { 445 });
            Assert.Empty(onlyOther);
        }

        [Fact]
        public void GoldenControllers_OnlyGoldenDeities()
        {
            // 自分は古なる者の仮面で金色クトゥーン、相手は通常のヤシャラージュ。金色の非神祇ミニオンは数えない
            var spawned = new List<SpawnedMinion>
            {
                new SpawnedMinion(9001, 445, 5, golden: true),
                new SpawnedMinion(9002, 445, 5, golden: true),
                new SpawnedMinion(9003, 700, 13, golden: false),
                new SpawnedMinion(9004, 20163, 13, golden: true),
            };
            var sigils = new HashSet<int> { 445, 700 };
            Assert.Equal(new HashSet<int> { 5 }, DeityTracker.GoldenControllers(spawned, sigils));
        }

        private static readonly CardRef GoldenCthun = new CardRef { CardId = "BGFYM_000_G", Name = "C'Thun", DbfId = 134633 };

        [Fact]
        public void Golden_FromSigilCard_EvenWithoutAwakening()
        {
            // 古なる者の仮面を取ると Sigil の神祇カードが BGFYM_000_G になる（覚醒前でも分かる）
            var t = new DeityTracker();
            var asleep = DeityTracker.AtShopEnd(R(60, 57), GoldenCthun)!;
            Assert.True(asleep.Golden);
            t.CompletePlayer(asleep, fallback: R(60, 57), awakened: false);
            Assert.True(asleep.Golden);

            var normal = DeityTracker.AtShopEnd(R(17, 17), Cthun)!;
            Assert.False(normal.Golden);
            t.CompletePlayer(normal, fallback: R(17, 17), awakened: true);
            Assert.False(normal.Golden);

            // 覚醒したミニオンが金色なら、カードが取れていなくても金色
            var byAwakening = DeityTracker.AtShopEnd(R(1, 1), new CardRef())!;
            t.CompletePlayer(byAwakening, fallback: R(1, 1), awakened: true, awakenedGolden: true);
            Assert.True(byAwakening.Golden);

            t.ObserveOpponent(R(10, 10));
            Assert.False(t.OpponentRecord(Cthun, awakened: true)!.Golden);
            Assert.True(t.OpponentRecord(GoldenCthun, awakened: false)!.Golden);
        }
    }
}
