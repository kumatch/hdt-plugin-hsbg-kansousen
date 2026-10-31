using System;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using HsbgKansousen.Capture;
using HsbgKansousen.Config;
using HsbgKansousen.Diagnostics;
using HsbgKansousen.Output;
using HsbgKansousen.Spike;
using HsbgKansousen.UI;
using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HsbgKansousen
{
    /// <summary>
    /// HDT プラグインのエントリポイント。イベントの登録だけを行い、ロジックは持たない。
    /// 注意: GameEvents.*.Add は「呼び出し元メソッドの型 == IPlugin 実装クラス」を StackTrace で判定して
    /// プラグインに紐付け、無効化時に HDT が外す (ActionList に Remove は無い)。必ずこのクラスのメソッド内から直接 Add する。
    /// </summary>
    public class HsbgKansousenPlugin : IPlugin
    {
        private PluginConfigStore? _store;
        private GameSession? _session;
        private SpikeRecorder? _spike;
        private MenuItem? _menuItem;

        public string Name => "HsbgKansousen";
        public string Description => "Writes one JSON log per Battlegrounds game (plus events.jsonl and a copy of Power.log) for later analysis. Choose the log folder with the Settings button.";
        public string ButtonText => "Settings";
        public string Author => "kumatch";
        public Version Version => typeof(HsbgKansousenPlugin).Assembly.GetName().Version ?? new Version(0, 0, 0);

        /// <summary>
        /// HDT の「Plugins」メニューに追加される項目。押すと設定画面を開く（フォルダを開く操作は設定画面にある）。
        /// UI スレッド以外から呼ばれて WPF が例外を出したら null（項目なし）。
        /// </summary>
        public MenuItem MenuItem
        {
            get
            {
                if (_menuItem != null) return _menuItem;
                try
                {
                    var root = new MenuItem { Header = "HsbgKansousen" };
                    root.Click += (_, __) => OpenSettings();
                    _menuItem = root;
                    return root;
                }
                catch (Exception ex)
                {
                    PluginLogger.Warn("MenuItem could not be created: " + ex.Message);
                    return null!;
                }
            }
        }

        public void OnLoad()
        {
            try
            {
                var version = Version.ToString(3);
                PluginLogger.Info($"HsbgKansousen {version} loading (HDT {Hearthstone_Deck_Tracker.Helper.GetCurrentVersion()})");
                MigrateLegacyConfig();
                _store = PluginConfigStore.Default();
                var config = _store.Current;
                PluginLogger.Info($"config: output_dir={config.ResolveOutputDir()} events={config.WriteEventsJsonl} power_log={config.WritePowerLog} hand={config.SnapshotHand} spike_dump={config.SpikeDump}");

                RecoverPartialLogs(config);

                var session = new GameSession(_store, version);
                _session = session;
                GameEvents.OnGameStart.Add(session.OnGameStart);
                GameEvents.OnTurnStart.Add(session.OnTurnStart);
                GameEvents.OnGameEnd.Add(session.OnGameEnd);
                GameEvents.OnInMenu.Add(session.OnInMenu);
                GameEvents.OnEntityWillTakeDamage.Add(session.OnEntityWillTakeDamage);

                if (config.SpikeDump)
                {
                    var store = _store;
                    var spike = new SpikeRecorder(() => store.Current.ResolveOutputDir());
                    _spike = spike;
                    GameEvents.OnGameStart.Add(spike.OnGameStart);
                    GameEvents.OnTurnStart.Add(spike.OnTurnStart);
                    GameEvents.OnGameEnd.Add(spike.OnGameEnd);
                    GameEvents.OnInMenu.Add(spike.OnInMenu);
                    GameEvents.OnEntityWillTakeDamage.Add(spike.OnEntityWillTakeDamage);
                    PluginLogger.Info("spike dump enabled: " + spike.SpikeRoot);
                }

                HdtLog.Info($"HsbgKansousen {version} loaded");
                PluginLogger.Info("GameEvents registered");
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "OnLoad");
                HdtLog.Error("HsbgKansousen OnLoad failed: " + ex);
            }
        }

        /// <summary>改名前（HsbgLogger）の config.json を引き継ぐ（新しい設定フォルダに無いときだけ）。</summary>
        private static void MigrateLegacyConfig()
        {
            try
            {
                var legacy = System.IO.Path.Combine(PluginConfig.LegacyDirectory, "config.json");
                if (PluginConfig.MigrateLegacy(PluginConfig.DefaultPath, legacy))
                    PluginLogger.Info("copied the config of the renamed plugin: " + legacy + " -> " + PluginConfig.DefaultPath);
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "MigrateLegacyConfig");
            }
        }

        /// <summary>前回のゲーム中に HDT が落ちた/終了した場合の部分ログを incomplete として正規の場所へ移す。</summary>
        private static void RecoverPartialLogs(PluginConfig config)
        {
            try
            {
                var root = config.ResolveOutputDir();
                var recovered = new PartialLogJournal(root).RecoverAll(new GameLogWriter(root), PluginLogger.Warn);
                foreach (var path in recovered)
                    PluginLogger.Info("recovered incomplete log: " + path);
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "RecoverPartialLogs");
            }
        }

        public void OnUnload()
        {
            try
            {
                // ハンドラ自体は HDT が (IsEnabled=false を見て) 次のイベント実行時に外す。ここでは無害化と書き出しだけ行う
                if (_session != null)
                {
                    _session.Flush();
                    _session.Enabled = false;
                }
                if (_spike != null) _spike.Enabled = false;
                _session = null;
                _spike = null;
                PluginLogger.Info("HsbgKansousen unloaded");
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "OnUnload");
            }
        }

        public void OnButtonPress() => OpenSettings();

        private void OpenSettings() => SettingsWindow.Open(_store, Version.ToString(3));

        public void OnUpdate()
        {
            try
            {
                _session?.Update();
                _spike?.Update();
            }
            catch (Exception ex)
            {
                // HDT は OnUpdate の例外が 100 回を超えるとプラグインを無効化するので、必ずここで止める
                PluginLogger.Error(ex, "OnUpdate");
            }
        }
    }
}
