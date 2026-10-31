using System;
using System.Collections.Generic;
using System.Linq;

namespace HsbgKansousen.Logic
{
    /// <summary>ヒーローへの戦闘ダメージ（HDT の OnEntityWillTakeDamage 由来）。</summary>
    public sealed class HeroDamageEvent
    {
        public int EntityId { get; set; }
        public bool IsPlayerHero { get; set; }
        public int Value { get; set; }
        public DateTime At { get; set; }
    }

    public sealed class CombatOutcome
    {
        /// <summary>"win" | "loss" | "tie"</summary>
        public string Result { get; set; } = "tie";
        public int DamageDealt { get; set; }
        public int DamageTaken { get; set; }
    }

    /// <summary>
    /// 被弾イベント列から戦闘結果を出す。HDT は同じダメージを 2 回通知するので (entity, value) が短時間に連続したら 1 回にまとめる。
    /// </summary>
    public static class CombatResolver
    {
        public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(500);

        public static List<HeroDamageEvent> Dedupe(IEnumerable<HeroDamageEvent> events)
        {
            var kept = new List<HeroDamageEvent>();
            foreach (var e in events.OrderBy(e => e.At))
            {
                var last = kept.LastOrDefault(k => k.EntityId == e.EntityId);
                if (last != null && last.Value == e.Value && e.At - last.At <= DuplicateWindow)
                    continue;
                kept.Add(e);
            }
            return kept;
        }

        public static CombatOutcome Resolve(IEnumerable<HeroDamageEvent> events)
        {
            var list = Dedupe(events);
            var dealt = list.Where(e => !e.IsPlayerHero).Sum(e => e.Value);
            var taken = list.Where(e => e.IsPlayerHero).Sum(e => e.Value);
            string result;
            if (dealt > 0 && taken == 0) result = "win";
            else if (taken > 0 && dealt == 0) result = "loss";
            else if (dealt == 0 && taken == 0) result = "tie";
            else result = dealt > taken ? "win" : "loss"; // 両方に通知が来る異常ケース
            return new CombatOutcome { Result = result, DamageDealt = dealt, DamageTaken = taken };
        }
    }
}
