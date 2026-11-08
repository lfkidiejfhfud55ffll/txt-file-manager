using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TxtManager
{
    /// <summary>无界面自测：扫描 + 编码探测 + 保存回读。仅在本工作区内建临时文件并清理。</summary>
    internal static class SelfTest
    {
        private static readonly List<string> Report = new List<string>();
        private static int _fail;

        public static int Run(string[] args)
        {
            string dir = args.Length > 1 ? args[1] : ".";
            string outFile = args.Length > 2 ? args[2] : null;

            try
            {
                Line("== TXT 文件管理器 自测 ==");
                Line("目标目录: " + dir);
                ScanTest(dir);
                EncodingTest();
                Line("");
                Line(_fail == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + _fail + ")");
            }
            catch (Exception ex)
            {
                Line("EXCEPTION: " + ex);
                _fail++;
            }

            string text = string.Join("\r\n", Report.ToArray());
            // 先落盘（GUI 子系统程序的标准输出可能不可用），再尝试写控制台。
            if (outFile != null)
            {
                try
                {
                    string full = Path.GetFullPath(outFile);
                    string parent = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent)) Directory.CreateDirectory(parent);
                    File.WriteAllText(full, text, new UTF8Encoding(true));
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "txtmgr_selftest_error.txt"), ex.ToString()); }
                    catch (Exception) { }
                }
            }
            try { Console.WriteLine(text); Console.Out.Flush(); }
            catch (Exception) { }
            return _fail == 0 ? 0 : 1;
        }

        private static void ScanTest(string dir)
        {
            Line("");
            Line("-- 扫描 --");
            if (!Directory.Exists(dir))
            {
                Line("FAIL: 目录不存在 " + dir);
                _fail++;
                return;
            }
            ScanEngine e = new ScanEngine();
            e.IncludeSystemDirs = true;
            Stopwatch sw = Stopwatch.StartNew();
            e.ScanSync(new string[] { dir });
            sw.Stop();
            Line("文件数: " + e.Count + "  目录数: " + e.DirsVisited + "  耗时: " + sw.ElapsedMilliseconds + " ms");

            List<TextFileEntry> items = e.Snapshot();
            items.Sort(delegate(TextFileEntry a, TextFileEntry b) { return b.Length.CompareTo(a.Length); });
            int show = Math.Min(12, items.Count);
            for (int i = 0; i < show; i++)
            {
                TextFileEntry it = items[i];
                string enc = "?";
                try { enc = TextCodec.Load(it.FullPath, 64 * 1024).EncodingName; }
                catch (Exception) { enc = "读取失败"; }
                Line(string.Format("  [{0}] {1} | {2} B | 创建 {3:yyyy-MM-dd HH:mm} | 修改 {4:yyyy-MM-dd HH:mm} | {5}",
                    i + 1, it.FileName, it.Length, it.Created, it.Modified, enc));
                Line("      " + it.DirectoryPath);
            }
            if (items.Count == 0)
            {
                Line("  (该目录下没有 .txt)");
            }
        }

        private static void EncodingTest()
        {
            Line("");
            Line("-- 编码探测与保存回读 --");
            string tmp = Path.Combine(Path.GetTempPath(), "txtmgr_selftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string body = "中文测试 ABC 123\r\n第二行内容\r\n第三行";
                Check(tmp, "utf8_nobom.txt", new UTF8Encoding(false), body);
                Check(tmp, "utf8_bom.txt", new UTF8Encoding(true), body);
                Check(tmp, "gb18030.txt", Encoding.GetEncoding(54936), body);
                Check(tmp, "utf16le.txt", Encoding.Unicode, body);

                // 保存回读
                string p = Path.Combine(tmp, "roundtrip.txt");
                File.WriteAllText(p, "初始", new UTF8Encoding(false));
                TextCodec.Save(p, body, Encoding.GetEncoding(54936));
                LoadedText back = TextCodec.Load(p, int.MaxValue);
                bool ok = back.Text == body && back.Encoding.CodePage == 54936;
                Line((ok ? "PASS" : "FAIL") + "  保存回读(GB18030)  行数=" + back.LineCount);
                if (!ok) _fail++;
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch (Exception) { }
            }
        }

        private static void Check(string dir, string name, Encoding enc, string body)
        {
            string p = Path.Combine(dir, name);
            File.WriteAllText(p, body, enc);
            LoadedText lt = TextCodec.Load(p, int.MaxValue);
            bool textOk = lt.Text == body;
            bool encOk = lt.Encoding.CodePage == enc.CodePage;
            if (!textOk || !encOk) _fail++;
            Line((textOk && encOk ? "PASS" : "FAIL") + "  " + name
                 + "  探测=" + lt.EncodingName + "  行数=" + lt.LineCount
                 + (textOk ? "" : "  文本不一致"));
        }

        private static void Line(string s)
        {
            Report.Add(s);
        }
    }
}
