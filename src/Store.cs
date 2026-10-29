using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Lamplight
{
    public class BookmarkItem
    {
        public int Chapter { get; set; }
        public int Offset { get; set; }
        public string ChapterTitle { get; set; }
        public string Excerpt { get; set; }
        public long Created { get; set; }
    }

    public class ExcerptItem
    {
        public int Chapter { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
        public string ChapterTitle { get; set; }
        public string Text { get; set; }
        public long Created { get; set; }
    }

    public class ChapterRead
    {
        public int Chapter { get; set; }
        public float Percent { get; set; }
    }

    public class Progress
    {
        public int Chapter { get; set; }
        public int Offset { get; set; }
        public double Percent { get; set; }
    }

    public class BookEntry
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }        // 用户自定义显示名；空则用 AutoName
        public string AutoName { get; set; }
        public long Added { get; set; }
        public long LastRead { get; set; }
        public bool Pinned { get; set; }
        public bool Online { get; set; }
        public string SourceId { get; set; }
        public string Url { get; set; }
        public Progress Progress { get; set; }
        public List<BookmarkItem> Bookmarks { get; set; }
        public List<ExcerptItem> Excerpts { get; set; }
        public List<ChapterRead> ReadLog { get; set; }
        public long ReadingSeconds { get; set; }

        public BookEntry()
        {
            Name = "";
            AutoName = "";
            Progress = new Progress();
            Bookmarks = new List<BookmarkItem>();
            Excerpts = new List<ExcerptItem>();
            ReadLog = new List<ChapterRead>();
            ReadingSeconds = 0;
        }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(Name) ? AutoName : Name; }
        }
    }

    public class Settings
    {
        public string Theme { get; set; }
        public float FontSize { get; set; }
        public float LineScale { get; set; }
        public float PageWidth { get; set; }
        public int AutoScrollMs { get; set; }
        public bool Indent { get; set; }
        public string LastBook { get; set; }
        public string Sort { get; set; }
        public bool TopMost { get; set; }
        public float Opacity { get; set; }
        public string FontFamily { get; set; }
        public bool PageMode { get; set; }
        public int ClickMode { get; set; }
        public bool CompressBlank { get; set; }
        public string ChapterRegex { get; set; }
        public string Proxy { get; set; }
        public bool ShowChrome { get; set; }
        public string BgImage { get; set; }
        public int BgMode { get; set; }
        public int BgDim { get; set; }
        public int PanelOpacity { get; set; }
        public string TextColor { get; set; }
        public float LetterSpacing { get; set; }
        public float ParaGapScale { get; set; }
        public float MarginX { get; set; }
        public Dictionary<string, string> KeyMap { get; set; }
        public int TtsRate { get; set; }
        public int TtsVolume { get; set; }
        public int TtsAutoStopMinutes { get; set; }
        public string TtsVoice { get; set; }

        public Settings()
        {
            Theme = "Paper";
            FontSize = 20f;
            LineScale = 1.9f;
            PageWidth = 740f;
            AutoScrollMs = 3200;
            Indent = true;
            LastBook = "";
            Sort = "LastRead";
            TopMost = false;
            Opacity = 1f;
            FontFamily = "";
            PageMode = false;
            ClickMode = 1;
            CompressBlank = true;
            ChapterRegex = "";
            Proxy = "";
            ShowChrome = true;
            BgImage = "";
            BgMode = 0;
            BgDim = 18;
            PanelOpacity = 82;
            TextColor = "";
            LetterSpacing = 0f;
            ParaGapScale = 0.42f;
            MarginX = 0f;
            KeyMap = Lamplight.KeyMap.Defaults();
            TtsRate = 0;
            TtsVolume = 90;
            TtsAutoStopMinutes = 0;
            TtsVoice = "";
        }
    }

    public class Library
    {
        public int Version { get; set; }
        public Settings Settings { get; set; }
        public List<BookEntry> Books { get; set; }

        public Library()
        {
            Version = 1;
            Settings = new Settings();
            Books = new List<BookEntry>();
        }
    }

    /// <summary>数据层：library.json 的读写与书籍增删改查。</summary>
    public static class Store
    {
        public static string DataDir;
        public static string DataFile;
        public static Library Lib;

        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static void Init(string exeDir)
        {
            DataDir = exeDir;
            DataFile = Path.Combine(DataDir, "library.json");
            string probe = Path.Combine(DataDir, ".write-test");
            bool ok = true;
            try
            {
                File.WriteAllText(probe, "1");
                File.Delete(probe);
            }
            catch { ok = false; }
            if (!ok)
            {
                DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lamplight");
                Directory.CreateDirectory(DataDir);
                DataFile = Path.Combine(DataDir, "library.json");
            }
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(DataFile))
                {
                    string s = File.ReadAllText(DataFile, Encoding.UTF8);
                    Lib = Json.Deserialize<Library>(s);
                }
                if (Lib == null && File.Exists(DataFile + ".bak"))
                {
                    string s2 = File.ReadAllText(DataFile + ".bak", Encoding.UTF8);
                    Lib = Json.Deserialize<Library>(s2);
                }
            }
            catch
            {
                Lib = null;
                try
                {
                    if (File.Exists(DataFile + ".bak"))
                        Lib = Json.Deserialize<Library>(File.ReadAllText(DataFile + ".bak", Encoding.UTF8));
                }
                catch { }
            }
            if (Lib == null) Lib = new Library();
            if (Lib.Settings == null) Lib.Settings = new Settings();
            if (Lib.Settings.KeyMap == null) Lib.Settings.KeyMap = Lamplight.KeyMap.Defaults();
            if (Lib.Books == null) Lib.Books = new List<BookEntry>();
            foreach (BookEntry b in Lib.Books)
            {
                if (b.Bookmarks == null) b.Bookmarks = new List<BookmarkItem>();
                if (b.Excerpts == null) b.Excerpts = new List<ExcerptItem>();
                if (b.ReadLog == null) b.ReadLog = new List<ChapterRead>();
                if (b.Progress == null) b.Progress = new Progress();
            }
        }

        public static void Save()
        {
            try
            {
                string text = Json.Serialize(Lib);
                string tmp = DataFile + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(DataFile))
                {
                    try { File.Copy(DataFile, DataFile + ".bak", true); } catch { }
                }
                File.Copy(tmp, DataFile, true);
                try { File.Delete(tmp); } catch { }
            }
            catch { }
        }

        public static string MakeId(string path)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static long Now()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

        public static BookEntry Find(string id)
        {
            foreach (BookEntry b in Lib.Books) if (b.Id == id) return b;
            return null;
        }

        public static BookEntry FindByPath(string path)
        {
            string full = Path.GetFullPath(path);
            foreach (BookEntry b in Lib.Books)
                if (string.Equals(b.Path, full, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        public static BookEntry AddOrGet(string path)
        {
            string full = Path.GetFullPath(path);
            BookEntry b = FindByPath(full);
            if (b != null) return b;
            b = new BookEntry();
            b.Id = MakeId(full);
            b.Path = full;
            b.AutoName = Path.GetFileNameWithoutExtension(full);
            b.Added = Now();
            b.LastRead = 0;
            Lib.Books.Add(b);
            Save();
            return b;
        }

        public static void Remove(BookEntry b)
        {
            Lib.Books.Remove(b);
            if (Lib.Settings.LastBook == b.Id) Lib.Settings.LastBook = "";
            Save();
        }

        public static List<BookEntry> Sorted()
        {
            List<BookEntry> list = new List<BookEntry>(Lib.Books);
            list.Sort(delegate(BookEntry a, BookEntry b)
            {
                if (a.Pinned != b.Pinned) return a.Pinned ? -1 : 1;
                int c = b.LastRead.CompareTo(a.LastRead);
                if (c != 0) return c;
                return b.Added.CompareTo(a.Added);
            });
            return list;
        }

        public static string HumanTime(long unix)
        {
            if (unix <= 0) return "未读";
            DateTime t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToLocalTime().AddSeconds(unix);
            TimeSpan d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "刚刚";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " 分钟前";
            if (d.TotalHours < 24 && t.Date == DateTime.Today) return "今天 " + t.ToString("HH:mm");
            if (d.TotalDays < 1) return "昨天 " + t.ToString("HH:mm");
            if (d.TotalDays < 7) return (int)d.TotalDays + " 天前";
            if (t.Year == DateTime.Now.Year) return t.ToString("M 月 d 日");
            return t.ToString("yyyy 年 M 月 d 日");
        }
    }
}
