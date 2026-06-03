using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgentIsland
{
    public sealed class AppSettings
    {
        public event EventHandler Changed;

        public bool AutoHideEnabled { get; set; }
        public int AutoHideDelaySeconds { get; set; }
        public string PaletteName { get; set; }
        public bool CalmMotion { get; set; }
        public bool ShowMultiConversationCount { get; set; }
        public bool KeepCompletedConversations { get; set; }
        public bool FastStatusProbeEnabled { get; set; }
        public bool CompactModeEnabled { get; set; }
        public int CompactDelaySeconds { get; set; }
        public int CompactTitleChars { get; set; }
        public bool TrayNotificationsEnabled { get; set; }

        public static string SettingsPath
        {
            get { return Path.Combine(AppPaths.LocalRoot, "settings.ini"); }
        }

        public static AppSettings Load()
        {
            var settings = Defaults();
            if (!File.Exists(SettingsPath))
            {
                settings.Save(false);
                return settings;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var index = trimmed.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                values[trimmed.Substring(0, index).Trim()] = trimmed.Substring(index + 1).Trim();
            }

            settings.AutoHideEnabled = ReadBool(values, "autoHide", settings.AutoHideEnabled);
            settings.AutoHideDelaySeconds = Clamp(ReadInt(values, "autoHideDelaySeconds", settings.AutoHideDelaySeconds), 3, 60);
            settings.PaletteName = ReadString(values, "palette", settings.PaletteName);
            settings.CalmMotion = ReadBool(values, "calmMotion", settings.CalmMotion);
            settings.ShowMultiConversationCount = ReadBool(values, "showMultiConversationCount", settings.ShowMultiConversationCount);
            settings.KeepCompletedConversations = ReadBool(values, "keepCompletedConversations", settings.KeepCompletedConversations);
            settings.FastStatusProbeEnabled = ReadBool(values, "fastStatusProbe", settings.FastStatusProbeEnabled);
            settings.CompactModeEnabled = ReadBool(values, "compactMode", settings.CompactModeEnabled);
            settings.CompactDelaySeconds = Clamp(ReadInt(values, "compactDelaySeconds", settings.CompactDelaySeconds), 1, 12);
            settings.CompactTitleChars = Clamp(ReadInt(values, "compactTitleChars", settings.CompactTitleChars), 3, 8);
            settings.TrayNotificationsEnabled = ReadBool(values, "trayNotifications", settings.TrayNotificationsEnabled);
            return settings;
        }

        public static AppSettings Defaults()
        {
            return new AppSettings
            {
                AutoHideEnabled = false,
                AutoHideDelaySeconds = 8,
                PaletteName = "Aurora",
                CalmMotion = true,
                ShowMultiConversationCount = true,
                KeepCompletedConversations = true,
                FastStatusProbeEnabled = true,
                CompactModeEnabled = true,
                CompactDelaySeconds = 3,
                CompactTitleChars = 6,
                TrayNotificationsEnabled = true
            };
        }

        public void Save()
        {
            Save(true);
        }

        public void Save(bool notify)
        {
            AppPaths.EnsureDirectories();
            var builder = new StringBuilder();
            builder.AppendLine("# Agent Island settings");
            builder.AppendLine("autoHide=" + AutoHideEnabled.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
            builder.AppendLine("autoHideDelaySeconds=" + AutoHideDelaySeconds.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("palette=" + PaletteName);
            builder.AppendLine("calmMotion=" + CalmMotion.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
            builder.AppendLine("showMultiConversationCount=" + ShowMultiConversationCount.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
            builder.AppendLine("keepCompletedConversations=" + KeepCompletedConversations.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
            builder.AppendLine("fastStatusProbe=" + FastStatusProbeEnabled.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
            builder.AppendLine("compactMode=" + CompactModeEnabled.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
            builder.AppendLine("compactDelaySeconds=" + CompactDelaySeconds.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("compactTitleChars=" + CompactTitleChars.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("trayNotifications=" + TrayNotificationsEnabled.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
            File.WriteAllText(SettingsPath, builder.ToString(), new UTF8Encoding(false));

            if (notify && Changed != null)
            {
                Changed(this, EventArgs.Empty);
            }
        }

        public void Reset()
        {
            var defaults = Defaults();
            AutoHideEnabled = defaults.AutoHideEnabled;
            AutoHideDelaySeconds = defaults.AutoHideDelaySeconds;
            PaletteName = defaults.PaletteName;
            CalmMotion = defaults.CalmMotion;
            ShowMultiConversationCount = defaults.ShowMultiConversationCount;
            KeepCompletedConversations = defaults.KeepCompletedConversations;
            FastStatusProbeEnabled = defaults.FastStatusProbeEnabled;
            CompactModeEnabled = defaults.CompactModeEnabled;
            CompactDelaySeconds = defaults.CompactDelaySeconds;
            CompactTitleChars = defaults.CompactTitleChars;
            TrayNotificationsEnabled = defaults.TrayNotificationsEnabled;
            Save();
        }

        private static bool ReadBool(Dictionary<string, string> values, string key, bool fallback)
        {
            string value;
            if (!values.TryGetValue(key, out value))
            {
                return fallback;
            }

            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static int ReadInt(Dictionary<string, string> values, string key, int fallback)
        {
            string value;
            if (!values.TryGetValue(key, out value))
            {
                return fallback;
            }

            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static string ReadString(Dictionary<string, string> values, string key, string fallback)
        {
            string value;
            return values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
