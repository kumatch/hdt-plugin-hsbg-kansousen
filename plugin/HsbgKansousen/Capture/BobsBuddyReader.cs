using System;
using System.Reflection;
using Hearthstone_Deck_Tracker.API;
using HsbgKansousen.Diagnostics;
using HsbgKansousen.Model;

namespace HsbgKansousen.Capture
{
    /// <summary>
    /// Bob's Buddy の予測値をリフレクションで読む。BobsBuddyInvoker は internal クラスなので public API は無い（docs/hdt-research.md §3.8）。
    /// HDT の更新で壊れたら null を返し、以後は試みない。
    /// </summary>
    internal static class BobsBuddyReader
    {
        private static bool _initialized;
        private static bool _unavailable;
        private static MethodInfo? _getInstance;
        private static PropertyInfo? _output;
        private static PropertyInfo? _state;

        public static BobsBuddyPrediction? TryRead(Guid gameId, int turn)
        {
            try
            {
                if (!EnsureInitialized()) return null;
                var inst = _getInstance!.Invoke(null, new object[] { gameId, turn, false });
                if (inst == null) return null;
                var output = _output!.GetValue(inst);
                if (output == null) return null;
                var t = output.GetType();
                float F(string n) => Convert.ToSingle(t.GetField(n)?.GetValue(output) ?? 0f);
                int I(string n) => Convert.ToInt32(t.GetField(n)?.GetValue(output) ?? 0);
                var sims = I("simulationCount");
                if (sims <= 0) return null;
                return new BobsBuddyPrediction
                {
                    Win = Math.Round(F("winRate"), 4),
                    Tie = Math.Round(F("tieRate"), 4),
                    Loss = Math.Round(F("lossRate"), 4),
                    Lethal = Math.Round(F("theirDeathRate"), 4),
                    OpponentLethal = Math.Round(F("myDeathRate"), 4),
                    AvgDamage = Math.Round(F("avDamage"), 2),
                    Simulations = sims,
                };
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("BobsBuddyReader failed: " + ex.Message);
                _unavailable = true;
                return null;
            }
        }

        public static string? TryReadState(Guid gameId, int turn)
        {
            try
            {
                if (!EnsureInitialized()) return null;
                var inst = _getInstance!.Invoke(null, new object[] { gameId, turn, false });
                return inst == null ? null : _state?.GetValue(inst)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool EnsureInitialized()
        {
            if (_unavailable) return false;
            if (_initialized) return true;
            _initialized = true;
            var type = typeof(Core).Assembly.GetType("Hearthstone_Deck_Tracker.BobsBuddy.BobsBuddyInvoker");
            _getInstance = type?.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Guid), typeof(int), typeof(bool) }, null);
            _output = type?.GetProperty("Output");
            _state = type?.GetProperty("State");
            if (type == null || _getInstance == null || _output == null)
            {
                PluginLogger.Warn("BobsBuddyInvoker not found via reflection; bobs_buddy will be null");
                _unavailable = true;
                return false;
            }
            return true;
        }
    }
}
