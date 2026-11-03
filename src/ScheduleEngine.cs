using System;
using System.Collections.Generic;

namespace KaoYanReminder
{
    public enum ReminderKind
    {
        Start,
        End,
        Snooze
    }

    public class DueReminder
    {
        public ReminderKind Kind;
        public PlanItem Item;
        public DateTime DueAt;
        public string PlanName;
    }

    public class SnoozeEntry
    {
        public PlanItem Item;
        public DateTime DueAt;
        public string PlanName;
    }

    /// <summary>调度核心：根据当前时间判断哪些提醒该触发、当前/下一项是什么。</summary>
    public class ScheduleEngine
    {
        private AppConfig config;
        private readonly HashSet<string> fired = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<SnoozeEntry> snoozes = new List<SnoozeEntry>();
        private DateTime stateDate = DateTime.MinValue;

        public DateTime? PausedUntil { get; set; }

        public ScheduleEngine(AppConfig config)
        {
            this.config = config;
        }

        public AppConfig Config
        {
            get { return config; }
            set { config = value; }
        }

        public PlanProfile TodayProfile
        {
            get { return config == null ? null : config.GetTodayProfile(); }
        }

        public string ActivePlanName
        {
            get
            {
                PlanProfile p = TodayProfile;
                return p == null ? AppConfig.DefaultPlanName : p.Name;
            }
        }

        public bool IsPaused(DateTime now)
        {
            return PausedUntil.HasValue && now < PausedUntil.Value;
        }

        public void PauseUntil(DateTime until)
        {
            PausedUntil = until;
        }

        public void Resume()
        {
            PausedUntil = null;
        }

        public List<PlanItem> GetDisplayItems()
        {
            List<PlanItem> list = new List<PlanItem>();
            PlanProfile p = TodayProfile;
            if (p == null || p.Items == null) return list;
            foreach (PlanItem it in p.Items) if (it != null) list.Add(it);
            list.Sort(PlanItemComparer.Instance);
            return list;
        }

        /// <summary>当前正在进行且有结束时间的任务。</summary>
        public PlanItem GetCurrent(DateTime now)
        {
            PlanProfile p = TodayProfile;
            if (p == null || p.Items == null) return null;
            PlanItem best = null;
            DateTime bestEnd = DateTime.MinValue;
            foreach (PlanItem it in p.Items)
            {
                if (it == null || !it.Enabled) continue;
                TimeSpan s, e;
                if (!it.TryGetTimes(out s, out e)) continue;
                if (string.IsNullOrEmpty(it.End)) continue;
                DateTime startAt = now.Date + s;
                DateTime endAt = now.Date + e;
                if (endAt <= startAt) continue;
                if (now >= startAt && now < endAt)
                {
                    if (best == null || endAt < bestEnd)
                    {
                        best = it;
                        bestEnd = endAt;
                    }
                }
            }
            return best;
        }

        /// <summary>下一项（今天还没开始的），如果没有则返回明天第一项。</summary>
        public PlanItem GetNext(DateTime now, out DateTime when)
        {
            when = DateTime.MinValue;
            PlanProfile p = TodayProfile;
            if (p != null && p.Items != null)
            {
                PlanItem best = null;
                DateTime bestWhen = DateTime.MaxValue;
                foreach (PlanItem it in p.Items)
                {
                    if (it == null || !it.Enabled) continue;
                    TimeSpan s, e;
                    if (!it.TryGetTimes(out s, out e)) continue;
                    DateTime startAt = now.Date + s;
                    if (startAt > now && startAt < bestWhen)
                    {
                        best = it;
                        bestWhen = startAt;
                    }
                }
                if (best != null)
                {
                    when = bestWhen;
                    return best;
                }
            }
            PlanItem tomorrow = GetFirstOfDay(now.Date.AddDays(1));
            if (tomorrow != null)
            {
                TimeSpan s, e;
                tomorrow.TryGetTimes(out s, out e);
                when = now.Date.AddDays(1) + s;
                return tomorrow;
            }
            return null;
        }

        public PlanItem GetFirstOfDay(DateTime date)
        {
            PlanProfile p = TodayProfile;
            if (p == null || p.Items == null) return null;
            PlanItem best = null;
            TimeSpan bestStart = TimeSpan.MaxValue;
            foreach (PlanItem it in p.Items)
            {
                if (it == null || !it.Enabled) continue;
                TimeSpan s, e;
                if (!it.TryGetTimes(out s, out e)) continue;
                if (s < bestStart)
                {
                    best = it;
                    bestStart = s;
                }
            }
            return best;
        }

