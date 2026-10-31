using System;
using System.Linq;
using HsbgKansousen.Model;
using HsbgKansousen.Output;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class SerializationTests
    {
        [Fact]
        public void Keys_AreSnakeCase()
        {
            var json = JObject.Parse(GameLogWriter.Serialize(SampleLogs.Complete()));
            Assert.NotNull(json["schema_version"]);
            Assert.NotNull(json["generator"]?["plugin_version"]);
            Assert.NotNull(json["result"]?["mmr_before"]);
            Assert.NotNull(json["player"]?["hero_choices_before_reroll"]);
            var turn = json["turns"]![0]!;
            Assert.NotNull(turn["board_end_of_shop"]);
            Assert.NotNull(turn["combat"]?["bobs_buddy"]?["avg_damage"]);
            Assert.NotNull(turn["actions"]?["tier_ups"]);
            // PascalCase が残っていないこと
            Assert.DoesNotContain(json.Descendants().OfType<JProperty>(), p => p.Name.Any(char.IsUpper));
        }

        [Fact]
        public void NullValues_AreKeptAsExplicitNull()
        {
            var json = JObject.Parse(GameLogWriter.Serialize(SampleLogs.Phase2Minimal()));
            Assert.Equal(JTokenType.Null, json["result"]!["mmr_after"]!.Type);
            Assert.Equal(JTokenType.Null, json["player"]!["hero"]!["name"]!.Type);
            Assert.Equal(JTokenType.Null, json["game"]!["anomaly"]!["card_id"]!.Type);
            Assert.Equal(JTokenType.Null, json["turns"]![0]!["combat"]!.Type);
            Assert.Equal(JTokenType.Null, json["player"]!["hero_choices_before_reroll"]!.Type);
        }

        [Fact]
        public void EmptyCollections_AreArraysNotNull()
        {
            var json = JObject.Parse(GameLogWriter.Serialize(SampleLogs.Phase2Minimal()));
            Assert.Equal(JTokenType.Array, json["opponents"]!.Type);
            Assert.Equal(JTokenType.Array, json["player"]!["trinkets"]!.Type);
            Assert.Equal(JTokenType.Array, json["turns"]![0]!["board_end_of_shop"]!.Type);
        }

        [Fact]
        public void DateTimeOffset_KeepsLocalOffset()
        {
            // JObject.Parse は ISO 日時を DateTime に変換してしまうので、生の文字列で検証する
            var json = GameLogWriter.Serialize(SampleLogs.Complete());
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"started_at\": \"([^\"]+)\"");
            Assert.True(m.Success, "started_at not found");
            Assert.StartsWith("2026-01-10T10:00:00", m.Groups[1].Value);
            Assert.EndsWith("+09:00", m.Groups[1].Value);
        }

        [Fact]
        public void RoundTrip_PreservesValues()
        {
            var original = SampleLogs.Complete();
            var json = GameLogWriter.Serialize(original);
            var back = GameLogWriter.Deserialize(json);

            Assert.NotNull(back);
            Assert.Equal(original.Game.GameId, back!.Game.GameId);
            Assert.Equal(original.Game.StartedAt, back.Game.StartedAt);
            Assert.Equal(original.Result.Placement, back.Result.Placement);
            Assert.Equal(original.Result.MmrDelta, back.Result.MmrDelta);
            Assert.Equal(original.Turns.Count, back.Turns.Count);
            Assert.Equal("win", back.Turns[0].Combat!.Result);
            Assert.Equal(1.0, back.Turns[0].Combat!.BobsBuddy!.Win);
            Assert.Equal("taunt", back.Turns[0].BoardEndOfShop[0].Keywords.Single());
            Assert.Equal(0, back.Turns[0].BoardEndOfShop[0].DivineShields);
            Assert.Equal(3, back.Turns[0].BoardEndOfShop[1].DivineShields);
            Assert.Contains("\"divine_shields\": 3", json);
            Assert.Null(back.Turns[1].Combat);
            Assert.Equal(GameLogWriter.Serialize(back), json);
        }

        [Fact]
        public void SchemaVersion_DefaultsToCurrent()
        {
            var log = new GameLog();
            Assert.Equal(GameLog.CurrentSchemaVersion, log.SchemaVersion);
            Assert.Equal(LogStatus.Incomplete, log.Status);
        }
    }
}
