using System;
using System.IO;
using HsbgKansousen.Output;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class GameLogWriterTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hsbg-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Fact]
        public void GetPath_UsesLocalStartTimeAndGameId()
        {
            var w = new GameLogWriter(_root);
            var path = w.GetPath(SampleLogs.StartedAt, "3b0f9a4e-2c7d-4e1a-9f6b-8d2e5c1a7b30");
            var expected = Path.Combine(_root, "2026", "01", "20260110-100000_3b0f9a4e-2c7d-4e1a-9f6b-8d2e5c1a7b30.json");
            Assert.Equal(expected, path);
        }

        [Fact]
        public void GetPath_SanitizesInvalidFileNameChars()
        {
            var w = new GameLogWriter(_root);
            var path = w.GetPath(SampleLogs.StartedAt, "a:b/c");
            Assert.EndsWith("_a_b_c.json", path);
        }

        [Fact]
        public void Write_CreatesFileAndLeavesNoTempFile()
        {
            var w = new GameLogWriter(_root);
            var path = w.Write(SampleLogs.Complete());

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));

            var json = JObject.Parse(File.ReadAllText(path));
            Assert.Equal("complete", (string?)json["status"]);
            Assert.Equal(6, (int?)json["result"]?["placement"]);
        }

        [Fact]
        public void Write_OverwritesExistingFile()
        {
            var w = new GameLogWriter(_root);
            var log = SampleLogs.Phase2Minimal();
            var first = w.Write(log);

            log.Result.MmrAfter = 4957;
            log.Result.MmrDelta = -43;
            var second = w.Write(log);

            Assert.Equal(first, second);
            var json = JObject.Parse(File.ReadAllText(second));
            Assert.Equal(4957, (int?)json["result"]?["mmr_after"]);
            Assert.False(File.Exists(second + ".tmp"));
        }

        [Fact]
        public void WriteAtomic_ManyWrites_ProduceValidJsonEveryTime()
        {
            var w = new GameLogWriter(_root);
            var log = SampleLogs.Complete();
            for (var i = 0; i < 100; i++)
            {
                log.Result.MmrAfter = 6900 + i;
                var path = w.Write(log);
                var json = JObject.Parse(File.ReadAllText(path));
                Assert.Equal(6900 + i, (int?)json["result"]?["mmr_after"]);
            }
        }

        [Fact]
        public void Write_UsesUtf8WithoutBom()
        {
            var w = new GameLogWriter(_root);
            var path = w.Write(SampleLogs.Complete());
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 3);
            Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM must not be written");
        }
    }
}
