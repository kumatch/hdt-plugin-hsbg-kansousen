using System;
using System.Collections.Generic;
using System.IO;
using HsbgKansousen.Model;

namespace HsbgKansousen.Output
{
    /// <summary>
    /// クラッシュ対策の部分ログ。各ターン開始時に &lt;root&gt;\.partial\&lt;game_id&gt;.json へ現状を上書きし、
    /// 正常終了時に削除する。次回のプラグイン読み込み時に残っていれば status: "incomplete" として正規の場所へ移す（docs/architecture.md §2.4）。
    /// HDT 非依存。
    /// </summary>
    public sealed class PartialLogJournal
    {
        public const string DirectoryName = ".partial";

        public string Directory { get; }

        public PartialLogJournal(string rootDirectory)
        {
            Directory = Path.Combine(rootDirectory, DirectoryName);
        }

        public string GetPath(string gameId) => Path.Combine(Directory, GameLogWriter.SanitizeFileName(gameId) + ".json");

        public void Save(GameLog log) => Save(log.Game.GameId, GameLogWriter.Serialize(log));

        public void Save(string gameId, string json) => GameLogWriter.WriteAtomic(GetPath(gameId), json);

        public void Delete(string gameId)
        {
            var path = GetPath(gameId);
            if (File.Exists(path)) File.Delete(path);
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
        }

        /// <summary>残骸を incomplete ログとして正規の場所へ移し、移した先のパスを返す。壊れたファイルは .corrupt にリネームする。</summary>
        public List<string> RecoverAll(GameLogWriter writer, Action<string>? onWarning = null)
        {
            var recovered = new List<string>();
            if (!System.IO.Directory.Exists(Directory)) return recovered;
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
            {
                try
                {
                    var log = GameLogWriter.Deserialize(File.ReadAllText(file));
                    if (log == null || string.IsNullOrEmpty(log.Game.GameId))
                        throw new InvalidDataException("empty or invalid partial log");
                    if (log.Status != LogStatus.NotBattlegrounds)
                        log.Status = LogStatus.Incomplete;
                    log.Notes.Add("recovered from partial journal (HDT or plugin stopped during the game)");
                    var target = writer.GetPath(log);
                    if (File.Exists(target))
                    {
                        // 正規のログが既にある（正常終了していた）なら残骸を捨てるだけ
                        File.Delete(file);
                        continue;
                    }
                    writer.Write(log);
                    File.Delete(file);
                    recovered.Add(target);
                }
                catch (Exception ex)
                {
                    onWarning?.Invoke($"partial log {file} could not be recovered: {ex.Message}");
                    try
                    {
                        var corrupt = file + ".corrupt";
                        if (File.Exists(corrupt)) File.Delete(corrupt);
                        File.Move(file, corrupt);
                    }
                    catch
                    {
                        // 放置（次回も警告が出る）
                    }
                }
            }
            return recovered;
        }
    }
}
