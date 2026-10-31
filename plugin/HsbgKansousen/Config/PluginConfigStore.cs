using System;
using System.IO;
using HsbgKansousen.Diagnostics;

namespace HsbgKansousen.Config
{
    /// <summary>config.json の読み込みと、ファイルが更新されていたときの再読み込み（ゲーム開始時とメニュー操作時）。</summary>
    public sealed class PluginConfigStore
    {
        private readonly object _gate = new object();
        private DateTime _loadedWriteTime;

        public string Path { get; }
        public PluginConfig Current { get; private set; }

        public PluginConfigStore(string path)
        {
            Path = path;
            Current = PluginConfig.Load(path);
            _loadedWriteTime = SafeWriteTime();
        }

        public static PluginConfigStore Default() => new PluginConfigStore(PluginConfig.DefaultPath);

        /// <summary>ファイルの更新時刻が変わっていれば読み直す。読み直したら true。</summary>
        public bool ReloadIfChanged()
        {
            lock (_gate)
            {
                var wt = SafeWriteTime();
                if (wt == _loadedWriteTime) return false;
                Reload();
                return true;
            }
        }

        public void Reload()
        {
            lock (_gate)
            {
                Current = PluginConfig.Load(Path);
                _loadedWriteTime = SafeWriteTime();
                PluginLogger.Info($"config reloaded: output_dir={Current.ResolveOutputDir()} events={Current.WriteEventsJsonl} power_log={Current.WritePowerLog} hand={Current.SnapshotHand} spike={Current.SpikeDump}");
            }
        }

        private DateTime SafeWriteTime()
        {
            try
            {
                return File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}