        public DateTime? GetCurrentEnd(DateTime now)
        {
            PlanItem current = GetCurrent(now);
            if (current == null) return null;
            TimeSpan s, e;
            if (!current.TryGetTimes(out s, out e)) return null;
            return now.Date + e;
        }

        /// <summary>收集所有到点且未触发过的提醒。</summary>
        public List<DueReminder> GetDue(DateTime now)
        {
            List<DueReminder> result = new List<DueReminder>();
            EnsureDay(now);
            if (PausedUntil.HasValue && now < PausedUntil.Value) return result;
            if (PausedUntil.HasValue && now >= PausedUntil.Value) PausedUntil = null;

            PlanProfile plan = TodayProfile;
            if (plan != null && plan.Items != null)
            {
                int grace = config.Settings.GraceSeconds;
                if (grace < 10) grace = 10;
                foreach (PlanItem item in plan.Items)
                {
                    if (item == null || !item.Enabled) continue;
                    TimeSpan s, e;
                    if (!item.TryGetTimes(out s, out e)) continue;
                    DateTime startAt = now.Date + s;
                    string startKey = BuildKey(now, "s", item, s);
                    if (!fired.Contains(startKey) && now >= startAt && now < startAt.AddSeconds(grace))
                    {
                        fired.Add(startKey);
                        DueReminder d = new DueReminder();
                        d.Kind = ReminderKind.Start;
                        d.Item = item;
                        d.DueAt = startAt;
                        d.PlanName = plan.Name;
                        result.Add(d);
                    }

                    bool hasEnd = !string.IsNullOrEmpty(item.End);
                    DateTime endAt = hasEnd ? now.Date + e : DateTime.MinValue;
                    if (config.Settings.RemindAtEnd && hasEnd && endAt > startAt)
                    {
                        string endKey = BuildKey(now, "e", item, e);
                        if (!fired.Contains(endKey) && now >= endAt && now < endAt.AddSeconds(grace)
                            && !HasNearbyStart(plan, item, e, 90))
                        {
                            fired.Add(endKey);
                            DueReminder d = new DueReminder();
                            d.Kind = ReminderKind.End;
                            d.Item = item;
                            d.DueAt = endAt;
                            d.PlanName = plan.Name;
                            result.Add(d);
                        }
                    }
                }
            }

            for (int i = snoozes.Count - 1; i >= 0; i--)
            {
                if (now >= snoozes[i].DueAt)
                {
                    DueReminder d = new DueReminder();
                    d.Kind = ReminderKind.Snooze;
                    d.Item = snoozes[i].Item;
                    d.DueAt = now;
                    d.PlanName = snoozes[i].PlanName;
                    result.Add(d);
                    snoozes.RemoveAt(i);
                }
            }

            result.Sort(delegate(DueReminder a, DueReminder b) { return a.DueAt.CompareTo(b.DueAt); });
            return result;
        }

        public void AddSnooze(PlanItem item, DateTime dueAt, string planName)
        {
            if (item == null) return;
            SnoozeEntry e = new SnoozeEntry();
            e.Item = item.CloneItem();
            e.DueAt = dueAt;
            e.PlanName = planName;
            snoozes.Add(e);
        }

        public int SnoozeCount { get { return snoozes.Count; } }

        public void ClearFiredForToday()
        {
            fired.Clear();
            stateDate = DateTime.Today;
        }

        private bool HasNearbyStart(PlanProfile plan, PlanItem self, TimeSpan endTime, int seconds)
        {
            if (plan == null || plan.Items == null) return false;
            TimeSpan window = TimeSpan.FromSeconds(seconds);
            foreach (PlanItem other in plan.Items)
            {
                if (other == null || other == self || !other.Enabled) continue;
                TimeSpan s, e;
                if (!other.TryGetTimes(out s, out e)) continue;
                TimeSpan diff = s - endTime;
                if (diff >= TimeSpan.Zero && diff <= window) return true;
            }
            return false;
        }

        private string BuildKey(DateTime now, string kind, PlanItem item, TimeSpan time)
        {
            string planName = "";
            PlanProfile p = TodayProfile;
            if (p != null) planName = p.Name;
            return now.ToString("yyyyMMdd") + "|" + planName + "|" + kind + "|" + TimeUtil.Format(time) + "|" + (item.Title == null ? "" : item.Title);
        }

        private void EnsureDay(DateTime now)
        {
            if (stateDate != now.Date)
            {
                fired.Clear();
                stateDate = now.Date;
            }
        }
    }
}