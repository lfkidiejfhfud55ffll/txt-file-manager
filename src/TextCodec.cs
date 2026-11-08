using System;
using System.IO;
using System.Text;

namespace TxtManager
{
    internal sealed class LoadedText
    {
        public string Text;
        public Encoding Encoding;
        public string EncodingName;
        public bool Truncated;
        public long TotalBytes;
        public int LineCount;
        public DateTime Modified;
    }

    /// <summary>文本读取与编码探测：BOM -> UTF-16 零字节启发 -> 严格 UTF-8 -> GB18030。</summary>
    internal static class TextCodec
    {
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        public static readonly Encoding Utf8Bom = new UTF8Encoding(true);
        private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);

        public static Encoding Gb18030
        {
            get { return Encoding.GetEncoding(54936); }
        }

        public static LoadedText Load(string path, int maxBytes)
        {
            byte[] buffer;
            int read;
            long total;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                total = fs.Length;
                int want = (int)Math.Min(total, (long)maxBytes);
                buffer = new byte[want];
                read = 0;
                while (read < want)
                {
                    int n = fs.Read(buffer, read, want - read);
                    if (n <= 0) break;
                    read += n;
                }
            }

            int bom;
            bool truncated = total > read;
            Encoding enc = Detect(buffer, read, truncated, out bom);
            string text = enc.GetString(buffer, bom, read - bom);

            LoadedText lt = new LoadedText();
            lt.Text = text;
            lt.Encoding = enc;
            lt.EncodingName = NameOf(enc);
            lt.TotalBytes = total;
            lt.Truncated = truncated;
            lt.LineCount = CountLines(text);
            lt.Modified = File.GetLastWriteTime(path);
            return lt;
        }

        public static void Save(string path, string text, Encoding enc)
        {
            // 原地覆盖，不生成副本；File.WriteAllText 会截断已有文件并保留创建时间
            File.WriteAllText(path, text, enc);
        }

        private static bool ValidUtf8(byte[] b, int len)
        {
            try
            {
                Utf8Strict.GetString(b, 0, len);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Encoding Detect(byte[] b, int len, bool truncated, out int bomLen)
        {
            bomLen = 0;
            if (len >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            {
                bomLen = 3;
                return Utf8Bom;
            }
            if (len >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            {
                bomLen = 2;
                return Encoding.Unicode;
            }
            if (len >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            {
                bomLen = 2;
                return Encoding.BigEndianUnicode;
            }

            int n = Math.Min(len, 8192);
            int zerosOdd = 0, zerosEven = 0, pairs = Math.Max(1, n / 2);
            for (int i = 0; i + 1 < n; i += 2)
            {
                if (b[i] == 0) zerosEven++;
                if (b[i + 1] == 0) zerosOdd++;
            }
            if (zerosOdd > pairs * 0.35 && zerosEven == 0) return Encoding.Unicode;
            if (zerosEven > pairs * 0.35 && zerosOdd == 0) return Encoding.BigEndianUnicode;

            if (ValidUtf8(b, len)) return Utf8NoBom;
            if (truncated)
            {
                // 截断可能切在多字节字符中间，退几个字节再判定
                for (int back = 1; back <= 3 && back < len; back++)
                {
                    if (ValidUtf8(b, len - back)) return Utf8NoBom;
                }
            }
            return Gb18030;
        }

        public static string NameOf(Encoding enc)
        {
            if (ReferenceEquals(enc, Utf8Bom)) return "UTF-8 (BOM)";
            if (ReferenceEquals(enc, Utf8NoBom)) return "UTF-8";
            if (enc.CodePage == 54936) return "GB18030 / GBK";
            if (enc.CodePage == 1200) return "UTF-16 LE";
            if (enc.CodePage == 1201) return "UTF-16 BE";
            if (enc.CodePage == 65001) return "UTF-8";
            return enc.WebName;
        }

        public static int CountLines(string text)
        {
            if (text.Length == 0) return 0;
            int n = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') n++;
            }
            if (text[text.Length - 1] != '\n') n++;
            return n;
        }
    }
}
