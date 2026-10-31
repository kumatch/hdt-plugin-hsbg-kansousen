using System;
using System.IO;
using System.Text;

namespace HsbgKansousen.Diagnostics
{
    /// <summary>
    /// プラグイン自身のログ。HDT の hdt_log.txt とは別に
    /// %APPDATA%\HearthstoneDeckTracker\HsbgKansousen\plugin.log に追記する。
    /// 例外を外に出さない (ログ書き込みの失敗で HDT を落とさない)。
    /// </summary>
    public static class PluginLogger
    {
        private static readonly object Gate = new object();

        public static string Directory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HearthstoneDeckTracker", "HsbgKansousen");

        public static string FilePath => Path.Combine(Directory, "plugin.log");

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Error(Exception ex, string? context = null)
            => Write("ERROR", (context == null ? "" : context + ": ") + ex);

        private const long MaxBytes = 2 * 1024 * 1024;

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    RotateIfNeeded();
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}|{level}|{message}{Environment.NewLine}";
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // ログ出力の失敗は握りつぶす
            }
        }

        /// <summary>2 MB を超えたら plugin.log.1 に退避する（1 世代のみ）。</summary>
        private static void RotateIfNeeded()
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length < MaxBytes)
                return;
            var backup = FilePath + ".1";
            if (File.Exists(backup))
                File.Delete(backup);
            File.Move(FilePath, backup);
        }
    }
}
