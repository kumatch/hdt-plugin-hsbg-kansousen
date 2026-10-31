using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HsbgKansousen.Output
{
    /// <summary>
    /// 補助ファイル 1: &lt;name&gt;.events.jsonl。1 行 1 JSON をゲーム中に逐次追記する（docs/log-schema.md）。
    /// 呼び出し側（ゲームスレッド）はキューに積むだけで、書き込みはバックグラウンドで行う。HDT 非依存。
    /// </summary>
    public sealed class EventsJsonlWriter : IDisposable
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private readonly object _drainGate = new object();
        private int _draining;
        private Task _drainTask = Task.CompletedTask;
        private int _written;

        public string Path { get; }
        public Exception? LastError { get; private set; }
        public int WrittenLines => _written;

        public EventsJsonlWriter(string path)
        {
            Path = path;
        }

        /// <summary>1 行を組み立てる。キーの順序は t, turn, type, その他の順。</summary>
        public static string Line(DateTimeOffset at, int turn, string type, IEnumerable<KeyValuePair<string, object?>>? fields = null)
        {
            var o = new JObject
            {
                ["t"] = at.ToString("o"),
                ["turn"] = turn,
                ["type"] = type,
            };
            if (fields != null)
            {
                foreach (var kv in fields)
                    o[kv.Key] = kv.Value == null ? JValue.CreateNull() : JToken.FromObject(kv.Value);
            }
            return o.ToString(Formatting.None);
        }

        public void Append(DateTimeOffset at, int turn, string type, params KeyValuePair<string, object?>[] fields)
            => Append(Line(at, turn, type, fields));

        public void Append(string line)
        {
            _queue.Enqueue(line);
            ScheduleDrain();
        }

        /// <summary>キューにある行がすべて書かれるまで待つ（最大 timeout）。</summary>
        public bool Flush(TimeSpan timeout)
        {
            ScheduleDrain();
            Task task;
            lock (_drainGate) task = _drainTask;
            try
            {
                return task.Wait(timeout) && _queue.IsEmpty;
            }
            catch
            {
                return false;
            }
        }

        private void ScheduleDrain()
        {
            if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
                return;
            lock (_drainGate)
                _drainTask = Task.Run(Drain);
        }

        private void Drain()
        {
            try
            {
                while (true)
                {
                    var lines = new List<string>();
                    while (_queue.TryDequeue(out var l)) lines.Add(l);
                    if (lines.Count == 0) break;
                    try
                    {
                        var dir = System.IO.Path.GetDirectoryName(Path);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        using (var fs = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        using (var w = new StreamWriter(fs, Utf8NoBom))
                        {
                            foreach (var l in lines) w.Write(l + "\n");
                        }
                        Interlocked.Add(ref _written, lines.Count);
                    }
                    catch (Exception ex)
                    {
                        LastError = ex;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _draining, 0);
                // ドレイン終了後に積まれた行があれば拾い直す
                if (!_queue.IsEmpty) ScheduleDrain();
            }
        }

        /// <summary>未書き込みの行を捨て、書き込み中のドレインを待ってからファイルを消す。以後 Append しないこと。</summary>
        public bool Discard(TimeSpan timeout)
        {
            while (_queue.TryDequeue(out _)) { }
            Flush(timeout);
            try
            {
                if (File.Exists(Path)) File.Delete(Path);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex;
                return false;
            }
        }

        public void Dispose() => Flush(TimeSpan.FromSeconds(2));
    }
}
