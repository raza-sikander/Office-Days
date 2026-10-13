using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OfficeAttendance
{
    internal sealed class AppSettings
    {
        public readonly List<string> OfficeNetworks = new List<string>();
        public int QuarterStartMonth = 1;
        public bool ShowNotifications = true;
        public DateTime PaceNoticeWeek = DateTime.MinValue;
        public DateTime TargetMetQuarter = DateTime.MinValue;
        public DateTime WeekShortNotice = DateTime.MinValue;
        public DateTime WeekSkipNotice = DateTime.MinValue;

        public static string DataFolder
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("OFFICE_ATTENDANCE_DATA");
                if (!string.IsNullOrEmpty(custom)) return custom;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OfficeAttendance");
            }
        }

        static string FilePath
        {
            get { return Path.Combine(DataFolder, "settings.ini"); }
        }

        public bool IsConfigured
        {
            get { return OfficeNetworks.Count > 0; }
        }

        public string NetworksText
        {
            get { return string.Join(", ", OfficeNetworks.ToArray()); }
        }

        public bool IsOfficeNetwork(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return false;
            foreach (string network in OfficeNetworks)
                if (string.Equals(network.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public Quarter QuarterFor(DateTime date)
        {
            return Quarter.For(date, QuarterStartMonth);
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(FilePath)) return settings;
            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith("#")) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                int month;
                DateTime date;
                if (key == "Network" && value.Length > 0) settings.OfficeNetworks.Add(value);
                else if (key == "QuarterStartMonth" && int.TryParse(value, out month) && month >= 1 && month <= 12) settings.QuarterStartMonth = month;
                else if (key == "ShowNotifications") settings.ShowNotifications = value != "0";
                else if (key == "PaceNoticeWeek" && TryDate(value, out date)) settings.PaceNoticeWeek = date;
                else if (key == "TargetMetQuarter" && TryDate(value, out date)) settings.TargetMetQuarter = date;
                else if (key == "WeekShortNotice" && TryDate(value, out date)) settings.WeekShortNotice = date;
                else if (key == "WeekSkipNotice" && TryDate(value, out date)) settings.WeekSkipNotice = date;
            }
            return settings;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Office Days settings");
            foreach (string network in OfficeNetworks) sb.AppendLine("Network=" + network);
            sb.AppendLine("QuarterStartMonth=" + QuarterStartMonth.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("ShowNotifications=" + (ShowNotifications ? "1" : "0"));
            if (PaceNoticeWeek != DateTime.MinValue) sb.AppendLine("PaceNoticeWeek=" + PaceNoticeWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (TargetMetQuarter != DateTime.MinValue) sb.AppendLine("TargetMetQuarter=" + TargetMetQuarter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (WeekShortNotice != DateTime.MinValue) sb.AppendLine("WeekShortNotice=" + WeekShortNotice.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (WeekSkipNotice != DateTime.MinValue) sb.AppendLine("WeekSkipNotice=" + WeekSkipNotice.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            FileUtil.WriteAllTextAtomic(FilePath, sb.ToString());
        }

        static bool TryDate(string value, out DateTime date)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
    }
}
