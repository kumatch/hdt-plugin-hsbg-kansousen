using System;
using System.Collections.Generic;
using System.IO;
using HsbgKansousen.Model;
using HsbgKansousen.Output;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class PartialLogJournalTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hsbg-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Fact]
        public void Save_ThenDelete()
        {
            var j = new PartialLogJournal(_root);
            var log = SampleLogs.Phase2Minimal();
            j.Save(log);
            var path = j.GetPath(log.Game.GameId);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
            j.Delete(log.Game.GameId);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void RecoverAll_MovesToRegularPathAsIncomplete()
        {
            var j = new PartialLogJournal(_root);
            var w = new GameLogWriter(_root);
            var log = SampleLogs.Phase2Minimal();
            log.Status = LogStatus.Complete; // ジャーナル上の値に関わらず incomplete になる
            j.Save(log);

            var warnings = new List<string>();
            var recovered = j.RecoverAll(w, warnings.Add);

            Assert.Single(recovered);
            Assert.Equal(w.GetPath(log), recovered[0]);
            Assert.Empty(warnings);
            Assert.False(File.Exists(j.GetPath(log.Game.GameId)));
            var json = JObject.Parse(File.ReadAllText(recovered[0]));
            Assert.Equal("incomplete", (string?)json["status"]);
            Assert.Contains("recovered from partial journal", json["notes"]!.ToString());
        }

        [Fact]
        public void RecoverAll_SkipsWhenRegularLogExists()
        {
            var j = new PartialLogJournal(_root);
            var w = new GameLogWriter(_root);
            var log = SampleLogs.Complete();
            w.Write(log);
            j.Save(log);

            var recovered = j.RecoverAll(w);

            Assert.Empty(recovered);
            Assert.False(File.Exists(j.GetPath(log.Game.GameId)));
            var json = JObject.Parse(File.ReadAllText(w.GetPath(log)));
            Assert.Equal("complete", (string?)json["status"]);
        }

        [Fact]
        public void RecoverAll_RenamesCorruptFile()
        {
            var j = new PartialLogJournal(_root);
            Directory.CreateDirectory(j.Directory);
            var bad = Path.Combine(j.Directory, "broken.json");
            File.WriteAllText(bad, "{ not json");
            var warnings = new List<string>();

            var recovered = j.RecoverAll(new GameLogWriter(_root), warnings.Add);

            Assert.Empty(recovered);
            Assert.Single(warnings);
            Assert.False(File.Exists(bad));
            Assert.True(File.Exists(bad + ".corrupt"));
        }

        [Fact]
        public void RecoverAll_NoDirectory_IsNoop()
        {
            var j = new PartialLogJournal(Path.Combine(_root, "missing"));
            Assert.Empty(j.RecoverAll(new GameLogWriter(_root)));
        }
    }
}
