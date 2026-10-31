using HsbgKansousen.Model;

namespace HsbgKansousen.Logic
{
    /// <summary>
    /// ターン 1 が始まる前に終わったゲーム（ヒーロー選択中にゲームから外された等）の判定。
    /// ターン・ヒーロー・順位のどれも無いログは分析に使えないので書き出さない。analysis/hsbg/loader.py の判定と揃える。
    /// </summary>
    public static class AbortedGame
    {
        public static bool IsBeforeFirstTurn(GameLog log) =>
            log.Turns.Count == 0
            && (log.Result.TotalTurns ?? 0) <= 0
            && !log.Result.Placement.HasValue
            && log.Player.Hero.IsEmpty;
    }
}
