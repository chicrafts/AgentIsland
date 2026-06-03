using System;
using System.IO;

namespace AgentIsland
{
    public static class AppPaths
    {
        public static readonly string LocalRoot =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentIsland");

        public static string EventLogPath { get { return Path.Combine(LocalRoot, "events.ndjson"); } }
        public static string LastEventPath { get { return Path.Combine(LocalRoot, "last-event.json"); } }
        public static string HookScriptPath { get { return Path.Combine(LocalRoot, "hook.ps1"); } }
        public static string ExamplesPath { get { return Path.Combine(LocalRoot, "examples"); } }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(LocalRoot);
            Directory.CreateDirectory(ExamplesPath);
            if (!File.Exists(EventLogPath))
            {
                File.WriteAllText(EventLogPath, string.Empty);
            }
        }
    }
}
