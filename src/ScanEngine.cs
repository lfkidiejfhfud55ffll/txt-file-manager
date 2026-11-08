using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TxtManager
{
    /// <summary>
    /// 多线程（每个磁盘一个线程）BFS 扫描 .txt。只读取目录项与文件元数据，绝不复制/移动文件。
    /// 线程安全：UI 线程通过 Snapshot / TakeNew 读取结果。
    /// </summary>
    internal sealed class ScanEngine
    {
        private static readonly string[] SkipDirNames = new string[]
        {
            "$recycle.bin", "system volume information", "winsxs", "$windows.~bt",
            "$windows.~ws", "recovery", "config.msi", "windowsapps", "installer"
        };

        private readonly object _sync = new object();
        private readonly List<TextFileEntry> _items = new List<TextFileEntry>();
        private volatile bool _cancel;
        private volatile bool _running;
        private volatile bool _finished;
        private int _dirsVisited;
        private int _dirsSkipped;
        private int _filesSeen;
        private volatile string _currentDir = "";
        private DateTime _startedAt;
        private DateTime _finishedAt;

        public bool IncludeSystemDirs;
        public bool Cancelled { get { return _cancel; } }
        public bool IsRunning { get { return _running; } }
        public bool IsFinished { get { return _finished; } }
        public int DirsVisited { get { return _dirsVisited; } }
        public int DirsSkipped { get { return _dirsSkipped; } }
        public string CurrentDir { get { return _currentDir; } }
        public TimeSpan Elapsed
        {
            get
            {
                DateTime end = _finished ? _finishedAt : DateTime.Now;
                return end - _startedAt;
            }
        }

        public int Count
        {
            get { lock (_sync) return _items.Count; }
        }

        public List<TextFileEntry> Snapshot()
        {
            lock (_sync) return new List<TextFileEntry>(_items);
        }

        /// <summary>取出 fromIndex 之后新增的条目（增量刷新用，避免整表复制）。</summary>
        public List<TextFileEntry> TakeNew(int fromIndex)
        {
            lock (_sync)
            {
                if (fromIndex < 0) fromIndex = 0;
                if (fromIndex >= _items.Count) return new List<TextFileEntry>();
                return _items.GetRange(fromIndex, _items.Count - fromIndex);
            }
        }

        public void Cancel()
        {
            _cancel = true;
        }

        /// <summary>后台启动扫描。</summary>
        public void Start(string[] roots)
        {
            Reset();
            _running = true;
            Thread t = new Thread(delegate() { Work(roots); });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Name = "txt-scan";
            t.Start();
        }

        /// <summary>在当前线程同步扫描（自测用）。</summary>
        public void ScanSync(string[] roots)
        {
            Reset();
            _running = true;
            Work(roots);
        }

        private void Reset()
        {
            lock (_sync) _items.Clear();
            _cancel = false;
            _finished = false;
            _dirsVisited = 0;
            _dirsSkipped = 0;
            _filesSeen = 0;
            _currentDir = "";
            _startedAt = DateTime.Now;
            _finishedAt = DateTime.Now;
        }

        private void Work(string[] roots)
        {
            try
            {
                List<Thread> threads = new List<Thread>();
                foreach (string root in roots)
                {
                    string r = root;
                    Thread t = new Thread(delegate() { ScanRoot(r); });
                    t.IsBackground = true;
                    t.Priority = ThreadPriority.BelowNormal;
                    t.Name = "txt-scan " + r;
                    t.Start();
                    threads.Add(t);
                }
                foreach (Thread t in threads) t.Join();
            }
            catch (Exception)
            {
                // 单盘异常不影响整体
            }
            finally
            {
                _finishedAt = DateTime.Now;
                _running = false;
                _finished = true;
            }
        }

        private void ScanRoot(string root)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0 && !_cancel)
            {
                string dir = stack.Pop();
                if (ShouldSkip(dir))
                {
                    Interlocked.Increment(ref _dirsSkipped);
                    continue;
                }
                Interlocked.Increment(ref _dirsVisited);
                _currentDir = dir;

                string[] files = null;
                try
                {
                    files = Directory.GetFiles(dir, "*.txt", SearchOption.TopDirectoryOnly);
                }
                catch (Exception) { }

                if (files != null && files.Length > 0)
                {
                    List<TextFileEntry> batch = new List<TextFileEntry>(files.Length);
                    for (int i = 0; i < files.Length; i++)
                    {
                        try
                        {
                            batch.Add(TextFileEntry.FromPath(files[i]));
                        }
                        catch (Exception) { }
                    }
                    if (batch.Count > 0)
                    {
                        lock (_sync)
                        {
                            _items.AddRange(batch);
                            _filesSeen += batch.Count;
                        }
                    }
                }

                string[] subs = null;
                try
                {
                    subs = Directory.GetDirectories(dir);
                }
                catch (Exception) { }

                if (subs != null)
                {
                    for (int i = 0; i < subs.Length; i++)
                    {
                        try
                        {
                            FileAttributes a = File.GetAttributes(subs[i]);
                            if ((a & FileAttributes.ReparsePoint) != 0) continue; // 跳过符号链接/联接点，防环
                        }
                        catch (Exception) { }
                        stack.Push(subs[i]);
                    }
                }
            }
        }

        private bool ShouldSkip(string dir)
        {
            string full;
            try { full = Path.GetFullPath(dir).TrimEnd('\\'); }
            catch (Exception) { return true; }
            if (full.Length == 2) return false; // 盘符根

            string name = Path.GetFileName(full);
            for (int i = 0; i < SkipDirNames.Length; i++)
            {
                if (string.Equals(name, SkipDirNames[i], StringComparison.OrdinalIgnoreCase)) return true;
            }

            if (!IncludeSystemDirs)
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (win.Length > 0 && full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>可扫描的固定磁盘根目录。</summary>
        public static string[] GetRoots()
        {
            List<string> list = new List<string>();
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    try
                    {
                        if (drives[i].DriveType == DriveType.Fixed && drives[i].IsReady)
                            list.Add(drives[i].RootDirectory.FullName);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            if (list.Count == 0) list.Add("C:\\");
            return list.ToArray();
        }
    }
}
