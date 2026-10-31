using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using Hearthstone_Deck_Tracker.Stats;
using Hearthstone_Deck_Tracker.Utility.Battlegrounds;
using HsbgKansousen.Config;
using HsbgKansousen.Diagnostics;
using HsbgKansousen.Logic;
using HsbgKansousen.Model;
using HsbgKansousen.Output;

namespace HsbgKansousen.Capture
{
    /// <summary>
    /// 1 ゲームのライフサイクル（開始 → ショップ → 戦闘 → 終了 → MMR 後値）を管理し、GameLog を組み立てて書き出す。
    /// HDT の型に触るのは Capture/ だけ。すべてのハンドラは例外を外に出さない。
    ///
    /// 1 ターンの流れ（docs/hdt-research.md §3.15 で確認済み）:
    ///   OnTurnStart(Player) = ショップ開始 → OnUpdate で行動を差分検出 → OnTurnStart(Opponent) = ショップ終了（盤面確定）
    ///   → IsBattlegroundsCombatPhase が true になった瞬間に相手ヒーロー/盤面を取得 → OnEntityWillTakeDamage で与被ダメ
    ///   → 次の OnTurnStart(Player) で前ターンの戦闘結果と Bob's Buddy を確定
    ///
    /// 補助ファイル: events.jsonl（逐次追記）、power.log（終了時）、.partial ジャーナル（各ターン開始時に上書き、終了時に削除）
    ///
    /// 再接続（P-22）: Hearthstone が落ちて同じゲームに戻ると HDT は OnGameStart を出し直す。GameHandle が同じなら
    /// ResumeAfterReconnect で同じ記録を続け、見られなかった戦闘は ResolveInterruptedCombat で推定する。HDT ごと落ちた場合は対象外（.partial を回収）
    /// </summary>
    public sealed class GameSession
    {
        private readonly object _gate = new object();
        private readonly PluginConfigStore _store;
        private readonly string _pluginVersion;
        private PluginConfig _config;
        private GameLogWriter _writer;
        private PartialLogJournal _journal;
        private EventsJsonlWriter? _events;
        private string? _basePath;

        private GameLog? _log;
        private bool _active;
        private readonly Dictionary<int, OpponentRecord> _opponents = new Dictionary<int, OpponentRecord>();
        private readonly Dictionary<int, int> _trinketFirstSeenTurn = new Dictionary<int, int>();

        // ヒーロー選択の追跡。選択肢の Entity はターン 1 で消えるので、見えている間にアーマーを覚えておく
        private int[]? _lastOffered;
        private List<HeroChoice>? _firstOffered;
        private readonly Dictionary<int, int> _choiceArmorByDbfId = new Dictionary<int, int>();
        private DateTime _lastHeroPickCheck = DateTime.MinValue;

        // ショップ中の行動検出
        private readonly ShopActionTracker _tracker = new ShopActionTracker();
        /// <summary>手札に現れた Entity ID → 作成元（CardSource。無ければ null）。ゲーム単位で保持し EntitySnapshotter.Tick が使う</summary>
        private readonly Dictionary<int, string?> _handSourceCache = new Dictionary<int, string?>();
        private bool _shopPhase;
        private int _currentTurn;

        // 戦闘
        private bool _lastCombatPhase;
        private int _combatTurn;
        private DateTime? _opponentBoardRecheckAt;
        private readonly List<HeroDamageEvent> _damageEvents = new List<HeroDamageEvent>();

        // 神祇（Secret Deity の Sigil）。戦闘中は毎 tick 読み、戦闘の確定時に turns[].deity / combat.opponent_deity へ
        private readonly DeityTracker _deity = new DeityTracker();
        private Entity? _playerSigil;
        private Entity? _opponentSigil;
        private DateTime _nextOpponentSigilScan = DateTime.MinValue;
        /// <summary>ショップ終了時点の最大 Entity ID。これより新しいミニオンが戦闘中に作られたもの（覚醒判定）</summary>
        private int _shopEndMaxEntityId;

        // OnUpdate の処理時間（受け入れ基準: 平均 1ms 未満）
        private long _tickTotalTicks;
        private long _tickMaxTicks;
        private int _tickCount;

        // 再接続（P-22）。HDT は再接続したゲームでも OnGameStart を出し直し GameStats.GameId も変わるので、
        // サーバー側のゲーム番号（MetaData.ServerInfo.GameHandle）が同じなら同じ記録を続ける
        private uint _gameHandle;
        /// <summary>このゲームの HDT の GameStats.GameId（再接続ごとに増える）。Bob's Buddy の結果はそのときの ID で引く</summary>
        private readonly List<Guid> _hdtGameIds = new List<Guid>();
        /// <summary>最後の再接続の直前に記録していたターン（0 = 再接続なし）</summary>
        private int _resumedAtTurn;
        /// <summary>再接続後、ターン番号とプレイヤーが読めるまで待っている（それまで OnUpdate の差分検出をしない）</summary>
        private bool _resumePending;
        /// <summary>結果を見ないまま再接続した戦闘のターン（次のショップ開始時に HP + アーマーの差から推定する）</summary>
        private int _interruptedCombatTurn;
        /// <summary>戦闘直前（ショップ終了時 / 戦闘開始時）の自分の HP + アーマー</summary>
        private int? _combatStartMine;
        /// <summary>ショップ中に落ちたターンの、落ちる前の行動回数（再接続後に同じショップが続けば足す）</summary>
        private TurnActions? _resumedShopActions;
        private int _resumedShopTurn;
        private bool _skipResumedShopSnapshot;
        // power.log: HDT は再接続時に Core.Game.PowerLog を空にし、前の行を残さない（StoredPowerLogs は internal で使われていない）ので、
        // ターン開始ごとに自分でコピーしておく。_powerLogBefore = 再接続前の区間、_powerLogCurrent = 今の区間のコピー済みの行
        private readonly List<string> _powerLogBefore = new List<string>();
        private readonly List<string> _powerLogCurrent = new List<string>();

        // ゲーム終了後の MMR 後値
        private GameStats? _endedStats;
        private DateTime _endedAt;
        private bool _awaitingRating;

        public bool Enabled { get; set; } = true;

        public GameSession(PluginConfigStore store, string pluginVersion)
        {
            _store = store;
            _pluginVersion = pluginVersion;
            _config = store.Current;
            _writer = new GameLogWriter(_config.ResolveOutputDir());
            _journal = new PartialLogJournal(_config.ResolveOutputDir());
        }

        // ------------------------------------------------------------------ events

        public void OnGameStart()
        {
            if (!Enabled) return;
            try
            {
                lock (_gate)
                {
                    var game = Core.Game;
                    var handle = SafeGameHandle(game);
                    switch (Reconnect.Decide(_active && _log != null, _gameHandle, handle, game.IsBattlegroundsMatch))
                    {
                        case GameStartDecision.Resume:
                            ResumeAfterReconnect(game, handle);
                            return;
                        case GameStartDecision.CloseAndFresh:
                            CloseUnfinished(handle);
                            break;
                    }
                    ResetState();
                    ApplyConfig();

                    if (!game.IsBattlegroundsMatch)
                    {
                        PluginLogger.Info($"OnGameStart: not Battlegrounds ({game.CurrentGameType}); ignoring");
                        _log = null;
                        return;
                    }

                    var stats = game.CurrentGameStats;
                    var startedAt = stats != null && stats.StartTime != default ? new DateTimeOffset(stats.StartTime) : DateTimeOffset.Now;
                    _log = new GameLog
                    {
                        Status = LogStatus.Incomplete,
                        Generator =
                        {
                            PluginVersion = _pluginVersion,
                            HdtVersion = SafeHdtVersion(),
                            HearthstoneBuild = stats?.HearthstoneBuild,
                        },
                        Game =
                        {
                            GameId = stats?.GameId.ToString() ?? Guid.NewGuid().ToString(),
                            Variant = game.IsBattlegroundsDuosMatch ? "duos" : "solo",
                            GameType = game.CurrentGameType.ToString(),
                            StartedAt = startedAt,
                            Region = stats != null && stats.Region != Region.UNKNOWN ? stats.Region.ToString() : null,
                        },
                        Result = { MmrBefore = game.CurrentBattlegroundsRating },
                    };
                    _basePath = _writer.GetBasePath(_log);
                    _events = _config.WriteEventsJsonl ? new EventsJsonlWriter(GameLogWriter.EventsPath(_basePath)) : null;
                    if (game.IsBattlegroundsDuosMatch)
                        Note("duos: not fully supported (MVP targets solo)");
                    _active = true;
                    _gameHandle = handle;
                    if (stats != null) _hdtGameIds.Add(stats.GameId);
                    Emit("game_start", 0, F("game_id", _log.Game.GameId), F("game_type", _log.Game.GameType), F("mmr_before", _log.Result.MmrBefore));
                    PluginLogger.Info($"OnGameStart: {_log.Game.GameId} type={_log.Game.GameType} mmr={_log.Result.MmrBefore} handle={handle}");
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "GameSession.OnGameStart");
            }
        }

