using System;
using System.IO;

namespace TxtManager
{
    /// <summary>一条 .txt 文件的索引信息。只记录元数据，不复制文件。</summary>
    internal sealed class TextFileEntry
    {
        public string FullPath;
        public string DirectoryPath;
        public string FileName;
        public long Length;
        public DateTime Created;
        public DateTime Modified;
        public DateTime Accessed;
        public FileAttributes Attributes;

        public static TextFileEntry FromPath(string path)
        {
            FileInfo fi = new FileInfo(path);
            TextFileEntry e = new TextFileEntry();
            e.FullPath = fi.FullName;
            e.FileName = fi.Name;
            e.DirectoryPath = fi.DirectoryName;
            e.Length = fi.Length;
            e.Created = fi.CreationTime;
            e.Modified = fi.LastWriteTime;
            e.Accessed = fi.LastAccessTime;
            e.Attributes = fi.Attributes;
            return e;
        }

        public string AttributesText()
        {
            string s = "";
            if ((Attributes & FileAttributes.ReadOnly) != 0) s += "只读 ";
            if ((Attributes & FileAttributes.Hidden) != 0) s += "隐藏 ";
            if ((Attributes & FileAttributes.System) != 0) s += "系统 ";
            if ((Attributes & FileAttributes.Archive) != 0) s += "存档 ";
            if (s.Length == 0) s = "普通";
            return s.Trim();
        }
    }

    internal static class Fmt
    {
        private static readonly string[] Units = new string[] { "KB", "MB", "GB", "TB" };

        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes.ToString() + " B";
            double v = bytes;
            int i = -1;
            do
            {
                v /= 1024.0;
                i++;
            } while (v >= 1024.0 && i < Units.Length - 1);
            return (v >= 100.0 ? v.ToString("0") : v.ToString("0.0")) + " " + Units[i];
        }

        public static string Number(long n)
        {
            return n.ToString("N0");
        }

        public static string Time(DateTime t)
        {
            if (t.Year < 1601) return "-";
            return t.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static string TimeShort(DateTime t)
        {
            if (t.Year < 1601) return "-";
            return t.ToString("yyyy-MM-dd HH:mm");
        }

        public static string Elapsed(TimeSpan ts)
        {
            if (ts.TotalSeconds < 60.0) return ts.TotalSeconds.ToString("0.0") + " 秒";
            return ((int)ts.TotalMinutes) + " 分 " + ts.Seconds.ToString("00") + " 秒";
        }
    }
}
