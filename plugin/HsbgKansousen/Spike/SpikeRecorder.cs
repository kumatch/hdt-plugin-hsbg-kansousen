using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using Hearthstone_Deck_Tracker.Stats;
using HsbgKansousen.Diagnostics;
using Newtonsoft.Json;

namespace HsbgKansousen.Spike
{
    /// <summary>
    /// Phase 1 用の調査ダンプ。BG ゲームの要所 (開始 / ヒーロー選択中 / 各ターン開始 / 戦闘開始・終了 / 終了 / 終了後の MMR 更新) で
    /// Core.Game の状態と全 Entity のタグを JSON に書く。
    /// 出力先: <output_dir>\.spike\<開始時刻>_<gameId>\NN_<event>.json（output_dir はゲーム開始時の config の値）
    /// 本番のロガーではない (Phase 2 で Capture/ に置き換える)。
    /// </summary>
    public sealed class SpikeRecorder
    {
        private readonly Func<string> _outputRoot;

        /// <param name="outputRoot">ログの出力先（config の output_dir を展開したもの）を返す</param>
        public SpikeRecorder(Func<string> outputRoot)
        {
            _outputRoot = outputRoot;
        }

        public string SpikeRoot => Path.Combine(_outputRoot(), ".spike");

        private readonly object _gate = new object();
        private string? _gameDir;
        private int _seq;
        private bool _isBg;
        private bool _lastCombatPhase;
        private int _lastTurn = -1;
        private DateTime _lastHeroPickDump = DateTime.MinValue;
        private int _heroPickDumps;
        private readonly List<object> _damageEvents = new List<object>();

        // ゲーム終了後の MMR 後値ポーリング
        private GameStats? _endedStats;
        private DateTime _endedAt;
        private bool _ratingAfterWritten;

        /// <summary>
        /// false の間はすべてのハンドラが何もしない。
        /// HDT の ActionList には Remove が無く、Add 時に「呼び出し元クラス = IPlugin 実装」を StackTrace で記録して
        /// プラグイン無効化時に HDT 側が外す。そのため GameEvents.*.Add は HsbgKansousenPlugin のメソッド内から直接呼ぶ必要があり、
        /// このクラスは登録を行わない (HsbgKansousenPlugin.OnLoad を参照)。
        /// </summary>
        public bool Enabled { get; set; } = true;

        // ---------------------------------------------------------------- events

        public void OnGameStart()
        {
            if (!Enabled) return;
            try
            {
                var game = Core.Game;
                _isBg = game.IsBattlegroundsMatch;
                _lastCombatPhase = false;
                _lastTurn = -1;
                _heroPickDumps = 0;
                _lastHeroPickDump = DateTime.MinValue;
                _endedStats = null;
                _ratingAfterWritten = false;
                lock (_gate) _damageEvents.Clear();

                PluginLogger.Info($"OnGameStart: IsBattlegroundsMatch={_isBg} GameType={game.CurrentGameType} GameMode={game.CurrentGameMode}");
                if (!_isBg)
                {
                    _gameDir = null;
                    return;
                }
                var gameId = game.CurrentGameStats?.GameId.ToString("N").Substring(0, 8) ?? "nogameid";
                _gameDir = Path.Combine(SpikeRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}_{gameId}");
                Directory.CreateDirectory(_gameDir);
                _seq = 0;
                Dump("game_start");
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "OnGameStart");
            }
        }

