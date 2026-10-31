using System;
using System.IO;
using System.Text;
using HsbgKansousen.Model;
using Newtonsoft.Json;

namespace HsbgKansousen.Output
{
    /// <summary>
    /// 主ログ JSON の命名・配置・アトミック書き込み。HDT 非依存。
    /// 配置: &lt;root&gt;\YYYY\MM\YYYYMMDD-HHMMSS_&lt;game_id&gt;.json（開始時刻のローカル日時）
    /// </summary>
    public sealed class GameLogWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public string RootDirectory { get; }

        public GameLogWriter(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("rootDirectory is required", nameof(rootDirectory));
            RootDirectory = rootDirectory;
        }

        /// <summary>拡張子を除いたパス。補助ファイルは &lt;base&gt;.events.jsonl / &lt;base&gt;.power.log になる。</summary>
        public string GetBasePath(DateTimeOffset startedAt, string gameId)
        {
            var name = $"{startedAt:yyyyMMdd-HHmmss}_{SanitizeFileName(gameId)}";
            return Path.Combine(RootDirectory, startedAt.ToString("yyyy"), startedAt.ToString("MM"), name);
        }

        public string GetBasePath(GameLog log)
        {
            var startedAt = log.Game.StartedAt ?? DateTimeOffset.Now;
            var gameId = string.IsNullOrEmpty(log.Game.GameId) ? "unknown" : log.Game.GameId;
            return GetBasePath(startedAt, gameId);
        }

        public string GetPath(DateTimeOffset startedAt, string gameId) => GetBasePath(startedAt, gameId) + ".json";

        public string GetPath(GameLog log) => GetBasePath(log) + ".json";

        public static string EventsPath(string basePath) => basePath + ".events.jsonl";

        public static string PowerLogPath(string basePath) => PowerLogWriter.PathFor(basePath, gzip: false);

        /// <summary>ログを書き（既存なら上書き）、書いたパスを返す。</summary>
        public string Write(GameLog log)
        {
            var path = GetPath(log);
            WriteAtomic(path, Serialize(log));
            return path;
        }

        public static string Serialize(GameLog log) => JsonConvert.SerializeObject(log, JsonSettings.Create());

        public static GameLog? Deserialize(string json) => JsonConvert.DeserializeObject<GameLog>(json, JsonSettings.Create());

        /// <summary>一時ファイルに書いてから置き換える。途中でクラッシュしても壊れたファイルが残らない。</summary>
        public static void WriteAtomic(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Utf8NoBom);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        public static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
