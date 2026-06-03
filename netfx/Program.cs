using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace AgentIsland
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (HasArg(args, "--install-support"))
            {
                AppPaths.EnsureDirectories();
                SupportFiles.WriteAll();
                var settings = AppSettings.Load();
                settings.Save(false);
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\AgentIsland.DynamicStatus", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                AppPaths.EnsureDirectories();
                SupportFiles.WriteAll();
                var settings = AppSettings.Load();

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var window = new MainWindow(settings);
                TrayService tray = null;
                var coordinator = new StatusCoordinator(delegate(StatusSnapshot snapshot)
                {
                    window.Dispatcher.Invoke(new Action(delegate
                    {
                        window.ApplyStatus(snapshot);
                        if (tray != null)
                        {
                            tray.NotifyStatus(snapshot);
                        }
                    }));
                }, settings);
                window.SetConversationClearHandler(coordinator.ClearConversation);
                var tailer = new EventTailer(AppPaths.EventLogPath, coordinator.HandleLine);
                var codexActivityWatcher = new CodexActivityWatcher(settings, coordinator.HandleLine);
                tray = new TrayService(window, coordinator, settings);

                if (File.Exists(AppPaths.LastEventPath))
                {
                    var age = DateTime.Now - File.GetLastWriteTime(AppPaths.LastEventPath);
                    if (age < TimeSpan.FromSeconds(10))
                    {
                        coordinator.HandleLine(File.ReadAllText(AppPaths.LastEventPath));
                    }
                }

                app.Exit += delegate
                {
                    codexActivityWatcher.Dispose();
                    tailer.Dispose();
                    tray.Dispose();
                };

                window.Show();
                tailer.Start();
                codexActivityWatcher.Start();
                app.Run();
            }
        }

        private static bool HasArg(string[] args, string expected)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
