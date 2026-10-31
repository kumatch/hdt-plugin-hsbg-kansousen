using System;
using System.IO;
using System.Linq;
using HsbgKansousen.Model;
using HsbgKansousen.Output;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using Xunit;

namespace HsbgKansousen.Tests
{
    /// <summary>schema/game-log.schema.json と DTO の整合を検証する。</summary>
    public sealed class SchemaTests
    {
        private static readonly Lazy<JsonSchema> Schema = new Lazy<JsonSchema>(() =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "game-log.schema.json");
            return JsonSchema.FromJsonAsync(File.ReadAllText(path)).GetAwaiter().GetResult();
        });

        private static string[] Validate(GameLog log)
            => Schema.Value.Validate(GameLogWriter.Serialize(log)).Select(e => $"{e.Path}: {e.Kind}").ToArray();

        [Fact]
        public void CompleteLog_IsValid()
        {
            var errors = Validate(SampleLogs.Complete());
            Assert.True(errors.Length == 0, string.Join("\n", errors));
        }

        [Fact]
        public void Phase2MinimalLog_IsValid()
        {
            var errors = Validate(SampleLogs.Phase2Minimal());
            Assert.True(errors.Length == 0, string.Join("\n", errors));
        }

        [Fact]
        public void EmptyLog_IsValid()
        {
            // 何も取れなかった incomplete ログでもスキーマには適合する（game_id だけは必要）
            var log = new GameLog { Game = { GameId = "x" } };
            var errors = Validate(log);
            Assert.True(errors.Length == 0, string.Join("\n", errors));
        }

        [Fact]
        public void UnknownProperty_IsRejected()
        {
            var json = JObject.Parse(GameLogWriter.Serialize(SampleLogs.Complete()));
            json["result"]!["mmr_after_2"] = 1;
            var errors = Schema.Value.Validate(json.ToString());
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void InvalidPlacement_IsRejected()
        {
            var log = SampleLogs.Complete();
            log.Result.Placement = 9;
            Assert.NotEmpty(Validate(log));
        }

        [Fact]
        public void InvalidStatus_IsRejected()
        {
            var log = SampleLogs.Complete();
            log.Status = "done";
            Assert.NotEmpty(Validate(log));
        }
    }
}
