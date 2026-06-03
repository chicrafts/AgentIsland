using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace AgentIsland
{
    public sealed class TrayService : IDisposable
    {
        private readonly MainWindow window;
        private readonly StatusCoordinator coordinator;
        private readonly AppSettings settings;
        private readonly Forms.NotifyIcon icon;
        private readonly Icon trayIcon;
        private SettingsWindow settingsWindow;
        private string lastAttentionKey;
        private DateTime lastAttentionUtc;

        public TrayService(MainWindow window, StatusCoordinator coordinator, AppSettings settings)
        {
            this.window = window;
            this.coordinator = coordinator;
            this.settings = settings;
            trayIcon = CreateIcon();
            icon = new Forms.NotifyIcon();
            icon.Icon = trayIcon;
            icon.Text = "Agent Island";
            icon.Visible = true;
            icon.ContextMenuStrip = BuildMenu();
            icon.DoubleClick += delegate { ToggleWindow(); };
        }

        public void NotifyStatus(StatusSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            var needsAttention = snapshot.Mode == IslandMode.Waiting || snapshot.Mode == IslandMode.Error;
            if (!needsAttention)
            {
                lastAttentionKey = null;
                return;
            }

            var key = (snapshot.SessionKey ?? string.Empty) + "|" + snapshot.Mode + "|" + snapshot.Title + "|" + snapshot.Detail;
            var now = DateTime.UtcNow;
            if (string.Equals(lastAttentionKey, key, StringComparison.Ordinal) &&
                now - lastAttentionUtc < TimeSpan.FromSeconds(45))
            {
                return;
            }

            lastAttentionKey = key;
            lastAttentionUtc = now;
            var title = "Agent Island · " + snapshot.Agent;
            var body = TrimBalloonText(snapshot.Title + "\n" + snapshot.Detail);
            icon.ShowBalloonTip(7000, title, body, snapshot.Mode == IslandMode.Error ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Warning);
        }
        private Forms.ContextMenuStrip BuildMenu()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏", null, delegate { ToggleWindow(); });
            menu.Items.Add("设置", null, delegate { OpenSettings(); });
            menu.Items.Add("演示状态", null, delegate { coordinator.Demo(); });
            menu.Items.Add("清除会话", null, delegate { coordinator.Reset(); window.CollapseConversationList(); });
            menu.Items.Add("回到待命", null, delegate { coordinator.Reset(); window.CollapseConversationList(); });
            menu.Items.Add("接入 Codex hooks", null, delegate { ConnectCodex(); });
            menu.Items.Add("接入 Claude Code hooks", null, delegate { ConnectClaude(); });
            menu.Items.Add("打开配置目录", null, delegate { OpenFolder(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate
            {
                icon.Visible = false;
                System.Windows.Application.Current.Shutdown();
            });
            return menu;
        }

        private void ToggleWindow()
        {
            if (window.IsVisible)
            {
                window.HideIsland();
            }
            else
            {
                window.ShowIsland();
            }
        }

        private void OpenSettings()
        {
            if (settingsWindow != null && settingsWindow.IsVisible)
            {
                settingsWindow.Activate();
                return;
            }

            settingsWindow = new SettingsWindow(settings);
            settingsWindow.Closed += delegate { settingsWindow = null; };
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        private static void OpenFolder()
        {
            Process.Start(new ProcessStartInfo(AppPaths.LocalRoot) { UseShellExecute = true });
        }

        private static void ConnectCodex()
        {
            var script = Path.Combine(AppPaths.LocalRoot, "connect-codex.ps1");
            Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
            {
                UseShellExecute = true
            });
        }


        private static void ConnectClaude()
        {
            var script = Path.Combine(AppPaths.LocalRoot, "connect-claude.ps1");
            Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
            {
                UseShellExecute = true
            });
        }
        private static Icon CreateIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.ico");
                if (File.Exists(iconPath))
                {
                    return new Icon(iconPath);
                }
            }
            catch
            {
            }

            var bitmap = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var bg = new SolidBrush(Color.FromArgb(255, 14, 17, 23)))
                using (var green = new SolidBrush(Color.FromArgb(255, 54, 211, 153)))
                {
                    g.FillEllipse(bg, 2, 2, 60, 60);
                    g.FillEllipse(green, 23, 23, 18, 18);
                }
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }

        private static string TrimBalloonText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "需要你回到终端处理。";
            }

            value = value.Replace("\r", " ").Trim();
            return value.Length > 180 ? value.Substring(0, 177) + "..." : value;
        }
        public void Dispose()
        {
            icon.Dispose();
            trayIcon.Dispose();
        }
    }
}
