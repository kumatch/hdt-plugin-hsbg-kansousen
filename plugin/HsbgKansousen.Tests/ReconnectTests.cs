using HsbgKansousen.Logic;
using HsbgKansousen.Model;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class ReconnectTests
    {
        [Fact]
        public void NoGameInProgress_IsFresh()
        {
            Assert.Equal(GameStartDecision.Fresh, Reconnect.Decide(false, 1936, 1936, true));
        }

        [Fact]
        public void SameGameHandle_IsResume()
        {
            // 2026-10-17 17:45 のゲーム: GameNetLogger.log の game=1936 が再接続（reconnecting=True）でも同じ
            Assert.Equal(GameStartDecision.Resume, Reconnect.Decide(true, 1936, 1936, true));
        }

        [Fact]
        public void DifferentGameHandle_ClosesPrevious()
        {
            Assert.Equal(GameStartDecision.CloseAndFresh, Reconnect.Decide(true, 1936, 1964, true));
        }

        [Fact]
        public void UnknownGameHandle_ClosesPrevious()
        {
            Assert.Equal(GameStartDecision.CloseAndFresh, Reconnect.Decide(true, 0, 0, true));
            Assert.Equal(GameStartDecision.CloseAndFresh, Reconnect.Decide(true, 1936, 0, true));
        }

        [Fact]
        public void SameHandleButNotBattlegrounds_ClosesPrevious()
        {
            Assert.Equal(GameStartDecision.CloseAndFresh, Reconnect.Decide(true, 1936, 1936, false));
        }

        [Fact]
        public void LostHealth_IsLoss()
        {
            // 2026-10-17 17:45 の T11: 戦闘開始時 30 + 5、T12 開始時 20 + 0。相手（フックタスク船長）は 13 のまま
            var r = Reconnect.InferCombat(35, 20, 13, 13);
            Assert.NotNull(r);
            Assert.Equal("loss", r!.Result);
            Assert.Equal(15, r.DamageTaken);
            Assert.Equal(0, r.DamageDealt);
        }

        [Fact]
        public void LostHealthWithoutOpponent_IsLoss()
        {
            var r = Reconnect.InferCombat(35, 20, null, null);
            Assert.Equal("loss", r!.Result);
            Assert.Equal(15, r.DamageTaken);
        }

        [Fact]
        public void OpponentLostHealth_IsWin()
        {
            var r = Reconnect.InferCombat(20, 20, 13, -2);
            Assert.Equal("win", r!.Result);
            Assert.Equal(15, r.DamageDealt);
            Assert.Equal(0, r.DamageTaken);
        }

        [Fact]
        public void NobodyLostHealth_IsTie()
        {
            var r = Reconnect.InferCombat(20, 20, 13, 13);
            Assert.Equal("tie", r!.Result);
            Assert.Equal(0, r.DamageDealt);
            Assert.Equal(0, r.DamageTaken);
        }

        [Fact]
        public void NoDamageWithoutOpponent_IsUnknown()
        {
            Assert.Null(Reconnect.InferCombat(20, 20, null, null));
        }

        [Fact]
        public void BothLostHealth_IsUnknown()
        {
            Assert.Null(Reconnect.InferCombat(20, 15, 13, 10));
        }

        [Fact]
        public void HealthGained_IsUnknown()
        {
            Assert.Null(Reconnect.InferCombat(20, 25, 13, 13));
            Assert.Null(Reconnect.InferCombat(20, 20, 13, 15));
        }

        [Fact]
        public void MissingOwnHealth_IsUnknown()
        {
            Assert.Null(Reconnect.InferCombat(null, 20, 13, 13));
            Assert.Null(Reconnect.InferCombat(20, null, 13, 13));
        }

        [Fact]
        public void Add_SumsCountsAndKeepsLaterGoldSpent()
        {
            var before = new TurnActions { GoldSpent = null, Buys = 2, Sells = 1, GoldGained = 0, Rerolls = 1, TierUps = 0, HeroPowerUses = 0, SpellsPlayed = 1, DarkGifts = 0 };
            var after = new TurnActions { GoldSpent = 10, Buys = 1, Sells = 2, GoldGained = 1, Rerolls = 3, TierUps = 1, HeroPowerUses = 1, SpellsPlayed = 0, DarkGifts = null };
            var sum = Reconnect.Add(before, after);
            Assert.Equal(10, sum.GoldSpent);
            Assert.Equal(3, sum.Buys);
            Assert.Equal(3, sum.Sells);
            Assert.Equal(1, sum.GoldGained);
            Assert.Equal(4, sum.Rerolls);
            Assert.Equal(1, sum.TierUps);
            Assert.Equal(1, sum.HeroPowerUses);
            Assert.Equal(1, sum.SpellsPlayed);
            Assert.Equal(0, sum.DarkGifts);
        }

        [Fact]
        public void Add_KeepsNullWhenBothNull()
        {
            var sum = Reconnect.Add(new TurnActions(), new TurnActions());
            Assert.Null(sum.Buys);
            Assert.Null(sum.GoldSpent);
        }

        [Fact]
        public void Add_FallsBackToEarlierGoldSpent()
        {
            var sum = Reconnect.Add(new TurnActions { GoldSpent = 4 }, new TurnActions());
            Assert.Equal(4, sum.GoldSpent);
        }
    }
}
