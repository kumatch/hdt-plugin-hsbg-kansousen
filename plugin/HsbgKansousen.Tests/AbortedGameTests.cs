using HsbgKansousen.Logic;
using HsbgKansousen.Model;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class AbortedGameTests
    {
        /// <summary>ヒーロー選択中にゲームから外されたときの実ログ（2026-10-12）の形</summary>
        private static GameLog KickedDuringHeroPick() => new GameLog
        {
            Status = LogStatus.Incomplete,
            Game = { GameId = "b9843e0f-8086-4402-8272-a59192a6d354", StartedAt = SampleLogs.StartedAt },
            Result = { MmrBefore = 7401, TotalTurns = 0, EliminatedTurn = 0 },
        };

        [Fact]
        public void NoTurnsNoHeroNoPlacement_IsAborted()
        {
            Assert.True(AbortedGame.IsBeforeFirstTurn(KickedDuringHeroPick()));
        }

        [Fact]
        public void TotalTurnsNotYetSet_IsAborted()
        {
            var log = KickedDuringHeroPick();
            log.Result.TotalTurns = null;
            Assert.True(AbortedGame.IsBeforeFirstTurn(log));
        }

        [Fact]
        public void HeroPicked_IsKept()
        {
            var log = KickedDuringHeroPick();
            log.Player.Hero.CardId = "TB_BaconShop_HERO_34";
            Assert.False(AbortedGame.IsBeforeFirstTurn(log));
        }

        [Fact]
        public void PlacementKnown_IsKept()
        {
            var log = KickedDuringHeroPick();
            log.Result.Placement = 8;
            Assert.False(AbortedGame.IsBeforeFirstTurn(log));
        }

        [Fact]
        public void TurnRecorded_IsKept()
        {
            var log = KickedDuringHeroPick();
            log.Turns.Add(new TurnRecord { Turn = 1 });
            Assert.False(AbortedGame.IsBeforeFirstTurn(log));
        }

        [Fact]
        public void CompleteSample_IsKept()
        {
            Assert.False(AbortedGame.IsBeforeFirstTurn(SampleLogs.Complete()));
            Assert.False(AbortedGame.IsBeforeFirstTurn(SampleLogs.Phase2Minimal()));
        }
    }
}
