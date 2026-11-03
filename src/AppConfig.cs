using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace KaoYanReminder
{
    public class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding
        {
            get { return new UTF8Encoding(false); }
        }
    }

    [XmlRoot("KaoYanReminder")]
    public class AppConfig
    {
        public const string DefaultPlanName = "每日计划";
        public const string RestPlanName = "休息日计划";

        public AppSettings Settings { get; set; }

        [XmlArray("Plans")]
        [XmlArrayItem("Plan")]
        public List<PlanProfile> Plans { get; set; }

        public TodaySelection TodayPlan { get; set; }

        public AppConfig()
        {
            Settings = new AppSettings();
            Plans = new List<PlanProfile>();
            TodayPlan = new TodaySelection();
        }

        public static AppConfig CreateDefault()
        {
            AppConfig c = new AppConfig();
            PlanProfile daily = new PlanProfile();
            daily.Name = DefaultPlanName;
            daily.Items = CreateDefaultDailyItems();
            c.Plans.Add(daily);

            PlanProfile rest = new PlanProfile();
            rest.Name = RestPlanName;
            rest.Items = CreateDefaultDailyItems();
            c.Plans.Add(rest);

            c.Settings = new AppSettings();
            DateTime today = DateTime.Today;
            c.TodayPlan = new TodaySelection();
            c.TodayPlan.Date = today.ToString("yyyy-MM-dd");
            c.TodayPlan.PlanName = DefaultPlanName;
            return c;
        }

        public static List<PlanItem> CreateDefaultDailyItems()
        {
            List<PlanItem> list = new List<PlanItem>();
            AddItem(list, "06:30", "07:30", "起床 + 早自习");
            AddItem(list, "07:30", "08:00", "吃饭");
            AddItem(list, "08:00", "09:00", "学习");
            AddItem(list, "09:10", "10:10", "学习");
            AddItem(list, "10:30", "11:30", "学习");
            AddItem(list, "11:40", "12:20", "学习");
            AddItem(list, "12:20", "13:00", "午饭、午休");
            AddItem(list, "13:00", "13:30", "睡觉");
            AddItem(list, "13:30", "13:40", "醒神");
            AddItem(list, "13:40", "14:40", "学习");
            AddItem(list, "14:50", "15:50", "学习");
            AddItem(list, "16:00", "17:00", "学习");
            AddItem(list, "17:10", "18:00", "晚饭");
            AddItem(list, "18:00", "18:40", "晚自习 1");
            AddItem(list, "18:50", "19:30", "晚自习 2");
            AddItem(list, "19:30", "20:20", "晚自习 3");
            AddItem(list, "20:20", "21:00", "晚自习 4");
            AddItem(list, "21:10", "21:50", "晚自习 5");
            AddItem(list, "22:00", "22:30", "每日复盘");
            AddItem(list, "23:00", "", "上床睡觉");
            return list;
        }

        private static void AddItem(List<PlanItem> list, string start, string end, string title)
        {
            PlanItem item = new PlanItem();
            item.Enabled = true;
            item.Start = start;
            item.End = end;
            item.Title = title;
            list.Add(item);
        }

        public PlanProfile GetPlan(string name)
        {
            if (Plans == null) return null;
            foreach (PlanProfile p in Plans)
            {
                if (p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        public PlanProfile GetTodayProfile()
        {
            PlanProfile p = GetPlan(TodayPlan == null ? null : TodayPlan.PlanName);
            if (p == null) p = GetPlan(DefaultPlanName);
            if (p == null && Plans != null && Plans.Count > 0) p = Plans[0];
            return p;
        }

        public string ActivePlanName
        {
            get
            {
                PlanProfile p = GetTodayProfile();
                return p == null ? DefaultPlanName : p.Name;
            }
        }

        /// <summary>跨天或方案失效时回到默认计划，返回是否发生变化。</summary>
        public bool EnsureTodayPlan(DateTime today)
        {
            if (TodayPlan == null) TodayPlan = new TodaySelection();
            string dateStr = today.ToString("yyyy-MM-dd");
            bool changed = false;
            if (TodayPlan.Date != dateStr)
            {
                TodayPlan.Date = dateStr;
                TodayPlan.PlanName = DefaultPlanName;
                changed = true;
            }
            if (GetPlan(TodayPlan.PlanName) == null)
            {
                TodayPlan.PlanName = DefaultPlanName;
                changed = true;
            }
            return changed;
        }

        /// <summary>补齐缺省值、去重方案名、规范化时间并排序。</summary>
        public void Normalize()
        {
            if (Settings == null) Settings = new AppSettings();
            if (Settings.RingSeconds < 5) Settings.RingSeconds = 30;
            if (Settings.RingSeconds > 600) Settings.RingSeconds = 600;
            if (Settings.GraceSeconds <= 0) Settings.GraceSeconds = 120;
            if (Settings.GraceSeconds > 600) Settings.GraceSeconds = 600;
            if (Settings.BarX < -1) Settings.BarX = -1;
            if (Settings.BarY < -1) Settings.BarY = -1;
            if (Settings.SoundFile == null) Settings.SoundFile = "";
            if (Settings.VoiceName == null) Settings.VoiceName = "";

            if (Plans == null) Plans = new List<PlanProfile>();
            if (Plans.Count == 0)
            {
                Plans.Add(MakeProfile(DefaultPlanName, CreateDefaultDailyItems()));
                Plans.Add(MakeProfile(RestPlanName, CreateDefaultDailyItems()));
            }

            List<string> seen = new List<string>();
            bool hasDefault = false;
            for (int i = 0; i < Plans.Count; i++)
            {
                PlanProfile p = Plans[i];
                if (p == null)
                {
                    p = MakeProfile("计划" + (i + 1), new List<PlanItem>());
                    Plans[i] = p;
                }
                p.Name = (p.Name == null ? "" : p.Name.Trim());
                if (p.Name.Length == 0) p.Name = "计划" + (i + 1);
                string original = p.Name;
                int suffix = 2;
                while (seen.Contains(p.Name))
                {
                    p.Name = original + " (" + suffix + ")";
                    suffix++;
                }
                seen.Add(p.Name);
                if (string.Equals(p.Name, DefaultPlanName, StringComparison.Ordinal)) hasDefault = true;
                if (p.Items == null) p.Items = new List<PlanItem>();
                foreach (PlanItem it in p.Items)
                {
                    if (it == null) continue;
                    it.Title = (it.Title == null ? "" : it.Title.Trim());
                    if (it.Title.Length == 0) it.Title = "未命名事项";
                    TimeSpan t;
                    if (!TimeUtil.TryParse(it.Start, out t)) it.Start = "08:00";
                    it.Start = TimeUtil.Format(t);
                    if (it.End != null && it.End.Trim().Length > 0 && TimeUtil.TryParse(it.End, out t))
                        it.End = TimeUtil.Format(t);
                    else
                        it.End = "";
                }
                p.Items.Sort(PlanItemComparer.Instance);
            }

            if (!hasDefault)
            {
                Plans.Insert(0, MakeProfile(DefaultPlanName, CreateDefaultDailyItems()));
            }

            if (TodayPlan == null) TodayPlan = new TodaySelection();
            if (TodayPlan.PlanName == null) TodayPlan.PlanName = DefaultPlanName;
            if (TodayPlan.Date == null) TodayPlan.Date = "";
            if (TodayPlan.Date != DateTime.Today.ToString("yyyy-MM-dd"))
            {
                TodayPlan.Date = DateTime.Today.ToString("yyyy-MM-dd");
                TodayPlan.PlanName = DefaultPlanName;
            }
            if (GetPlan(TodayPlan.PlanName) == null) TodayPlan.PlanName = DefaultPlanName;
        }

        private static PlanProfile MakeProfile(string name, List<PlanItem> items)
        {
            PlanProfile p = new PlanProfile();
            p.Name = name;
            p.Items = items;
            return p;
        }

        public static string Serialize(AppConfig config)
        {
            XmlSerializer ser = new XmlSerializer(typeof(AppConfig));
            XmlWriterSettings ws = new XmlWriterSettings();
            ws.Indent = true;
            ws.Encoding = new UTF8Encoding(false);
            ws.NewLineChars = "\r\n";
            using (Utf8StringWriter sw = new Utf8StringWriter())
            {
                using (System.Xml.XmlWriter xw = System.Xml.XmlWriter.Create(sw, ws))
                {
                    ser.Serialize(xw, config);
                }
                return sw.ToString();
            }
        }

        public static AppConfig Deserialize(string xml)
        {
            XmlSerializer ser = new XmlSerializer(typeof(AppConfig));
            using (StringReader sr = new StringReader(xml))
            {
                return (AppConfig)ser.Deserialize(sr);
            }
        }

        public AppConfig Clone()
        {
            string xml = Serialize(this);
            return Deserialize(xml);
        }

        public static AppConfig Load(string path, out bool createdNew)
        {
            createdNew = false;
            if (!File.Exists(path))
            {
                AppConfig c = CreateDefault();
                c.Normalize();
                try { c.Save(path); createdNew = true; } catch { }
                return c;
            }
            try
            {
                AppConfig loaded = Deserialize(File.ReadAllText(path, Encoding.UTF8));
                if (loaded == null) throw new InvalidDataException("配置文件为空");
                loaded.Normalize();
                return loaded;
            }
            catch (Exception ex)
            {
                Log.Write("[警告] 读取配置失败，已使用默认配置：" + ex.Message);
                try
                {
                    string bad = Path.Combine(Path.GetDirectoryName(path), "config.bad.xml");
                    File.Copy(path, bad, true);
                }
                catch { }
                AppConfig c = CreateDefault();
                c.Normalize();
                try { c.Save(path); } catch { }
                return c;
            }
        }

        public void Save(string path)
        {
            string xml = Serialize(this);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, xml, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch
                {
                    try { File.Copy(tmp, path, true); } finally { try { File.Delete(tmp); } catch { } }
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }

    public class AppSettings
    {
        public string SoundFile { get; set; }
        public bool LoopSound { get; set; }
        public int RingSeconds { get; set; }
        public bool SpeakEnabled { get; set; }
        public string VoiceName { get; set; }
        public bool RemindAtEnd { get; set; }
        public bool AutoStart { get; set; }
        public bool ShowCountdownBar { get; set; }
        public int BarX { get; set; }
        public int BarY { get; set; }
        public int GraceSeconds { get; set; }

        public AppSettings()
        {
            SoundFile = "";
            LoopSound = true;
            RingSeconds = 30;
            SpeakEnabled = true;
            VoiceName = "";
            RemindAtEnd = true;
            AutoStart = false;
            ShowCountdownBar = true;
            BarX = -1;
            BarY = -1;
            GraceSeconds = 120;
        }
    }

    public class PlanProfile
    {
        public string Name { get; set; }

        [XmlArray("Items")]
        [XmlArrayItem("Item")]
        public List<PlanItem> Items { get; set; }

        public PlanProfile()
        {
            Name = "";
            Items = new List<PlanItem>();
        }

        public PlanProfile CloneProfile()
        {
            PlanProfile p = new PlanProfile();
            p.Name = Name;
            p.Items = new List<PlanItem>();
            if (Items != null)
            {
                foreach (PlanItem it in Items) p.Items.Add(it.CloneItem());
            }
            return p;
        }
    }

    public class PlanItem
    {
        public bool Enabled { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public string Title { get; set; }

        public PlanItem()
        {
            Enabled = true;
            Start = "08:00";
            End = "09:00";
            Title = "学习";
        }

        public PlanItem CloneItem()
        {
            PlanItem it = new PlanItem();
            it.Enabled = Enabled;
            it.Start = Start;
            it.End = End;
            it.Title = Title;
            return it;
        }

        public bool TryGetTimes(out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;
            if (!TimeUtil.TryParse(Start, out start)) return false;
            bool hasEnd = !string.IsNullOrEmpty(End) && TimeUtil.TryParse(End, out end);
            if (!hasEnd) end = TimeSpan.Zero;
            return true;
        }
    }

    public class TodaySelection
    {
        public string Date { get; set; }
        public string PlanName { get; set; }

        public TodaySelection()
        {
            Date = "";
            PlanName = AppConfig.DefaultPlanName;
        }
    }

    public class PlanItemComparer : IComparer<PlanItem>
    {
        public static readonly PlanItemComparer Instance = new PlanItemComparer();

        public int Compare(PlanItem x, PlanItem y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;
            if (y == null) return -1;
            TimeSpan sx, ex, sy, ey;
            bool okx = x.TryGetTimes(out sx, out ex);
            bool oky = y.TryGetTimes(out sy, out ey);
            if (!okx && !oky) return string.Compare(x.Title, y.Title, StringComparison.Ordinal);
            if (!okx) return 1;
            if (!oky) return -1;
            int c = sx.CompareTo(sy);
            if (c != 0) return c;
            return string.Compare(x.Title, y.Title, StringComparison.Ordinal);
        }
    }

    public static class TimeUtil
    {
        public static bool TryParse(string text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (text == null) return false;
            string s = text.Trim().Replace('：', ':').Replace(" ", "");
            if (s.Length == 0) return false;
            string[] parts = s.Split(':');
            int hour;
            int minute = 0;
            if (parts.Length > 2) return false;
            if (!int.TryParse(parts[0], out hour)) return false;
            if (parts.Length == 2 && !int.TryParse(parts[1], out minute)) return false;
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return false;
            value = new TimeSpan(hour, minute, 0);
            return true;
        }

        public static string Format(TimeSpan t)
        {
            return t.Hours.ToString("00") + ":" + t.Minutes.ToString("00");
        }

        public static string FormatRange(PlanItem item)
        {
            if (item == null) return "";
            TimeSpan s, e;
            if (!item.TryGetTimes(out s, out e)) return "";
            string start = Format(s);
            if (string.IsNullOrEmpty(item.End)) return start;
            return start + " – " + Format(e);
        }

        public static string FormatClock(DateTime t)
        {
            return t.Hour.ToString("00") + ":" + t.Minute.ToString("00");
        }

        /// <summary>倒计时：01:23 / 1:02:03</summary>
        public static string FormatCountdown(TimeSpan d)
        {
            if (d.TotalSeconds < 0) d = TimeSpan.Zero;
            if (d.TotalHours >= 1)
                return ((int)d.TotalHours).ToString() + ":" + d.Minutes.ToString("00") + ":" + d.Seconds.ToString("00");
            return d.Minutes.ToString("00") + ":" + d.Seconds.ToString("00");
        }

        /// <summary>口语化剩余：还有 1 小时 5 分钟</summary>
        public static string FormatApprox(TimeSpan d)
        {
            if (d.TotalSeconds <= 0) return "马上开始";
            if (d.TotalSeconds < 60) return "还有不到 1 分钟";
            int minutes = (int)Math.Ceiling(d.TotalMinutes);
            if (minutes < 60) return "还有 " + minutes + " 分钟";
            int hours = minutes / 60;
            int mins = minutes % 60;
            if (mins == 0) return "还有 " + hours + " 小时";
            return "还有 " + hours + " 小时 " + mins + " 分钟";
        }

        public static string ChineseTime(DateTime t)
        {
            return t.Hour + " 点 " + t.Minute.ToString("00") + " 分";
        }
    }
}