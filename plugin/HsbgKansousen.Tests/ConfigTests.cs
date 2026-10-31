using System;
using System.IO;
using HsbgKansousen.Config;
using Xunit;

namespace HsbgKansousen.Tests
{
    public sealed class ConfigTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hsbg-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Check_RejectsEmpty(string? raw)
        {
            Assert.NotNull(OutputDirectory.Check(raw, out var resolved));
            Assert.Equal("", resolved);
        }

        [Theory]
        [InlineData("HSBG-Logs")]
        [InlineData(@"logs\HSBG")]
        [InlineData(@"\HSBG-Logs")]
        public void Check_RejectsRelativePaths(string raw)
        {
            Assert.NotNull(OutputDirectory.Check(raw, out _));
        }

        [Fact]
        public void Check_RejectsUnknownEnvironmentVariable()
        {
            var error = OutputDirectory.Check(@"%HSBG_TEST_NO_SUCH_VAR%\logs", out _);
            Assert.Contains("%HSBG_TEST_NO_SUCH_VAR%", error);
        }

        [Fact]
        public void Check_CreatesFolderAndLeavesNoProbeFile()
        {
            var dir = Path.Combine(_root, "new", "logs");
            Assert.Null(OutputDirectory.Check(dir, out var resolved));
            Assert.Equal(dir, resolved);
            Assert.True(Directory.Exists(dir));
            Assert.Empty(Directory.GetFileSystemEntries(dir));
        }

        [Fact]
        public void Check_ExpandsEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable("HSBG_TEST_ROOT", _root);
            try
            {
                Assert.Null(OutputDirectory.Check(@" %HSBG_TEST_ROOT%\logs ", out var resolved));
                Assert.Equal(Path.Combine(_root, "logs"), resolved);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HSBG_TEST_ROOT", null);
            }
        }

        [Theory]
        [InlineData(@"C:\Users\me\Documents\HSBG-Logs", @"%USERPROFILE%\Documents\HSBG-Logs")]
        [InlineData(@"c:\users\ME\Logs", @"%USERPROFILE%\Logs")]
        [InlineData(@"C:\Users\me", "%USERPROFILE%")]
        [InlineData(@"C:\Users\me\", "%USERPROFILE%")]
        [InlineData(@"C:\Users\me2\Logs", @"C:\Users\me2\Logs")] // check_private: ok (another user)
        [InlineData(@"D:\HSBG-Logs", @"D:\HSBG-Logs")]
        public void ToPortable_ReplacesOnlyTheProfileFolder(string path, string expected)
        {
            Assert.Equal(expected, OutputDirectory.ToPortable(path, @"C:\Users\me"));
        }

        [Fact]
        public void Default_IsTheDocumentedFolder()
        {
            Assert.Equal(@"%USERPROFILE%\Documents\HSBG-Logs", new PluginConfig().OutputDir);
        }

        [Fact]
        public void Save_ThenLoad_RoundTripsAndLeavesNoTempFile()
        {
            var path = Path.Combine(_root, "config.json");
            var config = new PluginConfig { OutputDir = @"D:\HSBG-Logs", WritePowerLog = false, SpikeDump = true };
            config.Save(path);
            config.SnapshotHand = false;
            config.Save(path); // 2 回目は File.Replace の経路

            var loaded = PluginConfig.Load(path);
            Assert.Equal(@"D:\HSBG-Logs", loaded.OutputDir);
            Assert.False(loaded.WritePowerLog);
            Assert.False(loaded.SnapshotHand);
            Assert.True(loaded.SpikeDump);
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void MigrateLegacy_CopiesTheOldConfigOnlyWhenTheNewOneIsMissing()
        {
            var legacy = Path.Combine(_root, "HsbgLogger", "config.json");
            var current = Path.Combine(_root, "HsbgKansousen", "config.json");
            Assert.False(PluginConfig.MigrateLegacy(current, legacy)); // nothing to copy
            new PluginConfig { OutputDir = @"D:\HSBG-Logs" }.Save(legacy);

            Assert.True(PluginConfig.MigrateLegacy(current, legacy));
            Assert.Equal(@"D:\HSBG-Logs", PluginConfig.Load(current).OutputDir);
            Assert.True(File.Exists(legacy)); // the old folder is kept

            new PluginConfig { OutputDir = @"E:\Other" }.Save(legacy);
            Assert.False(PluginConfig.MigrateLegacy(current, legacy)); // never overwrites the new one
            Assert.Equal(@"D:\HSBG-Logs", PluginConfig.Load(current).OutputDir);
        }

        [Fact]
        public void Clone_IsIndependent()
        {
            var config = new PluginConfig { OutputDir = @"D:\A" };
            var copy = config.Clone();
            copy.OutputDir = @"D:\B";
            Assert.Equal(@"D:\A", config.OutputDir);
        }
    }
}
