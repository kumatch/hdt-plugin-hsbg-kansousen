using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HsbgKansousen.Config;
using HsbgKansousen.Diagnostics;

namespace HsbgKansousen.UI
{
    /// <summary>
    /// 設定画面（Options &gt; Tracker &gt; Plugins のボタンと Plugins メニューから開く）。XAML を使わずコードで組む。
    /// 保存すると config.json を書き、次のゲームから反映される（ゲーム中の GameSession は開始時の設定のまま）。
    /// 値の検証は Config/OutputDirectory（テスト対象）に任せ、ここは表示と入出力だけ。
    /// </summary>
    internal sealed class SettingsWindow : Window
    {
        private readonly PluginConfigStore _store;
        private readonly TextBox _outputDir = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 24 };
        private readonly TextBlock _resolved = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        private readonly CheckBox _events = new CheckBox { Content = "Write events.jsonl (shop actions in time order)" };
        private readonly CheckBox _powerLog = new CheckBox { Content = "Save a copy of Power.log for each game (contains the BattleTags of everyone in the lobby)" };
        private readonly CheckBox _gzip = new CheckBox { Content = "Compress the Power.log copy (.power.log.gz)", Margin = new Thickness(20, 4, 0, 0) };
        private readonly CheckBox _hand = new CheckBox { Content = "Record the hand at the end of each shop phase" };
        private readonly TextBlock _status = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        private SettingsWindow(PluginConfigStore store, string version)
        {
            _store = store;
            Title = "HsbgKansousen " + version + " settings";
            Width = 600;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var config = store.Current;
            _outputDir.Text = config.OutputDir;
            _events.IsChecked = config.WriteEventsJsonl;
            _powerLog.IsChecked = config.WritePowerLog;
            _gzip.IsChecked = config.PowerLogGzip;
            _hand.IsChecked = config.SnapshotHand;

            _outputDir.TextChanged += (_, __) => UpdateResolved();
            _powerLog.Checked += (_, __) => _gzip.IsEnabled = true;
            _powerLog.Unchecked += (_, __) => _gzip.IsEnabled = false;
            _gzip.IsEnabled = config.WritePowerLog;
            UpdateResolved();

            Content = BuildLayout();
        }

        /// <summary>UI スレッドから呼ぶ。例外はここで止める（HDT を落とさない）。</summary>
        public static void Open(PluginConfigStore? store, string version)
        {
            try
            {
                if (store == null)
                {
                    PluginLogger.Warn("settings: config store is not loaded");
                    return;
                }
                // config.json を手で直した後に開いたとき、古い値を表示して Save で手の変更（spike_dump など）を上書きしないように
                store.ReloadIfChanged();
                var window = new SettingsWindow(store, version);
                var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
                if (owner != null && owner.IsVisible)
                    window.Owner = owner;
                else
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "SettingsWindow.Open");
            }
        }

        private UIElement BuildLayout()
        {
            var browse = new Button { Content = "Browse...", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            browse.Click += (_, __) => Browse();
            var reset = new Button { Content = "Default", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            reset.Click += (_, __) => _outputDir.Text = OutputDirectory.Default;

            var dirRow = new DockPanel();
            DockPanel.SetDock(reset, Dock.Right);
            DockPanel.SetDock(browse, Dock.Right);
            dirRow.Children.Add(reset);
            dirRow.Children.Add(browse);
            dirRow.Children.Add(_outputDir);

            var save = new Button { Content = "Save", IsDefault = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0) };
            save.Click += (_, __) => Save();
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0) };
            var openLogs = new Button { Content = "Open log folder", Padding = new Thickness(8, 2, 8, 2) };
            openLogs.Click += (_, __) => OpenFolder(_store.Current.ResolveOutputDir());
            var openConfig = new Button { Content = "Open config folder", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
            openConfig.Click += (_, __) => OpenFolder(Path.GetDirectoryName(_store.Path) ?? PluginConfig.DefaultDirectory);

            var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
            DockPanel.SetDock(cancel, Dock.Right);
            DockPanel.SetDock(save, Dock.Right);
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            buttons.Children.Add(openLogs);
            buttons.Children.Add(openConfig);

            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = "Log folder", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(dirRow);
            panel.Children.Add(_resolved);
            panel.Children.Add(new TextBlock { Text = "Files", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
            panel.Children.Add(_events);
            panel.Children.Add(Spaced(_powerLog));
            panel.Children.Add(_gzip);
            panel.Children.Add(Spaced(_hand));
            panel.Children.Add(new TextBlock
            {
                Text = "Changes apply from the next game. Logs already written are not moved.",
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0),
            });
            panel.Children.Add(_status);
            panel.Children.Add(buttons);
            return panel;
        }

        private static CheckBox Spaced(CheckBox box)
        {
            box.Margin = new Thickness(0, 4, 0, 0);
            return box;
        }

        private void UpdateResolved()
        {
            var expanded = Environment.ExpandEnvironmentVariables(_outputDir.Text.Trim());
            _resolved.Text = expanded == _outputDir.Text.Trim() ? "" : "= " + expanded;
        }

        private void Browse()
        {
            try
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "Folder for the game logs";
                    var current = Environment.ExpandEnvironmentVariables(_outputDir.Text.Trim());
                    if (Directory.Exists(current))
                        dialog.SelectedPath = current;
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        _outputDir.Text = OutputDirectory.ToPortable(dialog.SelectedPath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "SettingsWindow.Browse");
                _status.Text = "Could not open the folder picker: " + ex.Message;
            }
        }

        private void Save()
        {
            try
            {
                var error = OutputDirectory.Check(_outputDir.Text, out _);
                if (error != null)
                {
                    _status.Text = error;
                    return;
                }
                // 画面に無い項目（spike_dump など）は今の値を引き継ぐ
                var config = _store.Current.Clone();
                config.OutputDir = _outputDir.Text.Trim();
                config.WriteEventsJsonl = _events.IsChecked == true;
                config.WritePowerLog = _powerLog.IsChecked == true;
                config.PowerLogGzip = _gzip.IsChecked == true;
                config.SnapshotHand = _hand.IsChecked == true;
                config.Save(_store.Path);
                _store.Reload();
                Close();
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "SettingsWindow.Save");
                _status.Text = "Could not save: " + ex.Message;
            }
        }

        private void OpenFolder(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                PluginLogger.Error(ex, "SettingsWindow.OpenFolder");
                _status.Text = "Could not open the folder: " + ex.Message;
            }
        }
    }
}
