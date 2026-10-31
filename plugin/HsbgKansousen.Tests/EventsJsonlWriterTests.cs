using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HsbgKansousen.Output;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class EventsJsonlWriterTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hsbg-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private static KeyValuePair<string, object?> F(string k, object? v) => new KeyValuePair<string, object?>(k, v);

        [Fact]
        public void Line_HasFixedKeyOrderAndNulls()
        {
            var at = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.FromHours(9));
            var line = EventsJsonlWriter.Line(at, 3, "buy", new[] { F("card_id", "BG35_814"), F("gold_after", null) });
            Assert.Equal("{\"t\":\"2026-01-10T10:00:00.0000000+09:00\",\"turn\":3,\"type\":\"buy\",\"card_id\":\"BG35_814\",\"gold_after\":null}", line);
        }

        [Fact]
        public void Append_WritesLinesInOrder()
        {
            var path = Path.Combine(_root, "g.events.jsonl");
            using (var w = new EventsJsonlWriter(path))
            {
                for (var i = 0; i < 50; i++)
                    w.Append(DateTimeOffset.Now, i, "turn_start", F("i", i));
                Assert.True(w.Flush(TimeSpan.FromSeconds(5)));
                Assert.Null(w.LastError);
            }
            var lines = File.ReadAllLines(path);
            Assert.Equal(50, lines.Length);
            for (var i = 0; i < 50; i++)
            {
                var o = JObject.Parse(lines[i]);
                Assert.Equal(i, (int?)o["turn"]);
                Assert.Equal(i, (int?)o["i"]);
                Assert.Equal("turn_start", (string?)o["type"]);
            }
        }

        [Fact]
        public async Task ConcurrentAppends_LoseNothing()
        {
            var path = Path.Combine(_root, "c.events.jsonl");
            var w = new EventsJsonlWriter(path);
            var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
            {
                for (var i = 0; i < 250; i++)
                    w.Append(DateTimeOffset.Now, t, "x", F("i", i));
            }));
            await Task.WhenAll(tasks);
            Assert.True(w.Flush(TimeSpan.FromSeconds(10)));
            var lines = File.ReadAllLines(path);
            Assert.Equal(2000, lines.Length);
            Assert.All(lines, l => JObject.Parse(l));
            Assert.Equal(2000, w.WrittenLines);
        }

        [Fact]
        public void File_IsUtf8WithoutBom()
        {
            var path = Path.Combine(_root, "b.events.jsonl");
            var w = new EventsJsonlWriter(path);
            w.Append(DateTimeOffset.Now, 1, "hero_selected", F("name", "勉強家ケイ"));
            Assert.True(w.Flush(TimeSpan.FromSeconds(5)));
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Contains("勉強家ケイ", File.ReadAllText(path));
        }

        [Fact]
        public void Discard_DeletesWrittenFileAndDropsQueuedLines()
        {
            var path = Path.Combine(_root, "d.events.jsonl");
            var w = new EventsJsonlWriter(path);
            w.Append(DateTimeOffset.Now, 0, "game_start");
            Assert.True(w.Flush(TimeSpan.FromSeconds(5)));
            Assert.True(File.Exists(path));
            for (var i = 0; i < 100; i++)
                w.Append(DateTimeOffset.Now, 0, "warning", F("i", i));
            Assert.True(w.Discard(TimeSpan.FromSeconds(5)));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Discard_WithoutFile_Succeeds()
        {
            var w = new EventsJsonlWriter(Path.Combine(_root, "none.events.jsonl"));
            Assert.True(w.Discard(TimeSpan.FromSeconds(1)));
        }
    }
}
