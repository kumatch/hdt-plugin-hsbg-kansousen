using System;
using System.IO;
using System.Text.RegularExpressions;

namespace HsbgKansousen.Config
{
    /// <summary>出力先フォルダ（config.json の output_dir）の検証と表記。設定画面から使う。HDT 非依存。</summary>
    public static class OutputDirectory
    {
        public const string Default = @"%USERPROFILE%\Documents\HSBG-Logs";

        private static readonly Regex UnresolvedVariable = new Regex(@"%[^%\\]+%");

        /// <summary>
        /// 入力値を環境変数展開してフルパスにし、フォルダを作って書き込めるか試す（副作用としてフォルダが作られる）。
        /// 使えれば null と resolved、使えなければ理由（設定画面に出す英文）を返す。
        /// </summary>
        public static string? Check(string? raw, out string resolved)
        {
            resolved = "";
            if (string.IsNullOrWhiteSpace(raw))
                return "Enter a folder.";
            var expanded = Environment.ExpandEnvironmentVariables(raw!.Trim());
            if (UnresolvedVariable.IsMatch(expanded))
                return "Unknown environment variable: " + UnresolvedVariable.Match(expanded).Value;
            string full;
            try
            {
                var root = Path.GetPathRoot(expanded);
                if (string.IsNullOrEmpty(root) || root == @"\" || root == "/")
                    return @"Use a full path (for example D:\HSBG-Logs).";
                full = Path.GetFullPath(expanded);
            }
            catch (Exception ex)
            {
                return "Invalid path: " + ex.Message;
            }
            try
            {
                Directory.CreateDirectory(full);
                var probe = Path.Combine(full, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                return "Cannot write to this folder: " + ex.Message;
            }
            resolved = full;
            return null;
        }

        /// <summary>
        /// ユーザープロファイル配下のパスを %USERPROFILE%\... に書き換える（config.json にユーザー名を残さず、
        /// 既定値と同じ書き方にするため）。配下でなければそのまま。
        /// </summary>
        public static string ToPortable(string path, string userProfile)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(userProfile))
                return path;
            var profile = userProfile.TrimEnd('\\', '/');
            if (string.Equals(path.TrimEnd('\\', '/'), profile, StringComparison.OrdinalIgnoreCase))
                return "%USERPROFILE%";
            if (path.Length > profile.Length
                && path.StartsWith(profile, StringComparison.OrdinalIgnoreCase)
                && (path[profile.Length] == '\\' || path[profile.Length] == '/'))
                return "%USERPROFILE%" + path.Substring(profile.Length);
            return path;
        }
    }
}
