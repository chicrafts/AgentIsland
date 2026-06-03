using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AgentIsland
{
    public sealed class EventTailer : IDisposable
    {
        private readonly string path;
        private readonly Action<string> onLine;
        private readonly object gate = new object();
        private FileSystemWatcher watcher;
        private long offset;
        private string remainder = string.Empty;
        private bool disposed;
        private int queued;

        public EventTailer(string path, Action<string> onLine)
        {
            this.path = path;
            this.onLine = onLine;
        }

        public void Start()
        {
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, string.Empty);
            }

            offset = new FileInfo(path).Length;
            watcher = new FileSystemWatcher(directory, Path.GetFileName(path));
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
            watcher.Changed += delegate { ScheduleDrain(); };
            watcher.Created += delegate { ScheduleDrain(); };
            watcher.EnableRaisingEvents = true;
        }

        private void ScheduleDrain()
        {
            if (Interlocked.Exchange(ref queued, 1) == 1)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(15);
                Interlocked.Exchange(ref queued, 0);
                Drain();
            });
        }

        private void Drain()
        {
            if (disposed)
            {
                return;
            }

            lock (gate)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (stream.Length < offset)
                        {
                            offset = 0;
                        }

                        if (stream.Length == offset)
                        {
                            return;
                        }

                        stream.Position = offset;
                        var buffer = new byte[stream.Length - offset];
                        var read = stream.Read(buffer, 0, buffer.Length);
                        offset = stream.Position;
                        if (read <= 0)
                        {
                            return;
                        }

                        var text = remainder + Encoding.UTF8.GetString(buffer, 0, read);
                        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        var complete = text.EndsWith("\n", StringComparison.Ordinal);
                        remainder = complete ? string.Empty : lines[lines.Length - 1];
                        var limit = complete ? lines.Length : lines.Length - 1;

                        for (var i = 0; i < limit; i++)
                        {
                            if (!string.IsNullOrWhiteSpace(lines[i]))
                            {
                                onLine(lines[i]);
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            disposed = true;
            if (watcher != null)
            {
                watcher.Dispose();
            }
        }
    }
}
