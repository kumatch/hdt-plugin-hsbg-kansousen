using HsbgKansousen.Model;

namespace HsbgKansousen.Logic
{
    public enum GameStartDecision
    {
        /// <summary>進行中のゲームが無い。新しいゲームとして記録する</summary>
        Fresh,
        /// <summary>進行中のゲームと同じサーバーのゲーム（再接続）。同じ記録を続ける</summary>
        Resume,
        /// <summary>進行中のゲームが終わらないまま別のゲームが始まった。前のゲームを incomplete で書き出してから新しく始める</summary>
        CloseAndFresh,
    }

    /// <summary>
    /// Hearthstone のクラッシュ / 切断からの再接続（P-22）。HDT は再接続したゲームでも OnGameStart を出し直し、
    /// GameStats.GameId も新しくなるので、サーバー側のゲーム番号（MetaData.ServerInfo.GameHandle）で同じゲームかを判定する。
    /// HDT 非依存。
    /// </summary>
    public static class Reconnect
    {
        /// <param name="previousActive">OnGameEnd を経ずに記録中のゲームがあるか</param>
        /// <param name="previousHandle">記録中のゲームの GameHandle（取れなければ 0）</param>
        /// <param name="newHandle">始まったゲームの GameHandle（取れなければ 0）</param>
        /// <param name="isBattlegrounds">始まったゲームが BG か</param>
        public static GameStartDecision Decide(bool previousActive, uint previousHandle, uint newHandle, bool isBattlegrounds)
        {
            if (!previousActive) return GameStartDecision.Fresh;
            // GameHandle が取れないときは同じゲームと言い切れないので、前のゲームを捨てずに閉じる
            if (isBattlegrounds && previousHandle != 0 && previousHandle == newHandle) return GameStartDecision.Resume;
            return GameStartDecision.CloseAndFresh;
        }

        /// <summary>
        /// 再接続中に終わった戦闘の結果を、戦闘開始時と次のショップ開始時の HP + アーマーの差から推定する。
        /// 戦闘のダメージ通知を見ていないので CombatResolver は使えない。決められなければ null。
        /// </summary>
        /// <param name="mineBefore">戦闘開始時の自分の HP + アーマー</param>
        /// <param name="mineAfter">次のショップ開始時の自分の HP + アーマー</param>
        /// <param name="opponentBefore">戦闘開始時の相手の HP + アーマー（取れなければ null）</param>
        /// <param name="opponentAfter">次のショップ開始時の相手の HP + アーマー（リーダーボードのヒーロー。取れなければ null）</param>
        public static CombatOutcome? InferCombat(int? mineBefore, int? mineAfter, int? opponentBefore, int? opponentAfter)
        {
            if (!mineBefore.HasValue || !mineAfter.HasValue) return null;
            var taken = mineBefore.Value - mineAfter.Value;
            int? dealt = opponentBefore.HasValue && opponentAfter.HasValue ? opponentBefore.Value - opponentAfter.Value : (int?)null;
            // 増えている = 戦闘以外の変化が混ざっている（ターン開始時のアーマー等）。推定しない
            if (taken < 0 || dealt < 0) return null;
            if (taken > 0)
                return dealt.GetValueOrDefault() == 0 ? new CombatOutcome { Result = "loss", DamageDealt = 0, DamageTaken = taken } : null;
            if (!dealt.HasValue) return null; // 被ダメージ 0: 勝ちか引き分けかは相手の HP が無いと決まらない
            return new CombatOutcome { Result = dealt.Value > 0 ? "win" : "tie", DamageDealt = dealt.Value, DamageTaken = 0 };
        }

        /// <summary>同じターンの行動回数を足す（ショップ中に落ちたときの、落ちる前と再接続後）。gold_spent は後の値（ショップ終了時の RESOURCES_USED）。</summary>
        public static TurnActions Add(TurnActions before, TurnActions after) => new TurnActions
        {
            GoldSpent = after.GoldSpent ?? before.GoldSpent,
            Buys = Sum(before.Buys, after.Buys),
            Sells = Sum(before.Sells, after.Sells),
            GoldGained = Sum(before.GoldGained, after.GoldGained),
            Rerolls = Sum(before.Rerolls, after.Rerolls),
            TierUps = Sum(before.TierUps, after.TierUps),
            HeroPowerUses = Sum(before.HeroPowerUses, after.HeroPowerUses),
            SpellsPlayed = Sum(before.SpellsPlayed, after.SpellsPlayed),
            DarkGifts = Sum(before.DarkGifts, after.DarkGifts),
        };

        private static int? Sum(int? a, int? b) => a.HasValue || b.HasValue ? a.GetValueOrDefault() + b.GetValueOrDefault() : (int?)null;
    }
}
