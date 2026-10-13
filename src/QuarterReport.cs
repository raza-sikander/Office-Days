using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace OfficeAttendance
{
    internal sealed class PeriodRow
    {
        public string Label;
        public int OfficeDays;
        public int WorkingDays;
        public string Detail = "";

        public string Percent
        {
            get { return WorkDays.Percent(OfficeDays, WorkingDays); }
        }
    }

    internal sealed class QuarterReport
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public Quarter Quarter;
        public List<AttendanceRecord> Days;
        public int WorkingDays;
        public int WorkingDaysSoFar;
        public bool InProgress;
        public readonly List<PeriodRow> Months = new List<PeriodRow>();
        public readonly List<PeriodRow> Weeks = new List<PeriodRow>();
        public Pace TargetPace;

        public int OfficeDays
        {
            get { return Days.Count; }
        }

        public string Percent
        {
            get { return WorkDays.Percent(OfficeDays, WorkingDaysSoFar); }
        }

        public string PerWeek
        {
            get { return WorkingDaysSoFar > 0 ? (OfficeDays * 5.0 / WorkingDaysSoFar).ToString("0.0", Inv) : "\u2013"; }
        }

        public string Summary
        {
            get
            {
                string pace = TargetPace == null ? "" : TargetPace.StatusLine + Environment.NewLine;
                if (WorkingDaysSoFar == 0 && OfficeDays == 0) return pace + "This quarter hasn't started yet.";
                string text = DateText.Count(OfficeDays, "office day") + " of " + Pace.TargetDays
                    + " \u00B7 " + DateText.Count(WorkingDaysSoFar, "working day") + (InProgress ? " so far" : "")
                    + " (" + Percent + ") \u00B7 " + PerWeek + " days a week on average";
                if (InProgress)
                    text += Environment.NewLine + DateText.Count(WorkingDays - WorkingDaysSoFar, "working day")
                        + " left in the quarter (" + WorkingDays + " in total).";
                return pace + text;
            }
        }

        public static QuarterReport Build(Quarter quarter, AttendanceStore store, DateTime today)
        {
            today = today.Date;
            DateTime end = quarter.End;
            DateTime cutoff = today < end ? today : end;
            var report = new QuarterReport
            {
                Quarter = quarter,
                Days = store.InRange(quarter.Start, end),
                WorkingDays = WorkDays.Count(quarter.Start, end),
                WorkingDaysSoFar = WorkDays.Count(quarter.Start, cutoff),
                InProgress = quarter.Contains(today),
                TargetPace = Pace.Assess(quarter, store, today)
            };

            for (DateTime month = quarter.Start; month <= end && month <= today; month = month.AddMonths(1))
            {
                DateTime monthEnd = month.AddMonths(1).AddDays(-1);
                PeriodRow row = report.Row(month.ToString("MMMM yyyy", Inv), month, monthEnd, cutoff);
                row.Detail = string.Join(", ", report.DaysBetween(month, monthEnd).Select(d => d.Date.Day.ToString(Inv)).ToArray());
                report.Months.Add(row);
            }

            DateTime monday = quarter.Start.AddDays(-(((int)quarter.Start.DayOfWeek + 6) % 7));
            for (DateTime week = monday; week <= end && week <= today; week = week.AddDays(7))
            {
                DateTime from = week < quarter.Start ? quarter.Start : week;
                DateTime to = week.AddDays(6) > end ? end : week.AddDays(6);
                PeriodRow row = report.Row(DateText.RangeNoYear(from, to), from, to, cutoff);
                row.Detail = string.Join(", ", report.DaysBetween(from, to).Select(d => d.Date.ToString("ddd", Inv)).ToArray());
                report.Weeks.Add(row);
            }
            return report;
        }

        IEnumerable<AttendanceRecord> DaysBetween(DateTime from, DateTime to)
        {
            return Days.Where(d => d.Date >= from && d.Date <= to);
        }

        PeriodRow Row(string label, DateTime from, DateTime to, DateTime cutoff)
        {
            return new PeriodRow
            {
                Label = label,
                OfficeDays = DaysBetween(from, to).Count(),
                WorkingDays = WorkDays.Count(from, to < cutoff ? to : cutoff)
            };
        }

        public string ToHtml()
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Office Days ").Append(H(Quarter.Name)).Append("</title><style>")
              .Append("body{font-family:'Segoe UI',Arial,sans-serif;margin:32px;color:#222}h1{margin:0 0 4px}h2{margin:28px 0 6px;font-size:18px}")
              .Append(".muted{color:#666}.cards{display:flex;gap:14px;margin:20px 0;flex-wrap:wrap}")
              .Append(".card{border:1px solid #ddd;border-radius:8px;padding:10px 18px;min-width:140px}.big{font-size:28px;font-weight:600;color:#2e7d32}")
              .Append("table{border-collapse:collapse;min-width:480px}th,td{border-bottom:1px solid #e5e5e5;padding:6px 14px;text-align:left}")
              .Append("th{background:#f4f4f4}td.n{text-align:right}</style></head><body>");
            sb.Append("<h1>Office Days \u2013 ").Append(H(Quarter.Name)).Append("</h1><div class=\"muted\">").Append(H(Quarter.Range))
              .Append(InProgress ? " \u00B7 in progress" : "").Append("</div><div class=\"cards\">");
            Card(sb, OfficeDays + " of " + OfficeAttendance.Pace.TargetDays, "This quarter");
            Card(sb, TargetPace.ShortLabel, "Pace");
            Card(sb, WorkingDaysSoFar + (InProgress ? " of " + WorkingDays : ""), InProgress ? "Working days so far" : "Working days");
            Card(sb, PerWeek, "Days a week (average)");
            sb.Append("</div><p><strong>").Append(H(TargetPace.StatusLine)).Append("</strong></p>");

            Table(sb, "By month", new[] { "Month", "Office days", "Working days", "Attendance", "Dates" }, Months);
            Table(sb, "By week", new[] { "Week", "Office days", "Working days", "Attendance", "Days" }, Weeks);

            sb.Append("<h2>Office days</h2><table><tr><th>Date</th><th>Arrived</th><th>Recorded by</th></tr>");
            foreach (AttendanceRecord d in Days)
                sb.Append("<tr><td>").Append(H(DateText.Day(d.Date))).Append("</td><td>").Append(H(DateText.Time(d.FirstSeen)))
                  .Append("</td><td>").Append(H(d.Source)).Append("</td></tr>");
            if (Days.Count == 0) sb.Append("<tr><td colspan=\"3\" class=\"muted\">No office days recorded.</td></tr>");
            sb.Append("</table><p class=\"muted\">Working days are Monday to Friday; public holidays and leave are not excluded. Generated ")
              .Append(H(DateText.Day(DateTime.Now))).Append(" ").Append(H(DateText.Time(DateTime.Now))).Append(".</p></body></html>");
            return sb.ToString();
        }

        static void Card(StringBuilder sb, string value, string label)
        {
            sb.Append("<div class=\"card\"><div class=\"big\">").Append(H(value)).Append("</div><div class=\"muted\">").Append(H(label)).Append("</div></div>");
        }

        static void Table(StringBuilder sb, string title, string[] headers, List<PeriodRow> rows)
        {
            sb.Append("<h2>").Append(H(title)).Append("</h2><table><tr>");
            foreach (string h in headers) sb.Append("<th>").Append(H(h)).Append("</th>");
            sb.Append("</tr>");
            foreach (PeriodRow r in rows)
                sb.Append("<tr><td>").Append(H(r.Label)).Append("</td><td class=\"n\">").Append(r.OfficeDays).Append("</td><td class=\"n\">")
                  .Append(r.WorkingDays).Append("</td><td class=\"n\">").Append(H(r.Percent)).Append("</td><td>").Append(H(r.Detail)).Append("</td></tr>");
            sb.Append("</table>");
        }

        static string H(string text)
        {
            return WebUtility.HtmlEncode(text ?? "");
        }
    }
}
