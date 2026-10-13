using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeAttendance
{
    internal struct Quarter : IEquatable<Quarter>
    {
        public readonly DateTime Start;
        public readonly int Number;
        readonly int fiscalStartMonth;

        Quarter(DateTime start, int number, int fiscalStartMonth)
        {
            Start = start;
            Number = number;
            this.fiscalStartMonth = fiscalStartMonth;
        }

        public static Quarter For(DateTime date, int fiscalStartMonth)
        {
            var fiscalStart = new DateTime(date.Month >= fiscalStartMonth ? date.Year : date.Year - 1, fiscalStartMonth, 1);
            int number = (date.Month - fiscalStartMonth + 12) % 12 / 3 + 1;
            return new Quarter(fiscalStart.AddMonths((number - 1) * 3), number, fiscalStartMonth);
        }

        /// <summary>Quarters from the earliest recorded day up to today (or the latest recorded day), newest first.</summary>
        public static List<Quarter> UpTo(DateTime today, AttendanceStore store, int fiscalStartMonth)
        {
            Quarter first = For(today, fiscalStartMonth), last = first;
            foreach (AttendanceRecord r in store.Records)
            {
                Quarter q = For(r.Date, fiscalStartMonth);
                if (q.Start < first.Start) first = q;
                if (q.Start > last.Start) last = q;
            }
            var quarters = new List<Quarter>();
            for (Quarter q = last; q.Start >= first.Start; q = q.Previous) quarters.Add(q);
            return quarters;
        }

        public DateTime End
        {
            get { return Start.AddMonths(3).AddDays(-1); }
        }

        public Quarter Previous
        {
            get { return For(Start.AddMonths(-3), fiscalStartMonth); }
        }

        public bool Contains(DateTime date)
        {
            return date.Date >= Start && date.Date <= End;
        }

        public string Name
        {
            get
            {
                if (fiscalStartMonth == 1) return "Q" + Number + " " + Start.Year;
                int firstYear = Start.AddMonths(-3 * (Number - 1)).Year;
                return "Q" + Number + " FY" + firstYear + "-" + ((firstYear + 1) % 100).ToString("00");
            }
        }

        public string Range
        {
            get { return DateText.Range(Start, End); }
        }

        public static string Describe(int fiscalStartMonth)
        {
            var parts = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                var start = new DateTime(2000, fiscalStartMonth, 1).AddMonths(i * 3);
                parts.Add("Q" + (i + 1) + " = " + start.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture)
                    + "\u2013" + start.AddMonths(2).ToString("MMM", System.Globalization.CultureInfo.InvariantCulture));
            }
            return string.Join(", ", parts.ToArray());
        }

        public bool Equals(Quarter other)
        {
            return Start == other.Start;
        }

        public override bool Equals(object obj)
        {
            return obj is Quarter && Equals((Quarter)obj);
        }

        public override int GetHashCode()
        {
            return Start.GetHashCode();
        }

        public override string ToString()
        {
            return Name;
        }
    }

    internal static class WorkDays
    {
        /// <summary>Monday to Friday between the two dates, inclusive. Public holidays are not known, so they count too.</summary>
        public static int Count(DateTime from, DateTime to)
        {
            int count = 0;
            for (DateTime d = from.Date; d <= to.Date; d = d.AddDays(1))
                if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) count++;
            return count;
        }

        public static string Percent(int part, int whole)
        {
            return whole > 0 ? Math.Round(100.0 * part / whole) + "%" : "\u2013";
        }
    }
}
