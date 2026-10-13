using System;

namespace OfficeAttendance
{
    internal enum PaceStatus
    {
        Early,
        OnTrack,
        Behind,
        Short,
        Met
    }

    /// <summary>
    /// A quarter's target is 39 office days, which is 3 days a week across a 13-week quarter.
    /// </summary>
    internal sealed class Pace
    {
        public const int TargetDays = 39;
        public const int DaysPerWeek = 3;

        public PaceStatus Status;
        public int OfficeDays;
        public int AtThreePerWeek;
        public int RemainingWeekdays;
        public bool Closed;
        public string StatusLine = "";
        public string Notice;

        public string ShortLabel
        {
            get
            {
                if (Closed) return Status == PaceStatus.Met ? "Target met" : "Ended at " + OfficeDays;
                switch (Status)
                {
                    case PaceStatus.Met: return "You can skip";
                    case PaceStatus.OnTrack: return "On track";
                    case PaceStatus.Behind: return "May miss 39";
                    case PaceStatus.Short: return "Short of 39";
                    default: return "Not started";
                }
            }
        }

        public static DateTime MondayOf(DateTime date)
        {
            return date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        }

        public static int DaysThisWeek(AttendanceStore store, DateTime today)
        {
            DateTime monday = MondayOf(today);
            return store.InRange(monday, monday.AddDays(6)).Count;
        }

        /// <summary>Weekdays after today, through Friday. Zero once the work week is over.</summary>
        public static int WeekdaysAfter(DateTime today)
        {
            DateTime tomorrow = today.Date.AddDays(1);
            DateTime friday = MondayOf(today).AddDays(4);
            if (tomorrow > friday) return 0;
            return WorkDays.Count(tomorrow, friday);
        }

        /// <summary>"2 of 3 this week", plus what's left, or that the rest of the week can be skipped.</summary>
        public static string WeekLine(int weekDays, bool quarterComplete, DateTime today)
        {
            string line = weekDays + " of " + DaysPerWeek + " this week";
            if (weekDays >= DaysPerWeek)
                return !quarterComplete && WeekdaysAfter(today) > 0 ? line + "  \u00B7  you can skip the rest" : line;
            if (quarterComplete) return line;
            if (today.DayOfWeek == DayOfWeek.Saturday || today.DayOfWeek == DayOfWeek.Sunday) return line;
            int left = DaysPerWeek - weekDays;
            int weekdaysLeft = WorkDays.Count(today.Date, MondayOf(today).AddDays(4));
            if (left > weekdaysLeft) return line;
            return line + "  \u00B7  " + DateText.Count(left, "day") + " left this week";
        }

        public static Pace Assess(Quarter quarter, AttendanceStore store, DateTime today)
        {
            today = today.Date;
            int office = store.InRange(quarter.Start, quarter.End).Count;
            var pace = new Pace { OfficeDays = office };

            if (today < quarter.Start)
            {
                pace.Status = PaceStatus.Early;
                pace.StatusLine = "This quarter hasn't started.";
                return pace;
            }

            if (today > quarter.End)
            {
                pace.Closed = true;
                pace.Status = office >= TargetDays ? PaceStatus.Met : PaceStatus.Short;
                pace.StatusLine = "Quarter ended at " + office + " of 39."
                    + (office >= TargetDays ? " Target met." : "");
                return pace;
            }

            if (office >= TargetDays)
            {
                pace.Status = PaceStatus.Met;
                pace.StatusLine = "Target of 39 met — you can skip going to the office.";
                pace.Notice = "You've done " + DateText.Count(office, "office day") + " this quarter. The target is 39, so you can skip going to the office.";
                return pace;
            }

            int cap3 = 0, cap5 = 0;
            for (DateTime week = MondayOf(today); week <= quarter.End; week = week.AddDays(7))
            {
                DateTime from = week < today ? today : week;
                if (from < quarter.Start) from = quarter.Start;
                DateTime to = week.AddDays(6);
                if (to > quarter.End) to = quarter.End;
                if (from > to) continue;

                int weekdays = WorkDays.Count(from, to);
                if (weekdays == 0) continue;

                DateTime weekStart = week < quarter.Start ? quarter.Start : week;
                int already = from <= weekStart ? 0 : store.InRange(weekStart, from.AddDays(-1)).Count;
                cap3 += Math.Min(Math.Max(0, DaysPerWeek - already), weekdays);
                cap5 += weekdays;
            }

            pace.AtThreePerWeek = office + cap3;
            pace.RemainingWeekdays = cap5;
            int needed = TargetDays - office;

            if (cap5 < needed)
            {
                pace.Status = PaceStatus.Short;
                pace.StatusLine = "You may not reach 39. You still need " + DateText.Count(needed, "day")
                    + " and only " + DateText.Count(cap5, "weekday") + " are left.";
                pace.Notice = "You may not reach 39 office days this quarter. You have " + office
                    + " so far and need " + needed + " more, but only " + cap5 + " weekdays are left.";
            }
            else if (cap3 < needed)
            {
                pace.Status = PaceStatus.Behind;
                pace.StatusLine = "At 3 days a week you may not reach 39. You have " + office
                    + ", and that pace gets you to about " + pace.AtThreePerWeek + ".";
                pace.Notice = "You missed some days and may not reach 39 office days this quarter. You have "
                    + office + " of 39. Staying at 3 days a week from here gets you to about "
                    + pace.AtThreePerWeek + ", so you'd need to come in more often.";
            }
            else
            {
                pace.Status = PaceStatus.OnTrack;
                pace.StatusLine = DateText.Count(needed, "day") + " to go for the 39-day target, at 3 days a week.";
            }
            return pace;
        }
    }
}