        public void OnTurnStart(ActivePlayer player)
        {
            if (!Enabled) return;
            try
            {
                lock (_gate)
                {
                    if (!_active || _log == null) return;
                    var game = Core.Game;
                    CopyPowerLog(game);
                    var turn = game.GetTurnNumber();
                    if (turn <= 0) return;
                    _currentTurn = turn;
                    // 再接続後に最初のターン切り替えが来たら、その時点の状態から通常どおり追跡を始める
                    _resumePending = false;
                    if (player == ActivePlayer.Player)
                        OnShopStart(game, turn);
                    else
                        OnShopEnd(game, turn);
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "GameSession.OnTurnStart");
            }
        }

        public void OnEntityWillTakeDamage(PredamageInfo info)
        {
            if (!Enabled) return;
            try
            {
                lock (_gate)
                {
                    if (!_active || _log == null) return;
                    var e = info.Entity;
                    if (!e.IsHero) return;
                    _damageEvents.Add(new HeroDamageEvent
                    {
                        EntityId = e.Id,
                        IsPlayerHero = e.IsControlledBy(Core.Game.Player.Id),
                        Value = info.Value,
                        At = DateTime.Now,
                    });
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "GameSession.OnEntityWillTakeDamage");
            }
        }

        public void OnGameEnd()
        {
            if (!Enabled) return;
            try
            {
                string? path = null;
                string? json = null;
                List<string>? powerLog = null;
                string? powerLogPath = null;
                string? gameId = null;
                EventsJsonlWriter? events = null;
                lock (_gate)
                {
                    if (!_active || _log == null) return;
                    var game = Core.Game;
                    var stats = game.CurrentGameStats;
                    var turn = game.GetTurnNumber();
                    var hero = FindPlayerHero(game);

                    if (_shopPhase)
                        FlushShopActions(game, turn);
                    var interrupted = _combatTurn > 0 && _combatTurn == _interruptedCombatTurn;
                    if (!interrupted)
                        ResolveCombat(game, _combatTurn > 0 ? _combatTurn : turn, hero, crossCheck: false);
                    ReadBobsBuddy(game, turn);

                    FinalizeHero(game, hero, turn);
                    UpdateLobby(game, turn, final: true);
                    if (interrupted)
                        ResolveInterruptedCombat(game, _combatTurn, HealthAndArmor(hero));
                    FillGameInfo(game);

                    var placement = hero?.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) ?? 0;
                    _log.Result.Placement = placement > 0 ? placement : (int?)null;
                    _log.Result.TotalTurns = turn;
                    _log.Result.EliminatedTurn = turn;
                    if (AbortedGame.IsBeforeFirstTurn(_log))
                    {
                        DiscardAborted("OnGameEnd");
                        return;
                    }
                    _log.Result.FinalTavernTier = hero != null && hero.GetTag(GameTag.PLAYER_TECH_LEVEL) > 0
                        ? hero.GetTag(GameTag.PLAYER_TECH_LEVEL)
                        : _log.Turns.LastOrDefault()?.TavernTier;
                    if (stats != null && stats.BattlegroundsRating > 0)
                        _log.Result.MmrBefore = stats.BattlegroundsRating;
                    if (_log.Result.MmrBefore == null)
                        Note("mmr_before unavailable");

                    var endedAt = stats != null && stats.EndTime != default ? new DateTimeOffset(stats.EndTime) : DateTimeOffset.Now;
                    _log.Game.EndedAt = endedAt;
                    if (_log.Game.StartedAt.HasValue)
                        _log.Game.DurationSec = (int)Math.Round((endedAt - _log.Game.StartedAt.Value).TotalSeconds);
                    _log.Game.LobbySize = _opponents.Count > 0 ? _opponents.Count + 1 : (int?)null;
                    _log.Opponents = _opponents.Values.OrderBy(o => o.PlayerId).ToList();

                    _log.Status = _log.Result.Placement.HasValue ? LogStatus.Complete : LogStatus.Incomplete;
                    if (!_log.Result.Placement.HasValue)
                        Note("placement unavailable at OnGameEnd");
                    ReportTickStats();
                    Emit("game_end", turn, F("placement", _log.Result.Placement), F("total_turns", turn), F("status", _log.Status));

                    if (_config.WritePowerLog)
                    {
                        try
                        {
                            powerLog = _powerLogBefore.Concat(game.PowerLog).ToList();
                            powerLogPath = PowerLogWriter.PathFor(_basePath!, _config.PowerLogGzip);
                        }
                        catch (Exception ex)
                        {
                            Note("power log unavailable: " + ex.Message);
                        }
                    }

                    _endedStats = stats;
                    _endedAt = DateTime.Now;
                    _awaitingRating = true;
                    _active = false;
                    _shopPhase = false;

                    path = _writer.GetPath(_log);
                    json = GameLogWriter.Serialize(_log);
                    gameId = _log.Game.GameId;
                    events = _events;
                    PluginLogger.Info($"OnGameEnd: placement={_log.Result.Placement} turns={turn} -> {path}");
                }

                var journal = _journal;
                Task.Run(() =>
                {
                    try
                    {
                        GameLogWriter.WriteAtomic(path!, json!);
                        PluginLogger.Info("wrote " + path);
                        journal.Delete(gameId!);
                        if (powerLog != null && powerLogPath != null)
                        {
                            PowerLogWriter.Write(powerLogPath, powerLog);
                            PluginLogger.Info($"wrote {powerLogPath} ({powerLog.Count} lines)");
                        }
                        events?.Flush(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        PluginLogger.Error(ex, "OnGameEnd write");
                    }
                });
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "GameSession.OnGameEnd");
            }
        }

        public void OnInMenu()
        {
            // ゲーム終了後のポーリングは Update() が続ける。ここでは何もしない
        }

        /// <summary>OnUpdate（約 100ms）から呼ばれる。差分検出のみで、I/O はしない（events.jsonl はキュー経由）。</summary>
        public void Update()
        {
            if (!Enabled) return;
            try
            {
                lock (_gate)
                {
                    if (_active && _log != null && (!_resumePending || ContinueAfterReconnect(Core.Game)))
                    {
                        var sw = Stopwatch.StartNew();
                        var game = Core.Game;
                        TrackHeroPick(game);
                        TrackCombatPhase(game);
                        if (_shopPhase && !game.IsBattlegroundsCombatPhase)
                            ObserveShop(game);
                        sw.Stop();
                        _tickCount++;
                        _tickTotalTicks += sw.ElapsedTicks;
                        if (sw.ElapsedTicks > _tickMaxTicks) _tickMaxTicks = sw.ElapsedTicks;
                    }
                    if (_awaitingRating)
                        PollRatingAfter();
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "GameSession.Update");
            }
        }

