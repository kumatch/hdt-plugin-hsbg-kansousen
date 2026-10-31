using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HsbgKansousen.Output;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HsbgKansousen.Tests
{
    /// <summary>100 ゲーム相当の書き込みでファイル破損ゼロ。</summary>
    public sealed class StressTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hsbg-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Fact]
        public void HundredGames_AllFilesValid()
        {
            var writer = new GameLogWriter(_root);
            var journal = new PartialLogJournal(_root);
            var mainPaths = new List<string>();
            var eventPaths = new List<string>();

            for (var g = 0; g < 100; g++)
            {
                var log = SampleLogs.Complete();
                log.Game.GameId = Guid.NewGuid().ToString();
                log.Game.StartedAt = SampleLogs.StartedAt.AddMinutes(g * 25);
                var basePath = writer.GetBasePath(log);
                var events = new EventsJsonlWriter(GameLogWriter.EventsPath(basePath));

                for (var t = 1; t <= 12; t++)
                {
                    journal.Save(log);                                   // 各ターン開始時
                    events.Append(DateTimeOffset.Now, t, "turn_start", new KeyValuePair<string, object?>("tier", t / 2 + 1));
                    events.Append(DateTimeOffset.Now, t, "combat_end", new KeyValuePair<string, object?>("result", "win"));
                }
                mainPaths.Add(writer.Write(log));                        // ゲーム終了
                journal.Delete(log.Game.GameId);
                log.Result.MmrAfter = 7000 + g;
                writer.Write(log);                                       // MMR 後値の上書き
                var powerLines = Enumerable.Range(0, 200).Select(i => $"D 12:00:00.{i:D7} line {i} 勉強家ケイ").ToList();
                var gz = PowerLogWriter.PathFor(basePath, gzip: true);
                PowerLogWriter.Write(gz, powerLines);
                Assert.Equal(powerLines, PowerLogWriter.Read(gz));
                if (g == 0)
                {
                    var plain = PowerLogWriter.PathFor(basePath, gzip: false);
                    PowerLogWriter.Write(plain, powerLines);
                    Assert.Equal(powerLines, PowerLogWriter.Read(plain));
                    Assert.True(new FileInfo(gz).Length < new FileInfo(plain).Length);
                }
                Assert.True(events.Flush(TimeSpan.FromSeconds(10)));
                Assert.Null(events.LastError);
                eventPaths.Add(events.Path);
            }

            Assert.Equal(100, mainPaths.Distinct().Count());
            foreach (var p in mainPaths)
            {
                var json = JObject.Parse(File.ReadAllText(p));
                Assert.Equal("complete", (string?)json["status"]);
                Assert.NotNull(json["result"]?["mmr_after"]);
                Assert.True(File.Exists(Path.ChangeExtension(p, null) + ".power.log.gz"));
            }
            foreach (var p in eventPaths)
            {
                var lines = File.ReadAllLines(p);
                Assert.Equal(24, lines.Length);
                Assert.All(lines, l => JObject.Parse(l));
            }
            Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(journal.Directory));
        }
    }
}