        public void OnTurnStart(ActivePlayer player)
        {
            if (!Enabled) return;
            try
            {
                if (!_isBg || _gameDir == null) return;
                var turn = Core.Game.GetTurnNumber();
                _lastTurn = turn;
                Dump($"turn{turn:D2}_{(player == ActivePlayer.Player ? "shop" : "combat")}_start");
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "OnTurnStart");
            }
        }

        public void OnGameEnd()
        {
            if (!Enabled) return;
            try
            {
                if (!_isBg || _gameDir == null) return;
                _endedStats = Core.Game.CurrentGameStats;
                _endedAt = DateTime.Now;
                _ratingAfterWritten = false;
                Dump("game_end", includePowerLogTail: true);
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "OnGameEnd");
            }
        }

        public void OnInMenu()
        {
            if (!Enabled) return;
            try
            {
                if (!_isBg || _gameDir == null) return;
                Dump("in_menu", entities: false);
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "OnInMenu");
            }
        }

        public void OnEntityWillTakeDamage(PredamageInfo info)
        {
            if (!Enabled) return;
            try
            {
                if (!_isBg || _gameDir == null) return;
                var e = info.Entity;
                lock (_gate)
                {
                    _damageEvents.Add(new
                    {
                        t = DateTime.Now.ToString("o"),
                        turn = Core.Game.GetTurnNumber(),
                        combat_phase = Core.Game.IsBattlegroundsCombatPhase,
                        entity_id = e.Id,
                        card_id = e.CardId,
                        is_hero = e.IsHero,
                        controller = e.GetTag(GameTag.CONTROLLER),
                        is_player = e.IsControlledBy(Core.Game.Player.Id),
                        value = info.Value,
                        health_before = e.Health,
                        armor_before = e.GetTag(GameTag.ARMOR),
                    });
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "OnEntityWillTakeDamage");
            }
        }

        /// <summary>OnUpdate (約 100ms) から呼ばれる。軽量な差分検出のみ。</summary>
        public void Update()
        {
            if (!Enabled || !_isBg || _gameDir == null) return;
            var game = Core.Game;

            // 1) 戦闘フェーズの遷移
            var combat = game.IsBattlegroundsCombatPhase;
            if (combat != _lastCombatPhase)
            {
                _lastCombatPhase = combat;
                Dump(combat ? $"turn{game.GetTurnNumber():D2}_combat_phase_on" : $"turn{game.GetTurnNumber():D2}_combat_phase_off");
            }

            // 2) ヒーロー選択中 (ターン 0) は選択肢が遅れて現れるので数秒おきに数回ダンプ
            if (_endedStats == null && game.GetTurnNumber() == 0 && _heroPickDumps < 6
                && (DateTime.Now - _lastHeroPickDump).TotalSeconds >= 4)
            {
                _lastHeroPickDump = DateTime.Now;
                _heroPickDumps++;
                Dump($"hero_pick_{_heroPickDumps}");
            }

            // 3) ゲーム終了後: BattlegroundsRatingAfter が入るのを最大 40 秒待つ
            if (_endedStats != null && !_ratingAfterWritten)
            {
                var elapsed = (DateTime.Now - _endedAt).TotalSeconds;
                if (_endedStats.BattlegroundsRatingAfter != 0 || elapsed > 40)
                {
                    _ratingAfterWritten = true;
                    var stats = _endedStats;
                    WriteJson("gamestats_after_wait", new
                    {
                        waited_sec = Math.Round(elapsed, 1),
                        rating_after_available = stats.BattlegroundsRatingAfter != 0,
                        game_stats = GameStatsToDto(stats),
                    });
                }
            }
        }

        // ---------------------------------------------------------------- dump

        private void Dump(string eventName, bool entities = true, bool includePowerLogTail = false)
        {
            try
            {
                var payload = Capture(eventName, entities, includePowerLogTail);
                WriteJson(eventName, payload);
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "Dump(" + eventName + ")");
            }
        }

        private void WriteJson(string eventName, object payload)
        {
            var dir = _gameDir;
            if (dir == null) return;
            var seq = System.Threading.Interlocked.Increment(ref _seq);
            var path = Path.Combine(dir, $"{seq:D2}_{eventName}.json");
            // シリアライズとファイル I/O はゲームスレッドから逃がす (payload は既に値型/文字列のみ)
            Task.Run(() =>
            {
                try
                {
                    var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
                    var tmp = path + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                    PluginLogger.Info("wrote " + path);
                }
                catch (Exception ex)
                {
                    PluginLogger.Error(ex, "WriteJson(" + eventName + ")");
                }
            });
        }

        private object Capture(string eventName, bool includeEntities, bool includePowerLogTail)
        {
            var game = Core.Game;
            var stats = game.CurrentGameStats;
            var turn = game.GetTurnNumber();

            Entity? playerHero = null;
            Entity? opponentHero = null;
            List<Entity> all;
            try
            {
                all = game.Entities.Values.ToList();
            }
            catch (InvalidOperationException)
            {
                // 列挙中に変更された場合は一度だけやり直す
                all = game.Entities.Values.ToList();
            }
            try
            {
                playerHero = all.FirstOrDefault(x => x.IsHero && x.IsInPlay && x.IsControlledBy(game.Player.Id));
                opponentHero = all.FirstOrDefault(x => x.IsHero && x.IsInPlay && x.IsControlledBy(game.Opponent.Id));
            }
            catch (Exception ex)
            {
                PluginLogger.Warn("hero lookup failed: " + ex.Message);
            }

            object? damageEvents;
            lock (_gate) damageEvents = _damageEvents.ToList();

            return new
            {
                schema = "hsbg-spike-dump/1",
                @event = eventName,
                timestamp = DateTime.Now.ToString("o"),
                turn,
                game = new
                {
                    is_battlegrounds_match = game.IsBattlegroundsMatch,
                    is_solo = game.IsBattlegroundsSoloMatch,
                    is_duos = game.IsBattlegroundsDuosMatch,
                    is_combat_phase = game.IsBattlegroundsCombatPhase,
                    current_game_type = game.CurrentGameType.ToString(),
                    current_game_mode = game.CurrentGameMode.ToString(),
                    current_mode = game.CurrentMode.ToString(),
                    is_in_menu = game.IsInMenu,
                    is_running = game.IsRunning,
                    spectator = Safe(() => (object)game.Spectator),
                    current_battlegrounds_rating = game.CurrentBattlegroundsRating,
                    game_entity_turn = game.GameEntity?.GetTag(GameTag.TURN),
                    game_entity_id = game.GameEntity?.Id,
                    player_id = game.Player.Id,
                    opponent_id = game.Opponent.Id,
                    player_entity_id = game.PlayerEntity?.Id,
                    opponent_entity_id = game.OpponentEntity?.Id,
                    player_hero_entity_id = playerHero?.Id,
                    player_hero_card_id = playerHero?.CardId,
                    opponent_hero_entity_id = opponentHero?.Id,
                    opponent_hero_card_id = opponentHero?.CardId,
                    power_log_lines = Safe(() => (object)game.PowerLog.Count),
                    entity_count = all.Count,
                },
                game_stats = stats == null ? null : GameStatsToDto(stats),
                hero_pick = Safe(() => (object)new
                {
                    offered_hero_dbf_ids = game.BattlegroundsHeroPickState.OfferedHeroDbfIds,
                    picked_hero_dbf_id = game.BattlegroundsHeroPickState.PickedHeroDbfId,
                }),
                anomaly_dbf_id = Safe(() => (object?)BattlegroundsUtils.GetBattlegroundsAnomalyDbfId(game.GameEntity)),
                deity_dbf_id = Safe(() => (object?)BattlegroundsUtils.GetBattlegroundsDeityDbfId(game.GameEntity)),
                available_races = Safe(() => (object?)BattlegroundsUtils.GetAvailableRaces()?.Select(r => r.ToString()).ToList()),
                player_hero = playerHero == null ? null : new
                {
                    entity_id = playerHero.Id,
                    card_id = playerHero.CardId,
                    original_card_id = Safe(() => (object?)BattlegroundsUtils.GetOriginalHeroId(playerHero.CardId ?? "")),
                    tech_level = playerHero.GetTag(GameTag.PLAYER_TECH_LEVEL),
                    leaderboard_place = playerHero.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE),
                    health = playerHero.Health,
                    armor = playerHero.GetTag(GameTag.ARMOR),
                    next_opponent_player_id = playerHero.GetTag(GameTag.NEXT_OPPONENT_PLAYER_ID),
                    tavern_up_turns = Safe(() => (object?)game.GetBattlegroundsHeroLatestTavernUpTurn(playerHero.Id)),
                    triples_by_tier = Safe(() => (object?)game.GetBattlegroundsHeroTriplesByTier(playerHero.Id)),
                    trinkets = Safe(() => (object?)game.Player.Trinkets.Select(x => x.CardId).ToList()),
                    quest_rewards = Safe(() => (object?)game.Player.QuestRewards.Select(x => x.CardId).ToList()),
                },
                opponent_board_snapshot = opponentHero == null ? null : Safe(() =>
                {
                    var snap = game.GetBattlegroundsBoardStateFor(opponentHero.Id);
                    return snap == null ? null : (object)new
                    {
                        snap.Turn,
                        entities = snap.Entities.Select(EntityToDto).ToList(),
                    };
                }),
                bobs_buddy = Safe(() => TryReadBobsBuddy(stats, turn)),
                damage_events = damageEvents,
                entities = includeEntities ? all.OrderBy(x => x.Id).Select(EntityToDto).ToList() : null,
                power_log_tail = includePowerLogTail ? Safe(() => (object?)game.PowerLog.Skip(Math.Max(0, game.PowerLog.Count - 200)).ToList()) : null,
            };
        }

        private static object GameStatsToDto(GameStats s) => new
        {
            game_id = s.GameId,
            start_time = s.StartTime,
            end_time = s.EndTime,
            turns = s.Turns,
            result = s.Result.ToString(),
            game_mode = s.GameMode.ToString(),
            game_type = s.GameType.ToString(),
            battlegrounds_rating = s.BattlegroundsRating,
            battlegrounds_rating_after = s.BattlegroundsRatingAfter,
            hearthstone_build = s.HearthstoneBuild,
            scenario_id = s.ScenarioId,
            region = s.Region.ToString(),
            friendly_player_id = s.FriendlyPlayerId,
            opponent_player_id = s.OpponentPlayerId,
            player_hero_card_id = s.PlayerHeroCardId,
            was_conceded = s.WasConceded,
            battlegrounds_races = s.BattlegroundsRaces?.Select(r => r.ToString()).ToList(),
            battlegrounds_details = s.BattlegroundsDetails == null ? null : new
            {
                s.BattlegroundsDetails.LobbyRawHeroDbfIds,
                s.BattlegroundsDetails.FriendlyPlayerEntityId,
                s.BattlegroundsDetails.FriendlyRawHeroDbfId,
                s.BattlegroundsDetails.FinalPlacement,
                s.BattlegroundsDetails.AnomalyDbfId,
                s.BattlegroundsDetails.DeityDbfId,
            },
        };

        private static object EntityToDto(Entity e)
        {
            var tags = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in e.Tags.ToList())
            {
                var name = Enum.IsDefined(typeof(GameTag), kv.Key) ? kv.Key.ToString() : "TAG_" + (int)kv.Key;
                tags[name] = kv.Value;
            }
            var isPlayerEntity = e.HasTag(GameTag.PLAYER_ID) && !e.IsHero && string.IsNullOrEmpty(e.CardId);
            return new
            {
                id = e.Id,
                card_id = e.CardId,
                // プレイヤー Entity の Name は BattleTag なのでダンプに含めない
                name = isPlayerEntity ? null : e.Name,
                zone = Enum.IsDefined(typeof(Zone), e.GetTag(GameTag.ZONE)) ? ((Zone)e.GetTag(GameTag.ZONE)).ToString() : e.GetTag(GameTag.ZONE).ToString(),
                zone_position = e.GetTag(GameTag.ZONE_POSITION),
                controller = e.GetTag(GameTag.CONTROLLER),
                card_type = Enum.IsDefined(typeof(CardType), e.GetTag(GameTag.CARDTYPE)) ? ((CardType)e.GetTag(GameTag.CARDTYPE)).ToString() : e.GetTag(GameTag.CARDTYPE).ToString(),
                is_hero = e.IsHero,
                is_minion = e.IsMinion,
                attack = e.Attack,
                health = e.Health,
                tags,
            };
        }

        /// <summary>
        /// Bob's Buddy の予測値をリフレクションで読む (BobsBuddyInvoker は internal)。
        /// 失敗したら理由文字列を返す。Phase 1 では「取れるか」を確認するだけ。
        /// </summary>
        private static object? TryReadBobsBuddy(GameStats? stats, int turn)
        {
            if (stats == null) return "no GameStats";
            var t = typeof(Core).Assembly.GetType("Hearthstone_Deck_Tracker.BobsBuddy.BobsBuddyInvoker");
            if (t == null) return "BobsBuddyInvoker type not found";
            var m = t.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return "GetInstance not found";
            var results = new Dictionary<string, object?>();
            foreach (var tr in new[] { turn, turn - 1 })
            {
                if (tr < 1) continue;
                var inst = m.Invoke(null, new object[] { stats.GameId, tr, false });
                if (inst == null)
                {
                    results["turn_" + tr] = null;
                    continue;
                }
                var state = t.GetProperty("State")?.GetValue(inst)?.ToString();
                var output = t.GetProperty("Output")?.GetValue(inst);
                object? outDto = null;
                if (output != null)
                {
                    var ot = output.GetType();
                    float F(string n) => (float)(ot.GetField(n)?.GetValue(output) ?? 0f);
                    int I(string n) => (int)(ot.GetField(n)?.GetValue(output) ?? 0);
                    outDto = new
                    {
                        win_rate = F("winRate"),
                        tie_rate = F("tieRate"),
                        loss_rate = F("lossRate"),
                        my_death_rate = F("myDeathRate"),
                        their_death_rate = F("theirDeathRate"),
                        av_damage = F("avDamage"),
                        simulation_count = I("simulationCount"),
                    };
                }
                results["turn_" + tr] = new { state, output = outDto };
            }
            return results;
        }

        private static object? Safe(Func<object?> f)
        {
            try
            {
                return f();
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
