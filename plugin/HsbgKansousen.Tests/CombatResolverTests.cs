using System;
using System.Collections.Generic;
using HsbgKansousen.Logic;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class CombatResolverTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 10, 10, 0, 0);

        private static HeroDamageEvent Ev(int entity, bool mine, int value, int ms)
            => new HeroDamageEvent { EntityId = entity, IsPlayerHero = mine, Value = value, At = T0.AddMilliseconds(ms) };

        [Fact]
        public void DuplicateNotification_IsCollapsed()
        {
            // HDT は同じダメージを 2 回通知する（Phase 1 の実測: 同一タイムスタンプ〜1ms 差）
            var events = new List<HeroDamageEvent> { Ev(97, true, 15, 0), Ev(97, true, 15, 1) };
            var r = CombatResolver.Resolve(events);
            Assert.Equal("loss", r.Result);
            Assert.Equal(15, r.DamageTaken);
            Assert.Equal(0, r.DamageDealt);
        }

        [Fact]
        public void SameValueFarApart_IsTwoHits()
        {
            var events = new List<HeroDamageEvent> { Ev(636, false, 5, 0), Ev(636, false, 5, 3000) };
            var r = CombatResolver.Resolve(events);
            Assert.Equal("win", r.Result);
            Assert.Equal(10, r.DamageDealt);
        }

        [Fact]
        public void NoDamage_IsTie()
        {
            var r = CombatResolver.Resolve(new List<HeroDamageEvent>());
            Assert.Equal("tie", r.Result);
            Assert.Equal(0, r.DamageDealt);
            Assert.Equal(0, r.DamageTaken);
        }

        [Fact]
        public void OpponentHeroDamaged_IsWin()
        {
            var r = CombatResolver.Resolve(new[] { Ev(636, false, 2, 0), Ev(636, false, 2, 0) });
            Assert.Equal("win", r.Result);
            Assert.Equal(2, r.DamageDealt);
        }

        [Fact]
        public void Dedupe_KeepsDifferentEntities()
        {
            var kept = CombatResolver.Dedupe(new[] { Ev(97, true, 3, 0), Ev(636, false, 3, 0) });
            Assert.Equal(2, kept.Count);
        }
    }
}
