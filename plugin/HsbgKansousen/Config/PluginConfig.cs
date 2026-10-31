using System;
using System.IO;
using System.Text;
using HsbgKansousen.Diagnostics;
using Newtonsoft.Json;

namespace HsbgKansousen.Config
{
    /// <summary>
    /// %APPDATA%\HearthstoneDeckTracker\HsbgKansousen\config.json。無ければデフォルトを書き出す。
    /// 主な項目は設定画面（UI/SettingsWindow）で変えられる。spike_dump と mmr_after_wait_sec は JSON を直接編集。
    /// </summary>
    public sealed class PluginConfig
    {
        public static string DefaultDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "HsbgKansousen");

        public static string DefaultPath => Path.Combine(DefaultDirectory, "config.json");

        /// <summary>0.11.0 で改名する前（HsbgLogger）の設定フォルダ</summary>
        public static string LegacyDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "HsbgLogger");

        /// <summary>
        /// 新しい config.json が無く、改名前の config.json があれば写す（出力先などの設定を引き継ぐ）。写したら true。
        /// 古いフォルダは消さない（改名前のプラグインのログ plugin.log が残っているため）。
        /// </summary>
        public static bool MigrateLegacy(string newPath, string legacyPath)
        {
            if (File.Exists(newPath) || !File.Exists(legacyPath))
                return false;
            var dir = Path.GetDirectoryName(newPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Copy(legacyPath, newPath);
            return true;
        }

        [JsonProperty("output_dir")]
        public string OutputDir { get; set; } = OutputDirectory.Default;

        [JsonProperty("write_events_jsonl")]
        public bool WriteEventsJsonl { get; set; } = true;

        [JsonProperty("write_power_log")]
        public bool WritePowerLog { get; set; } = true;

        /// <summary>power.log を gzip で書く（&lt;name&gt;.power.log.gz）。1 ゲーム約 29 MB が約 3 MB になる</summary>
        [JsonProperty("power_log_gzip")]
        public bool PowerLogGzip { get; set; } = true;

        /// <summary>ショップ終了時の手札を turns[].hand_end_of_shop に記録する（2026-10-11 から既定 true。分析で手札の呪文・投資カードを見るため）</summary>
        [JsonProperty("snapshot_hand")]
        public bool SnapshotHand { get; set; } = true;

        /// <summary>Phase 1 の全 Entity ダンプ（.spike）を有効にする。検証用。</summary>
        [JsonProperty("spike_dump")]
        public bool SpikeDump { get; set; } = false;

        /// <summary>ゲーム終了後に MMR 後値を待つ最大秒数</summary>
        [JsonProperty("mmr_after_wait_sec")]
        public int MmrAfterWaitSec { get; set; } = 30;

        public string ResolveOutputDir() => Environment.ExpandEnvironmentVariables(OutputDir);

        public static PluginConfig Load() => Load(DefaultPath);

        public static PluginConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var loaded = JsonConvert.DeserializeObject<PluginConfig>(json);
                    if (loaded != null)
                        return loaded;
                    PluginLogger.Warn("config.json is empty; using defaults");
                }
                else
                {
                    var config = new PluginConfig();
                    config.Save(path);
                    PluginLogger.Info("wrote default config: " + path);
                    return config;
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "PluginConfig.Load(" + path + ")");
            }
            return new PluginConfig();
        }

        public PluginConfig Clone() =>
            JsonConvert.DeserializeObject<PluginConfig>(JsonConvert.SerializeObject(this)) ?? new PluginConfig();

        /// <summary>一時ファイルに書いてから置き換える（HDT の終了や設定画面の保存が重なっても壊れた JSON を残さない）。</summary>
        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }
}