        /// <summary>プラグイン無効化 / HDT 終了時。進行中なら incomplete で書き出す。</summary>
        public void Flush()
        {
            try
            {
                lock (_gate)
                {
                    if (_log == null) return;
                    if (_active && AbortedGame.IsBeforeFirstTurn(_log))
                    {
                        DiscardAborted("Flush");
                        return;
                    }
                    if (_active)
                    {
                        _log.Status = LogStatus.Incomplete;
                        _log.Notes.Add("plugin unloaded during game");
                        _log.Opponents = _opponents.Values.OrderBy(o => o.PlayerId).ToList();
                        _active = false;
                        var path = _writer.Write(_log);
                        _journal.Delete(_log.Game.GameId);
                        PluginLogger.Info("Flush: wrote incomplete log " + path);
                    }
                    _awaitingRating = false;
                    _events?.Flush(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "GameSession.Flush");
            }
        }

        /// <summary>
        /// ターン 1 前に終わったゲームを書き出さずに捨てる（_gate 内で呼ぶ）。本体・power.log は書かず、
        /// 逐次書いていた events.jsonl と .partial ジャーナルを消し、MMR 後値も待たない。
        /// </summary>
        private void DiscardAborted(string where)
        {
            var gameId = _log!.Game.GameId;
            var events = _events;
            var journal = _journal;
            _active = false;
            _shopPhase = false;
            _awaitingRating = false;
            _log = null;
            _events = null;
            _basePath = null;
            PluginLogger.Info($"{where}: game {gameId} ended before turn 1; log discarded");
            Task.Run(() =>
            {
                try
                {
                    if (events != null && !events.Discard(TimeSpan.FromSeconds(5)))
                        PluginLogger.Warn("events.jsonl discard failed: " + events.LastError?.Message);
                    journal.Delete(gameId!);
                }
                catch (Exception ex)
                {
                    PluginLogger.Error(ex, "DiscardAborted");
                }
            });
        }

        // ------------------------------------------------------------------ reconnect (P-22)

        /// <summary>
        /// 同じサーバーのゲームに再接続した（_gate 内で呼ぶ）。記録・game_id・出力先・events.jsonl・.partial はそのまま引き継ぎ、
        /// Entity に依存する追跡の状態だけを捨てる。HDT はこの時点でまだゲームの状態を読み込んでいないので、続きは
        /// ContinueAfterReconnect（OnUpdate）か次の OnTurnStart で始める。
        /// </summary>
        private void ResumeAfterReconnect(GameV2 game, uint handle)
        {
            var log = _log!;
            var lastTurn = Math.Max(_currentTurn, _combatTurn);
            if (_shopPhase && _currentTurn > 0)
            {
                // ショップ中に落ちた: そこまでの行動を残し、このターンの戦闘は見られないものとして構える
                // （同じショップに戻ってショップ終了を見れば OnShopEnd が取り消す）。直前の値はショップ開始時の HP + アーマー
                SaveShopActionsBeforeReconnect();
                var rec = GetOrAddTurn(_currentTurn);
                rec.Combat ??= new CombatRecord();
                _combatTurn = _currentTurn;
                _combatStartMine = rec.Health.HasValue ? rec.Health.Value + rec.Armor.GetValueOrDefault() : (int?)null;
                _interruptedCombatTurn = _currentTurn;
            }
            else
            {
                var combat = _combatTurn > 0 ? log.Turns.FirstOrDefault(t => t.Turn == _combatTurn)?.Combat : null;
                _interruptedCombatTurn = combat != null && combat.Result == null ? _combatTurn : 0;
            }

            // HDT は PowerLog を空にしてから OnGameStart を出すので、ここまでにコピーした行が再接続前の区間になる
            _powerLogBefore.AddRange(_powerLogCurrent);
            _powerLogCurrent.Clear();

            _tracker.Reset();
            _shopPhase = false;
            _lastCombatPhase = false;
            _opponentBoardRecheckAt = null;
            _damageEvents.Clear();
            _deity.Reset();
            _playerSigil = null;
            _opponentSigil = null;
            _nextOpponentSigilScan = DateTime.MinValue;
            _handSourceCache.Clear();
            _resumedAtTurn = lastTurn;
            _resumePending = true;

            var stats = game.CurrentGameStats;
            if (stats != null && !_hdtGameIds.Contains(stats.GameId)) _hdtGameIds.Add(stats.GameId);
            var message = $"reconnect: resumed the same server game ({handle}) after turn {lastTurn}; HDT started it again as {stats?.GameId}";
            if (_interruptedCombatTurn > 0)
                message += $"; turn {_interruptedCombatTurn} combat was not seen to the end";
            log.Notes.Add(message);
            Emit("reconnect", lastTurn, F("game_handle", handle), F("hdt_game_id", stats?.GameId.ToString()),
                F("interrupted_combat_turn", _interruptedCombatTurn > 0 ? _interruptedCombatTurn : (int?)null),
                F("interrupted_shop_turn", _resumedShopActions != null && _resumedShopTurn == _currentTurn ? _currentTurn : (int?)null));
            PluginLogger.Info("OnGameStart: " + message);
            SaveJournal();
        }

        /// <summary>
        /// 再接続後の OnUpdate: ターン番号とプレイヤーが読めたら追跡を再開する。まだなら false（この tick は何もしない）。
        /// 落ちたターンのショップの続きなら行動の検出を再開し、戦闘中に戻ったならその戦闘を「始まり」として拾わない。
        /// </summary>
        private bool ContinueAfterReconnect(GameV2 game)
        {
            var turn = game.GetTurnNumber();
            if (turn <= 0 || game.PlayerEntity == null || FindPlayerHero(game) == null) return false;
            _resumePending = false;
            var combat = game.IsBattlegroundsCombatPhase;
            _lastCombatPhase = combat;
            if (combat)
                CaptureCombatAfterReconnect(game, turn);
            else if (turn == _currentTurn && _resumedShopActions != null && _resumedShopTurn == turn)
            {
                _tracker.Reset();
                _shopPhase = true;
                _skipResumedShopSnapshot = true;
            }
            PluginLogger.Info($"reconnect: tracking again at turn {turn} ({(combat ? "combat" : _shopPhase ? "shop, continuing" : "shop")})");
            return true;
        }

        /// <summary>
        /// 戦闘中に戻った: この戦闘は始まりから見ていないので、結果は次のショップ開始時に推定する。
        /// 相手がまだ分からなければヒーローだけ読む（盤面は戦闘の途中なので、戦闘開始時の盤面としては書かない）。
        /// ヒーローへのダメージは戦闘の最後に入るので、今の HP + アーマーは戦闘前の値として使える。
        /// </summary>
        private void CaptureCombatAfterReconnect(GameV2 game, int turn)
        {
            var rec = GetOrAddTurn(turn);
            rec.Combat ??= new CombatRecord();
            if (_combatTurn != turn)
            {
                if (_interruptedCombatTurn > 0 && _interruptedCombatTurn < turn)
                    _log!.Notes.Add($"reconnect: turn {_interruptedCombatTurn} combat result unknown (back in turn {turn})");
                _combatTurn = turn;
                _combatStartMine = null;
            }
            _combatStartMine ??= HealthAndArmor(FindPlayerHero(game));
            _interruptedCombatTurn = turn;
            if (!rec.Combat.OpponentHero.IsEmpty) return;
            var oppHero = EntitySnapshotter.OpponentHero(EntitySnapshotter.SafeEntities(game), game.Opponent.Id);
            if (oppHero == null) return;
            HdtCards.Fill(rec.Combat.OpponentHero, HdtCards.FromCardId(oppHero.CardId));
            rec.Combat.OpponentHealth = oppHero.Health;
            rec.Combat.OpponentArmor = oppHero.GetTag(GameTag.ARMOR);
            var oppTier = oppHero.GetTag(GameTag.PLAYER_TECH_LEVEL);
            rec.Combat.OpponentTavernTier = oppTier > 0 ? oppTier : (int?)null;
            var pid = oppHero.GetTag(GameTag.PLAYER_ID);
            if (pid > 0) rec.Combat.OpponentPlayerId = pid;
        }

        /// <summary>ショップ中に落ちたとき、そこまでの行動回数をそのターンに残す（再接続後に同じショップが続けば FlushShopActions で足す）。</summary>
        private void SaveShopActionsBeforeReconnect()
        {
            var turn = _currentTurn;
            _shopPhase = false;
            if (turn <= 0) return;
            var rec = GetOrAddTurn(turn);
            foreach (var a in _tracker.FlushPending())
                Emit(a.Type, turn, F("card_id", null), F("name", null), F("value", a.Value), F("gold_after", null));
            if (_tracker.Ticks >= 2)
            {
                var partial = _tracker.ToActions(null);
                rec.Actions = _resumedShopActions != null && _resumedShopTurn == turn ? Reconnect.Add(_resumedShopActions, partial) : partial;
            }
            // 何も数えていなくても、同じショップに戻ったら追跡を再開する目印にする
            _resumedShopActions = rec.Actions ?? new TurnActions();
            _resumedShopTurn = turn;
        }

        /// <summary>
        /// 再接続中に終わった戦闘の結果を、戦闘直前と今（次のショップ開始時 / ゲーム終了時）の HP + アーマーから推定する。
        /// 推定できなければ結果は null のまま。どちらの場合も notes に書く。神祇の戦闘後の値と覚醒は取れないので null のまま。
        /// </summary>
        private void ResolveInterruptedCombat(GameV2 game, int combatTurn, int? mineAfter)
        {
            _interruptedCombatTurn = 0;
            _deity.Reset();
            _opponentSigil = null;
            var rec = _log!.Turns.FirstOrDefault(t => t.Turn == combatTurn);
            if (rec?.Combat == null || rec.Combat.Result != null) return;
            var c = rec.Combat;
            int? opponentBefore = c.OpponentHealth.HasValue ? c.OpponentHealth.Value + c.OpponentArmor.GetValueOrDefault() : (int?)null;
            int? opponentAfter = null;
            if (c.OpponentPlayerId.HasValue)
            {
                if (!opponentBefore.HasValue)
                {
                    // 戦闘開始を見る前に落ちた: ショップ開始時のロビーの値で代える
                    var lobby = rec.Lobby?.FirstOrDefault(l => l.PlayerId == c.OpponentPlayerId.Value);
                    if (lobby?.Health != null) opponentBefore = lobby.Health.Value + lobby.Armor.GetValueOrDefault();
                }
                if (_opponents.TryGetValue(c.OpponentPlayerId.Value, out var o) && o.FinalHealth.HasValue)
                    opponentAfter = o.FinalHealth.Value + o.FinalArmor.GetValueOrDefault();
            }
            var outcome = Reconnect.InferCombat(_combatStartMine, mineAfter, opponentBefore, opponentAfter);
            if (outcome != null)
            {
                c.Result = outcome.Result;
                c.DamageDealt = outcome.DamageDealt;
                c.DamageTaken = outcome.DamageTaken;
            }
            var message = $"reconnect: turn {combatTurn} combat ended while disconnected; result {(outcome?.Result ?? "unknown")} inferred from health + armor " +
                $"(mine {Show(_combatStartMine)} -> {Show(mineAfter)}, opponent {Show(opponentBefore)} -> {Show(opponentAfter)})";
            _log.Notes.Add(message);
            PluginLogger.Info(message);
            Emit("combat_end", combatTurn, F("result", c.Result), F("damage_dealt", c.DamageDealt), F("damage_taken", c.DamageTaken), F("inferred", true));
            ReadBobsBuddy(game, combatTurn);
        }

        private static string Show(int? v) => v.HasValue ? v.Value.ToString() : "?";

        private static int? HealthAndArmor(Entity? hero) => hero != null ? hero.Health + hero.GetTag(GameTag.ARMOR) : (int?)null;

        /// <summary>
        /// 進行中のゲームが OnGameEnd を経ずに、別のゲーム（GameHandle が違う / 取れない）が始まった（_gate 内で呼ぶ）。
        /// 0.9.4 までは黙って捨てていた。incomplete で書き出し、ここまでにコピーした power.log も書く。
        /// </summary>
        private void CloseUnfinished(uint newHandle)
        {
            var log = _log!;
            if (AbortedGame.IsBeforeFirstTurn(log))
            {
                DiscardAborted("OnGameStart");
                return;
            }
            Note($"a new game started before this one ended (server game {_gameHandle} -> {newHandle})");
            log.Status = LogStatus.Incomplete;
            log.Opponents = _opponents.Values.OrderBy(o => o.PlayerId).ToList();
            log.Game.LobbySize = _opponents.Count > 0 ? _opponents.Count + 1 : (int?)null;
            _active = false;
            _shopPhase = false;
            var path = _writer.GetPath(log);
            var json = GameLogWriter.Serialize(log);
            var gameId = log.Game.GameId;
            var journal = _journal;
            var events = _events;
            _events = null; // ResetState で閉じさせず、書き出しのあとでフラッシュする
            List<string>? powerLog = null;
            string? powerLogPath = null;
            if (_config.WritePowerLog && _basePath != null && _powerLogBefore.Count + _powerLogCurrent.Count > 0)
            {
                powerLog = _powerLogBefore.Concat(_powerLogCurrent).ToList();
                powerLogPath = PowerLogWriter.PathFor(_basePath, _config.PowerLogGzip);
            }
            PluginLogger.Info("OnGameStart: closing unfinished game as incomplete -> " + path);
            Task.Run(() =>
            {
                try
                {
                    GameLogWriter.WriteAtomic(path, json);
                    PluginLogger.Info("wrote " + path);
                    journal.Delete(gameId);
                    if (powerLog != null && powerLogPath != null)
                        PowerLogWriter.Write(powerLogPath, powerLog);
                    events?.Flush(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    PluginLogger.Error(ex, "CloseUnfinished write");
                }
            });
        }

        /// <summary>
        /// Core.Game.PowerLog の新しい行をコピーする（OnTurnStart から。PowerLog に行を足すのと同じログ読み込みのスレッド）。
        /// 再接続時に HDT が PowerLog を空にするので、それより前の行はこのコピーにしか残らない。
        /// </summary>
        private void CopyPowerLog(GameV2 game)
        {
            if (!_config.WritePowerLog) return;
            try
            {
                var lines = game.PowerLog;
                var n = lines.Count;
                if (n > _powerLogCurrent.Count)
                    _powerLogCurrent.AddRange(lines.GetRange(_powerLogCurrent.Count, n - _powerLogCurrent.Count));
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("CopyPowerLog: " + ex.Message);
            }
        }

        private static uint SafeGameHandle(GameV2 game)
        {
            try
            {
                return game.MetaData.ServerInfo?.GameHandle ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private void ResetState()
        {
            _active = false;
            _awaitingRating = false;
            _endedStats = null;
            _opponents.Clear();
            _trinketFirstSeenTurn.Clear();
            _lastOffered = null;
            _firstOffered = null;
            _choiceArmorByDbfId.Clear();
            _handSourceCache.Clear();
            _tracker.Reset();
            _shopPhase = false;
            _currentTurn = 0;
            _lastCombatPhase = false;
            _combatTurn = 0;
            _opponentBoardRecheckAt = null;
            _damageEvents.Clear();
            _deity.Reset();
            _playerSigil = null;
            _opponentSigil = null;
            _nextOpponentSigilScan = DateTime.MinValue;
            _shopEndMaxEntityId = 0;
            _tickTotalTicks = 0;
            _tickMaxTicks = 0;
            _tickCount = 0;
            _gameHandle = 0;
            _hdtGameIds.Clear();
            _resumedAtTurn = 0;
            _resumePending = false;
            _interruptedCombatTurn = 0;
            _combatStartMine = null;
            _resumedShopActions = null;
            _resumedShopTurn = 0;
            _skipResumedShopSnapshot = false;
            _powerLogBefore.Clear();
            _powerLogCurrent.Clear();
            _events?.Flush(TimeSpan.FromSeconds(1));
            _events = null;
            _basePath = null;
        }

        /// <summary>config.json が更新されていれば読み直し、出力先が変わっていれば Writer を作り直す。</summary>
        private void ApplyConfig()
        {
            _store.ReloadIfChanged();
            _config = _store.Current;
            var root = _config.ResolveOutputDir();
            if (!string.Equals(_writer.RootDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                _writer = new GameLogWriter(root);
                _journal = new PartialLogJournal(root);
            }
        }

        // ------------------------------------------------------------------ events.jsonl

        private static KeyValuePair<string, object?> F(string key, object? value) => new KeyValuePair<string, object?>(key, value);

        private void Emit(string type, int turn, params KeyValuePair<string, object?>[] fields)
        {
            try
            {
                _events?.Append(DateTimeOffset.Now, turn, type, fields);
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("Emit(" + type + ") failed: " + ex.Message);
            }
        }

        /// <summary>notes[] への追記と warning イベント、プラグインログをまとめて行う。</summary>
        private void Note(string message)
        {
            _log?.Notes.Add(message);
            Emit("warning", _currentTurn, F("message", message));
            PluginLogger.Warn(message);
        }

        private void SaveJournal()
        {
            if (_log == null) return;
            string json;
            try
            {
                json = GameLogWriter.Serialize(_log);
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("journal serialize failed: " + ex.Message);
                return;
            }
            var journal = _journal;
            var gameId = _log.Game.GameId;
            Task.Run(() =>
            {
                try
                {
                    journal.Save(gameId, json);
                }
                catch (Exception ex)
                {
                    PluginLogger.Warn("journal save failed: " + ex.Message);
                }
            });
        }

        // ------------------------------------------------------------------ turn handling

        private void OnShopStart(GameV2 game, int turn)
        {
            var rec = GetOrAddTurn(turn);
            var hero = FindPlayerHero(game);

            // 前ターンの戦闘を確定（再接続中に終わった戦闘はロビーを読んでから推定する）
            var interrupted = 0;
            if (_combatTurn > 0 && _combatTurn < turn)
            {
                if (_combatTurn == _interruptedCombatTurn)
                    interrupted = _combatTurn;
                else
                {
                    ResolveCombat(game, _combatTurn, hero, crossCheck: true);
                    ReadBobsBuddy(game, _combatTurn);
                }
            }
            _damageEvents.Clear();
            _opponentBoardRecheckAt = null;

            if (hero != null)
            {
                rec.Health = hero.Health;
                rec.Armor = hero.GetTag(GameTag.ARMOR);
                if (turn == 1)
                {
                    _log!.Player.StartingHealth = hero.Health;
                    _log.Player.StartingArmor = hero.GetTag(GameTag.ARMOR);
                }
            }
            if (turn == 1)
                FinalizeHero(game, hero, turn);
            UpdateLobby(game, turn, final: false);
            if (interrupted > 0)
                ResolveInterruptedCombat(game, interrupted, HealthAndArmor(hero));
            SnapshotLobby(rec);

            _tracker.Reset();
            // ショップ中に落ちて、HDT が同じターンの開始を出し直した: 最初の酒場は落ちる前に記録したものと同じ
            _skipResumedShopSnapshot = _resumedShopActions != null && _resumedShopTurn == turn;
            _shopPhase = true;
            Emit("turn_start", turn, F("tier", hero?.GetTag(GameTag.PLAYER_TECH_LEVEL)), F("health", rec.Health), F("armor", rec.Armor));
            SaveJournal();
        }

        private void OnShopEnd(GameV2 game, int turn)
        {
            var rec = GetOrAddTurn(turn);
            var hero = FindPlayerHero(game);
            var tier = hero?.GetTag(GameTag.PLAYER_TECH_LEVEL) ?? 0;
            if (tier <= 0)
                tier = game.PlayerEntity?.GetTag(GameTag.PLAYER_TECH_LEVEL) ?? 0;
            rec.TavernTier = tier > 0 ? tier : (int?)null;

            var pe = game.PlayerEntity;
            if (pe != null)
                rec.GoldAvailable = pe.HasTag(GameTag.RESOURCES) ? pe.GetTag(GameTag.RESOURCES) : (int?)null;

            FlushShopActions(game, turn);

            // 戦闘に出す盤面と手札
            var ents = EntitySnapshotter.SafeEntities(game);
            rec.BoardEndOfShop = EntitySnapshotter.Board(ents, game.Player.Id);
            rec.HandEndOfShop = _config.SnapshotHand ? EntitySnapshotter.Hand(ents, game.Player.Id) : null;

            // 神祇: 戦闘に持ち込む値。相手の Sigil は戦闘ごとに複製されるので戦闘中に探す
            _shopEndMaxEntityId = ents.Count > 0 ? ents.Max(e => e.Id) : 0;
            _playerSigil = DeityReader.PlayerSigil(ents, game.Player.Id);
            var deityReading = DeityReader.Read(_playerSigil);
            rec.Deity = DeityTracker.AtShopEnd(deityReading, DeityCard(deityReading));
            _deity.Reset();
            _opponentSigil = null;
            _nextOpponentSigilScan = DateTime.MinValue;

            // 戦闘レコードの器（相手ヒーローと盤面は戦闘フェーズ開始時に埋める）
            rec.Combat ??= new CombatRecord();
            var next = hero?.GetTag(GameTag.NEXT_OPPONENT_PLAYER_ID) ?? 0;
            if (next > 0) rec.Combat.OpponentPlayerId = next;
            _combatTurn = turn;
            _combatStartMine = HealthAndArmor(hero);
            if (_interruptedCombatTurn == turn)
                _interruptedCombatTurn = 0; // ショップ中に落ちて同じショップに戻れた。この戦闘は普通に見られる
            _damageEvents.Clear();

            TrackTrinkets(game, turn);
            var fields = new List<KeyValuePair<string, object?>>
            {
                F("tier", rec.TavernTier), F("gold_available", rec.GoldAvailable), F("gold_spent", rec.Actions?.GoldSpent),
                F("board", rec.BoardEndOfShop.Select(m => m.CardId).ToList()), F("next_opponent_player_id", next > 0 ? next : (int?)null),
            };
            if (rec.Deity != null)
            {
                fields.Add(F("deity_attack", rec.Deity.Attack));
                fields.Add(F("deity_health", rec.Deity.Health));
            }
            Emit("shop_end", turn, fields.ToArray());
        }

        private void FlushShopActions(GameV2 game, int turn)
        {
            if (!_shopPhase) return;
            _shopPhase = false;
            var rec = GetOrAddTurn(turn);
            var pe = game.PlayerEntity;
            // HasTag は「値 > 0」なので、使わなかったターン（0）も null にならないよう、所持ゴールドがあるときは GetTag で読む
            int? goldSpent = pe != null && pe.HasTag(GameTag.RESOURCES) ? pe.GetTag(GameTag.RESOURCES_USED) : (int?)null;
            foreach (var a in _tracker.FlushPending())
                Emit(a.Type, turn, F("card_id", null), F("name", null), F("value", a.Value), F("gold_after", null));
            if (_tracker.Ticks >= 2)
            {
                rec.Actions = _tracker.ToActions(goldSpent);
                // ショップ中に落ちて同じショップに戻った: 落ちる前の分を足す
                if (_resumedShopActions != null && _resumedShopTurn == turn)
                    rec.Actions = Reconnect.Add(_resumedShopActions, rec.Actions);
            }
            else
            {
                rec.Actions ??= new TurnActions();
                rec.Actions.GoldSpent = goldSpent;
            }
            if (_resumedShopTurn == turn)
                _resumedShopActions = null;
        }

        private void ObserveShop(GameV2 game)
        {
            var ents = EntitySnapshotter.SafeEntities(game);
            var pid = game.Player.Id;
            var pe = game.PlayerEntity;
            var heroId = pe?.GetTag(GameTag.HERO_ENTITY) ?? 0;
            Entity? hero = null;
            Entity? heroPower = null;
            foreach (var e in ents)
            {
                if (e.Id == heroId) hero = e;
                else if (heroPower == null && e.IsHeroPower && e.IsInPlay && e.IsControlledBy(pid)) heroPower = e;
            }
            // 発見（悪夢の発見 / ヒーローパワー / トリプル報酬）の 3 択。HDT が選択中だけ入れ、選択完了で空にする
            List<int>? offered = null;
            try
            {
                var ids = game.Player.OfferedEntityIds;
                if (ids != null && ids.Count > 0) offered = ids.ToList();
            }
            catch (Exception) { /* 列挙中の変更など。3 択が取れないだけで行動の検出には影響しない */ }

            var tick = EntitySnapshotter.Tick(ents, pid, game.Opponent.Id, pe, hero, heroPower, offered, _handSourceCache);
            var actions = _tracker.Observe(tick);
            if (actions.Count == 0) return;
            foreach (var a in actions)
            {
                if (a.Type == ShopAction.Shop)
                {
                    RecordShopOffer(ents, a);
                    continue;
                }
                var ent = a.EntityId.HasValue ? ents.FirstOrDefault(e => e.Id == a.EntityId.Value) : null;
                if (a.Source == null && a.OfferedIds == null)
                {
                    Emit(a.Type, _currentTurn,
                        F("card_id", ent != null ? HdtCards.LatestCardId(ent) : null), F("name", ent?.LatestCard.Name), F("value", a.Value), F("gold_after", tick.GoldRemaining));
                    continue;
                }
                var offeredCards = a.OfferedIds?
                    .Select(id => ents.FirstOrDefault(e => e.Id == id))
                    .Where(e => e != null)
                    .Select(e => HdtCards.LatestCardId(e!))
                    .Where(c => c != null)
                    .ToList();
                Emit(a.Type, _currentTurn,
                    F("card_id", ent != null ? HdtCards.LatestCardId(ent) : null), F("name", ent?.LatestCard.Name), F("value", a.Value), F("gold_after", tick.GoldRemaining),
                    F("source", a.Source), F("offered", offeredCards));
            }
        }

        /// <summary>ショップに並んだカードを turns[].shop_offers と events.jsonl の shop に書く（schema 1.5.0）。</summary>
        private void RecordShopOffer(List<Entity> ents, ShopAction a)
        {
            try
            {
                if (_log == null || _currentTurn <= 0 || a.ShopIds == null) return;
                if (_skipResumedShopSnapshot && a.Trigger == ShopTrigger.TurnStart)
                {
                    // 再接続後に追跡し直した最初の tick。落ちる前に記録したショップと同じなので turn_start としては書かない
                    _skipResumedShopSnapshot = false;
                    return;
                }
                var cards = EntitySnapshotter.ShopCards(ents, a.ShopIds);
                if (cards.Count == 0) return;
                GetOrAddTurn(_currentTurn).ShopOffers.Add(new ShopOffer { Trigger = a.Trigger ?? "", Cards = cards });
                Emit(ShopAction.Shop, _currentTurn, F("trigger", a.Trigger), F("cards", cards.Select(c => c.CardId).ToList()));
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("RecordShopOffer: " + ex.Message);
            }
        }

        /// <summary>ショップ開始時のロビー（リーダーボードに出ている他プレイヤーのティアと HP）を turns[].lobby に（schema 1.5.0）。</summary>
        private void SnapshotLobby(TurnRecord rec)
        {
            if (_opponents.Count == 0) return;
            rec.Lobby = _opponents.Values
                .OrderBy(o => o.PlayerId)
                .Select(o => new LobbyEntry
                {
                    PlayerId = o.PlayerId,
                    TavernTier = o.FinalTavernTier,
                    Health = o.FinalHealth,
                    Armor = o.FinalArmor,
                    LeaderboardPlace = o.EstimatedPlacement,
                    Eliminated = o.EliminatedTurn.HasValue,
                })
                .ToList();
        }

        private TurnRecord GetOrAddTurn(int turn)
        {
            var rec = _log!.Turns.FirstOrDefault(t => t.Turn == turn);
            if (rec == null)
            {
                rec = new TurnRecord { Turn = turn };
                _log.Turns.Add(rec);
                _log.Turns.Sort((a, b) => a.Turn.CompareTo(b.Turn));
            }
            return rec;
        }

        // ------------------------------------------------------------------ combat

        private void TrackCombatPhase(GameV2 game)
        {
            var combat = game.IsBattlegroundsCombatPhase;
            if (combat && !_lastCombatPhase)
            {
                var turn = game.GetTurnNumber();
                if (turn > 0)
                {
                    CaptureCombat(game, turn);
                    _opponentBoardRecheckAt = DateTime.Now.AddMilliseconds(700);
                }
            }
            _lastCombatPhase = combat;

            if (combat && _opponentBoardRecheckAt.HasValue && DateTime.Now >= _opponentBoardRecheckAt.Value)
            {
                _opponentBoardRecheckAt = null;
                RecheckOpponentBoard(game);
            }
            if (combat)
                ObserveDeity(game);
        }

        /// <summary>
        /// 戦闘中の毎 tick: 自分と相手の Sigil を読む。HDT は PowerTaskList（演出の進行）に合わせて Entity を更新するので、
        /// 戦闘中の成長を追える。相手の Sigil のコピーは戦闘準備中に作られ、後片付けで 0 に戻される（DeityTracker が 0 を無視する）。
        /// </summary>
        private void ObserveDeity(GameV2 game)
        {
            // 神祇の無いシーズンでは自分の Sigil も無い。そのときは相手の Sigil を探して Entities を走査し続けない
            if (_playerSigil == null) return;
            _deity.ObservePlayer(DeityReader.Read(_playerSigil));
            if (_deity.OpponentFirst == null && DateTime.Now >= _nextOpponentSigilScan
                && (_opponentSigil == null || DeityReader.Read(_opponentSigil)?.IsEmpty != false))
            {
                // 前の戦闘のコピーは REMOVEDFROMGAME にあり値も 0 なので、今回のコピーが現れるまで 300ms ごとに探し直す
                _nextOpponentSigilScan = DateTime.Now.AddMilliseconds(300);
                _opponentSigil = DeityReader.OpponentSigil(EntitySnapshotter.SafeEntities(game), game.Opponent.Id, 0);
            }
            if (_opponentSigil != null)
                _deity.ObserveOpponent(DeityReader.Read(_opponentSigil));
        }

        /// <summary>戦闘の確定時: 戦闘後の値と覚醒の有無を turns[].deity / combat.opponent_deity に書く。</summary>
        private void CompleteDeity(GameV2 game, TurnRecord rec, List<KeyValuePair<string, object?>> fields)
        {
            try
            {
                var ents = EntitySnapshotter.SafeEntities(game);
                var (spawned, sigilIds) = DeityReader.CombatSpawns(ents, _shopEndMaxEntityId);
                var awakened = DeityTracker.AwakenedControllers(spawned, sigilIds);
                var golden = DeityTracker.GoldenControllers(spawned, sigilIds);
                var playerId = game.Player.Id;
                if (rec.Deity != null)
                {
                    _deity.CompletePlayer(rec.Deity, DeityReader.Read(_playerSigil ?? DeityReader.PlayerSigil(ents, playerId)),
                        awakened.Contains(playerId), awakenedGolden: golden.Contains(playerId));
                    fields.Add(F("deity_attack_after", rec.Deity.AttackAfterCombat));
                    fields.Add(F("deity_health_after", rec.Deity.HealthAfterCombat));
                    fields.Add(F("deity_awakened", rec.Deity.Awakened));
                    fields.Add(F("deity_golden", rec.Deity.Golden));
                }
                var opp = rec.Combat!.OpponentDeity = _deity.OpponentRecord(DeityCard(_deity.OpponentFirst),
                    awakened.Any(c => c != playerId), awakenedGolden: golden.Any(c => c != playerId));
                if (opp != null)
                {
                    fields.Add(F("opponent_deity_attack", opp.Attack));
                    fields.Add(F("opponent_deity_health", opp.Health));
                    fields.Add(F("opponent_deity_awakened", opp.Awakened));
                    fields.Add(F("opponent_deity_golden", opp.Golden));
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("CompleteDeity: " + ex.Message);
            }
            finally
            {
                _deity.Reset();
                _opponentSigil = null;
            }
        }

        /// <summary>Sigil の BACON_EVOLUTION_CARD_ID から神祇のカード。無ければロビー共通の神祇（BACON_GLOBAL_OLD_GOD_DBID）</summary>
        private static CardRef DeityCard(DeityReading? reading)
        {
            var dbf = reading?.DbfId ?? 0;
            if (dbf <= 0)
                dbf = Core.Game.GameEntity?.GetTag(GameTag.BACON_GLOBAL_OLD_GOD_DBID) ?? 0;
            return HdtCards.FromDbfId(dbf);
        }

        private void CaptureCombat(GameV2 game, int turn)
        {
            var rec = GetOrAddTurn(turn);
            rec.Combat ??= new CombatRecord();
            _combatTurn = turn;
            _combatStartMine = HealthAndArmor(FindPlayerHero(game)) ?? _combatStartMine;
            var ents = EntitySnapshotter.SafeEntities(game);
            var oppHero = EntitySnapshotter.OpponentHero(ents, game.Opponent.Id);
            if (oppHero != null)
            {
                HdtCards.Fill(rec.Combat.OpponentHero, HdtCards.FromCardId(oppHero.CardId));
                rec.Combat.OpponentHealth = oppHero.Health;
                rec.Combat.OpponentArmor = oppHero.GetTag(GameTag.ARMOR);
                var oppTier = oppHero.GetTag(GameTag.PLAYER_TECH_LEVEL);
                rec.Combat.OpponentTavernTier = oppTier > 0 ? oppTier : (int?)null;
                var pid = oppHero.GetTag(GameTag.PLAYER_ID);
                if (pid > 0)
                {
                    if (rec.Combat.OpponentPlayerId.HasValue && rec.Combat.OpponentPlayerId.Value != pid)
                        Note($"turn {turn}: NEXT_OPPONENT_PLAYER_ID={rec.Combat.OpponentPlayerId} but combat opponent PLAYER_ID={pid}");
                    rec.Combat.OpponentPlayerId = pid;
                }
            }
            else
            {
                Note($"turn {turn}: opponent hero not found at combat start");
            }
            rec.Combat.OpponentBoard = EntitySnapshotter.Board(ents, game.Opponent.Id);
            if (rec.BoardEndOfShop.Count == 0)
                rec.BoardEndOfShop = EntitySnapshotter.Board(ents, game.Player.Id);
            Emit("combat_start", turn, F("opponent_player_id", rec.Combat.OpponentPlayerId), F("opponent_hero_card_id", rec.Combat.OpponentHero.CardId),
                F("opponent_health", rec.Combat.OpponentHealth), F("opponent_armor", rec.Combat.OpponentArmor), F("opponent_tavern_tier", rec.Combat.OpponentTavernTier),
                F("opponent_board", rec.Combat.OpponentBoard.Select(m => m.CardId).ToList()));
        }

        private void RecheckOpponentBoard(GameV2 game)
        {
            if (_combatTurn <= 0) return;
            var rec = _log!.Turns.FirstOrDefault(t => t.Turn == _combatTurn);
            if (rec?.Combat == null) return;
            var ents = EntitySnapshotter.SafeEntities(game);
            var board = EntitySnapshotter.Board(ents, game.Opponent.Id);
            if (board.Count > rec.Combat.OpponentBoard.Count)
            {
                PluginLogger.Info($"turn {_combatTurn}: opponent board grew {rec.Combat.OpponentBoard.Count} -> {board.Count} on recheck");
                rec.Combat.OpponentBoard = board;
            }
        }

        private void ResolveCombat(GameV2 game, int combatTurn, Entity? hero, bool crossCheck)
        {
            if (combatTurn <= 0) return;
            var rec = _log!.Turns.FirstOrDefault(t => t.Turn == combatTurn);
            if (rec?.Combat == null) return;
            if (rec.Combat.Result != null) return; // 確定済み
            var outcome = CombatResolver.Resolve(_damageEvents);
            rec.Combat.Result = outcome.Result;
            rec.Combat.DamageDealt = outcome.DamageDealt;
            rec.Combat.DamageTaken = outcome.DamageTaken;
            var fields = new List<KeyValuePair<string, object?>>
            {
                F("result", outcome.Result), F("damage_dealt", outcome.DamageDealt), F("damage_taken", outcome.DamageTaken),
            };
            CompleteDeity(game, rec, fields);
            Emit("combat_end", combatTurn, fields.ToArray());

            if (crossCheck && hero != null && hero.HasTag(GameTag.BACON_WON_LAST_COMBAT) && outcome.Result != "tie")
            {
                var won = hero.GetTag(GameTag.BACON_WON_LAST_COMBAT) == 1;
                if (won != (outcome.Result == "win"))
                    Note($"turn {combatTurn}: combat result '{outcome.Result}' disagrees with BACON_WON_LAST_COMBAT={hero.GetTag(GameTag.BACON_WON_LAST_COMBAT)}");
            }
        }

        private void ReadBobsBuddy(GameV2 game, int turn)
        {
            var rec = _log!.Turns.FirstOrDefault(t => t.Turn == turn);
            if (rec?.Combat == null || rec.Combat.BobsBuddy != null) return;
            var gameId = game.CurrentGameStats?.GameId;
            if (gameId == null) return;
            rec.Combat.BobsBuddy = BobsBuddyReader.TryRead(gameId.Value, turn);
            // 再接続前のターンの結果は、そのときの HDT のゲーム ID に付いている
            for (var i = _hdtGameIds.Count - 1; i >= 0 && rec.Combat.BobsBuddy == null; i--)
            {
                if (_hdtGameIds[i] != gameId.Value)
                    rec.Combat.BobsBuddy = BobsBuddyReader.TryRead(_hdtGameIds[i], turn);
            }
        }

        private void ReportTickStats()
        {
            if (_tickCount == 0) return;
            var avgMs = _tickTotalTicks * 1000.0 / Stopwatch.Frequency / _tickCount;
            var maxMs = _tickMaxTicks * 1000.0 / Stopwatch.Frequency;
            PluginLogger.Info($"OnUpdate stats: count={_tickCount} avg={avgMs:F3}ms max={maxMs:F2}ms");
            if (avgMs >= 1.0)
                Note($"OnUpdate average {avgMs:F2}ms exceeds 1ms budget");
        }

        // ------------------------------------------------------------------ hero / lobby

        private static Entity? FindPlayerHero(GameV2 game)
        {
            var pe = game.PlayerEntity;
            var heroId = pe?.GetTag(GameTag.HERO_ENTITY) ?? 0;
            if (heroId > 0 && game.Entities.TryGetValue(heroId, out var hero))
                return hero;
            // フォールバック: 自分制御の PLAY 中ヒーロー（ゲーム終了時は複製に注意）
            return game.Entities.Values.FirstOrDefault(x => x.IsHero && x.IsInPlay && x.IsControlledBy(game.Player.Id));
        }

        private void FinalizeHero(GameV2 game, Entity? hero, int turn)
        {
            var log = _log!;
            if (hero != null && hero.CardId != null && log.Player.Hero.IsEmpty)
            {
                HdtCards.Fill(log.Player.Hero, HdtCards.FromCardId(hero.CardId));
                Emit("hero_selected", turn, F("card_id", log.Player.Hero.CardId), F("name", log.Player.Hero.Name), F("dbf_id", log.Player.Hero.DbfId));
            }
            if (log.Player.HeroPower.IsEmpty)
            {
                var hp = game.Player.Board.FirstOrDefault(x => x.IsHeroPower);
                if (hp?.CardId != null)
                    HdtCards.Fill(log.Player.HeroPower, HdtCards.FromCardId(hp.CardId));
            }
            // 選択肢の最終確定。再接続後の HDT はヒーロー選択を見ていないので、再接続前に確定した値を残す
            var offered = _resumedAtTurn > 0 && log.Player.HeroChoices.Count > 0 ? null : SafeOffered(game);
            if (offered != null && offered.Length > 0)
                log.Player.HeroChoices = offered.Select(id => ToHeroChoice(game, id)).ToList();
            if (_firstOffered != null && offered != null && !_firstOffered.Select(c => c.DbfId).SequenceEqual(offered.Cast<int?>()))
                log.Player.HeroChoicesBeforeReroll = _firstOffered;
            TrackTrinkets(game, turn);
        }

        private void TrackHeroPick(GameV2 game)
        {
            if (game.GetTurnNumber() != 0) return;
            if ((DateTime.Now - _lastHeroPickCheck).TotalSeconds < 2) return;
            _lastHeroPickCheck = DateTime.Now;
            var offered = SafeOffered(game);
            if (offered == null || offered.Length == 0) return;
            RememberChoiceArmor(game, offered);
            if (_lastOffered == null)
            {
                _lastOffered = offered;
                _firstOffered = offered.Select(id => ToHeroChoice(game, id)).ToList();
                Emit("hero_choice", 0, F("card_ids", _firstOffered.Select(c => c.CardId).ToList()), F("dbf_ids", offered));
            }
            else if (!_lastOffered.SequenceEqual(offered))
            {
                _lastOffered = offered;
                var choices = offered.Select(id => ToHeroChoice(game, id)).ToList();
                Emit("hero_reroll", 0, F("card_ids", choices.Select(c => c.CardId).ToList()), F("dbf_ids", offered));
                PluginLogger.Info("hero choices changed (reroll): " + string.Join(",", offered));
            }
        }

        private void RememberChoiceArmor(GameV2 game, int[] offered)
        {
            try
            {
                var inHand = game.Entities.Values
                    .Where(x => x.IsHero && x.IsInHand && x.IsControlledBy(game.Player.Id))
                    .ToList();
                foreach (var dbf in offered)
                {
                    var ent = inHand.FirstOrDefault(x => x.Card.DbfId == dbf);
                    if (ent != null)
                        _choiceArmorByDbfId[dbf] = ent.GetTag(GameTag.ARMOR);
                }
            }
            catch
            {
                // Entities の列挙中の変更は無視（次回の呼び出しで再試行）
            }
        }

        private static int[]? SafeOffered(GameV2 game)
        {
            try
            {
                return game.BattlegroundsHeroPickState.OfferedHeroDbfIds;
            }
            catch
            {
                return null;
            }
        }

        private HeroChoice ToHeroChoice(GameV2 game, int dbfId)
        {
            var c = HdtCards.FromDbfId(dbfId);
            var choice = new HeroChoice { CardId = c.CardId, Name = c.Name, DbfId = c.DbfId };
            if (!_choiceArmorByDbfId.ContainsKey(dbfId))
                RememberChoiceArmor(game, new[] { dbfId });
            if (_choiceArmorByDbfId.TryGetValue(dbfId, out var armor))
                choice.StartingArmor = armor;
            return choice;
        }

        private void TrackTrinkets(GameV2 game, int turn)
        {
            foreach (var t in game.Player.Trinkets.ToList())
            {
                if (t.CardId == null) continue;
                if (_trinketFirstSeenTurn.ContainsKey(t.Id)) continue;
                _trinketFirstSeenTurn[t.Id] = turn;
                var c = HdtCards.FromCardId(t.CardId);
                // 再接続後に同じトリンケットを別の Entity として見直したときは、最初に取ったターンを残す
                if (_resumedAtTurn > 0 && _log!.Player.Trinkets.Any(x => x.CardId == c.CardId)) continue;
                _log!.Player.Trinkets.Add(new TrinketRecord { CardId = c.CardId, Name = c.Name, DbfId = c.DbfId, Turn = turn });
                Emit("trinket", turn, F("card_id", c.CardId), F("name", c.Name), F("dbf_id", c.DbfId));
            }
        }

        private void UpdateLobby(GameV2 game, int turn, bool final)
        {
            var myId = game.Player.Id;
            List<Entity> heroes;
            try
            {
                heroes = game.Entities.Values
                    .Where(x => x.IsHero && x.HasTag(GameTag.PLAYER_ID) && x.GetTag(GameTag.PLAYER_ID) != myId && x.IsInSetAside)
                    .ToList();
            }
            catch (InvalidOperationException)
            {
                return;
            }
            foreach (var h in heroes)
            {
                var pid = h.GetTag(GameTag.PLAYER_ID);
                if (!_opponents.TryGetValue(pid, out var rec))
                {
                    rec = new OpponentRecord { PlayerId = pid };
                    _opponents[pid] = rec;
                }
                if (rec.Hero.IsEmpty && h.CardId != null)
                    HdtCards.Fill(rec.Hero, HdtCards.FromCardId(h.CardId));
                rec.FinalHealth = h.Health;
                rec.FinalArmor = h.GetTag(GameTag.ARMOR);
                var tier = h.GetTag(GameTag.PLAYER_TECH_LEVEL);
                if (tier > 0) rec.FinalTavernTier = tier;
                var place = h.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                if (place > 0) rec.EstimatedPlacement = place;
                if (h.Health <= 0 && rec.EliminatedTurn == null)
                {
                    rec.EliminatedTurn = final ? turn : Math.Max(1, turn - 1);
                    Emit("player_eliminated", turn, F("player_id", pid), F("hero_card_id", rec.Hero.CardId), F("eliminated_turn", rec.EliminatedTurn), F("place", place > 0 ? place : (int?)null));
                }
                foreach (var dbf in new[] { h.GetTag(GameTag.BACON_FIRST_TRINKET_DATABASE_ID), h.GetTag(GameTag.BACON_SECOND_TRINKET_DATABASE_ID) })
                {
                    if (dbf > 0 && rec.Trinkets.All(t => t.DbfId != dbf))
                        rec.Trinkets.Add(HdtCards.FromDbfId(dbf));
                }
                if (final)
                    rec.Placement = rec.EliminatedTurn.HasValue && place > 0 ? place : (int?)null;
            }
        }

        private void FillGameInfo(GameV2 game)
        {
            var log = _log!;
            var ge = game.GameEntity;
            try
            {
                var anomaly = BattlegroundsUtils.GetBattlegroundsAnomalyDbfId(ge);
                if (anomaly.HasValue && anomaly.Value > 0)
                    HdtCards.Fill(log.Game.Anomaly, HdtCards.FromDbfId(anomaly.Value));
                var oldGod = ge?.GetTag(GameTag.BACON_GLOBAL_OLD_GOD_DBID) ?? 0;
                if (oldGod > 0)
                    HdtCards.Fill(log.Game.GlobalOldGod, HdtCards.FromDbfId(oldGod));
                if (ge != null && ge.GetTag(GameTag.BACON_COMBAT_DAMAGE_CAP_ENABLED) > 0)
                    log.Game.CombatDamageCap = ge.GetTag(GameTag.BACON_COMBAT_DAMAGE_CAP);
                var races = BattlegroundsUtils.GetAvailableRaces();
                if (races != null && races.Count > 0)
                    log.Game.MinionTypes = races.Select(HdtCards.RaceName).OrderBy(x => x).ToList();
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("FillGameInfo: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ MMR after

        private void PollRatingAfter()
        {
            var log = _log;
            if (log == null) { _awaitingRating = false; return; }
            var elapsed = (DateTime.Now - _endedAt).TotalSeconds;
            var after = _endedStats?.BattlegroundsRatingAfter ?? 0;
            if (after <= 0 && elapsed < _config.MmrAfterWaitSec)
                return;

            _awaitingRating = false;
            if (after <= 0)
            {
                after = ReadRatingAfterFromBgsLastGames();
                if (after > 0)
                    log.Notes.Add("mmr_after taken from BgsLastGames.xml");
            }
            if (after > 0)
            {
                log.Result.MmrAfter = after;
                if (log.Result.MmrBefore.HasValue)
                    log.Result.MmrDelta = after - log.Result.MmrBefore.Value;
                PluginLogger.Info($"mmr_after={after} (waited {elapsed:F1}s)");
                Emit("mmr_after", log.Result.TotalTurns ?? _currentTurn, F("mmr_after", after), F("mmr_delta", log.Result.MmrDelta));
            }
            else
            {
                Note("mmr_after unavailable");
            }
            WriteAsync(log, _writer.GetPath(log));
            var events = _events;
            Task.Run(() => events?.Flush(TimeSpan.FromSeconds(5)));
        }

        private int ReadRatingAfterFromBgsLastGames()
        {
            try
            {
                var stats = _endedStats;
                if (stats == null) return 0;
                var key = stats.StartTime.ToString("o");
                var item = BattlegroundsLastGames.Instance.Games.FirstOrDefault(g => g.StartTime == key);
                return item?.RatingAfter ?? 0;
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("BgsLastGames fallback failed: " + ex.Message);
                return 0;
            }
        }

        // ------------------------------------------------------------------ output

        private void WriteAsync(GameLog log, string path)
        {
            string json;
            lock (_gate)
                json = GameLogWriter.Serialize(log);
            Task.Run(() =>
            {
                try
                {
                    GameLogWriter.WriteAtomic(path, json);
                    PluginLogger.Info("wrote " + path);
                }
                catch (Exception ex)
                {
                    PluginLogger.Error(ex, "WriteAsync(" + path + ")");
                }
            });
        }

        private static string? SafeHdtVersion()
        {
            try
            {
                return Hearthstone_Deck_Tracker.Helper.GetCurrentVersion().ToString(3);
            }
            catch
            {
                return null;
            }
        }
    }
}
