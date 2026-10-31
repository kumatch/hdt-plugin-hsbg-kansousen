using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HsbgKansousen.Output
{
    /// <summary>
    /// 補助ファイル 2: &lt;name&gt;.power.log(.gz)。HDT が保持するゲーム単位の Power.log 行（Core.Game.PowerLog）をそのまま書く。
    /// HDT の Replays 機能は BG では動かないため自前で書く（docs/hdt-research.md §3.10）。
    /// 実測 1 ゲーム約 20 万行 / 29 MB なので既定は gzip（約 1/10）。HDT 非依存。
    /// </summary>
    public static class PowerLogWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string PathFor(string basePath, bool gzip) => basePath + (gzip ? ".power.log.gz" : ".power.log");

        public static void Write(string path, IReadOnlyList<string> lines) => Write(path, lines, gzip: path.EndsWith(".gz", System.StringComparison.OrdinalIgnoreCase));

        public static void Write(string path, IReadOnlyList<string> lines, bool gzip)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (Stream target = gzip ? new GZipStream(fs, CompressionLevel.Optimal) : fs)
            using (var w = new StreamWriter(target, Utf8NoBom, 1 << 16))
            {
                foreach (var line in lines) w.Write(line + "\n");
            }
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        /// <summary>読み戻し（テストと分析側の参考実装）。</summary>
        public static List<string> Read(string path)
        {
            var lines = new List<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (Stream source = path.EndsWith(".gz", System.StringComparison.OrdinalIgnoreCase) ? new GZipStream(fs, CompressionMode.Decompress) : fs)
            using (var r = new StreamReader(source, Utf8NoBom))
            {
                string? line;
                while ((line = r.ReadLine()) != null) lines.Add(line);
            }
            return lines;
        }
    }
}
