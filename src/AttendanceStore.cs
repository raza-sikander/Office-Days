using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OfficeAttendance
{
    internal sealed class AttendanceRecord
    {
        public DateTime Date;
        public DateTime? FirstSeen;
        public string Network = "";
        public string Source = "";
    }

    internal sealed class AttendanceStore
    {
        public const string SourceAuto = "Auto";
        public const string SourceHistory = "Wi-Fi history";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly string path;
        readonly SortedDictionary<DateTime, AttendanceRecord> records = new SortedDictionary<DateTime, AttendanceRecord>();

        public AttendanceStore(string path)
        {
            this.path = path;
        }

        public static string DefaultPath
        {
            get { return Path.Combine(AppSettings.DataFolder, "attendance.csv"); }
        }

        public string FilePath
        {
            get { return path; }
        }

        public int Count
        {
            get { return records.Count; }
        }

        public IEnumerable<AttendanceRecord> Records
        {
            get { return records.Values; }
        }

        public AttendanceRecord Get(DateTime date)
        {
            AttendanceRecord record;
            return records.TryGetValue(date.Date, out record) ? record : null;
        }

        public bool Contains(DateTime date)
        {
            return records.ContainsKey(date.Date);
        }

        public List<AttendanceRecord> InRange(DateTime from, DateTime to)
        {
            return records.Values.Where(r => r.Date >= from.Date && r.Date <= to.Date).ToList();
        }

        /// <summary>Records that the office network is visible right now. Returns true for the first sighting of the day.</summary>
        public bool MarkPresent(DateTime now, string network, DateTime? connectedSince)
        {
            AttendanceRecord record = Get(now);
            bool isNew = record == null;
            if (isNew)
            {
                record = new AttendanceRecord { Date = now.Date, Network = network, Source = SourceAuto };
                records[record.Date] = record;
            }
            DateTime first = connectedSince.HasValue && connectedSince.Value.Date == now.Date && connectedSince.Value < now
                ? connectedSince.Value
                : now;
            if (!record.FirstSeen.HasValue || first < record.FirstSeen.Value) record.FirstSeen = first;
            if (string.IsNullOrEmpty(record.Network)) record.Network = network;
            return isNew;
        }

        /// <summary>Adds days found elsewhere (e.g. Windows Wi-Fi history). Returns how many new days were added.</summary>
        public int Merge(IEnumerable<AttendanceRecord> found)
        {
            int added = 0;
            foreach (AttendanceRecord f in found)
            {
                AttendanceRecord record = Get(f.Date);
                if (record == null)
                {
                    records[f.Date.Date] = f;
                    added++;
                    continue;
                }
                if (f.FirstSeen.HasValue && (!record.FirstSeen.HasValue || f.FirstSeen.Value < record.FirstSeen.Value)) record.FirstSeen = f.FirstSeen;
                if (string.IsNullOrEmpty(record.Network)) record.Network = f.Network;
            }
            return added;
        }

        public static AttendanceStore Load(string path)
        {
            var store = new AttendanceStore(path);
            if (!File.Exists(path)) return store;
            bool legacy = false;
            bool knowFormat = false;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                List<string> fields = Csv.Split(line);
                if (fields.Count == 0) continue;
                if (fields[0].Trim().Equals("Date", StringComparison.OrdinalIgnoreCase))
                {
                    legacy = line.IndexOf("LastSeen", StringComparison.OrdinalIgnoreCase) >= 0;
                    knowFormat = true;
                    continue;
                }
                DateTime date;
                if (!DateTime.TryParseExact(fields[0].Trim(), "yyyy-MM-dd", Inv, DateTimeStyles.None, out date)) continue;
                if (!knowFormat)
                {
                    legacy = fields.Count >= 5 && ParseTime(date, fields, 2).HasValue;
                    knowFormat = true;
                }
                int networkAt = legacy ? 3 : 2;
                int sourceAt = legacy ? 4 : 3;
                store.records[date] = new AttendanceRecord
                {
                    Date = date,
                    FirstSeen = ParseTime(date, fields, 1),
                    Network = fields.Count > networkAt ? fields[networkAt] : "",
                    Source = fields.Count > sourceAt ? fields[sourceAt] : ""
                };
            }
            return store;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Date,FirstSeen,Network,Source");
            foreach (AttendanceRecord r in records.Values)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    r.Date.ToString("yyyy-MM-dd", Inv),
                    r.FirstSeen.HasValue ? r.FirstSeen.Value.ToString("HH:mm", Inv) : "",
                    Csv.Escape(r.Network),
                    Csv.Escape(r.Source)
                }));
            }
            FileUtil.WriteAllTextAtomic(path, sb.ToString());
        }

        public static void ExportCsv(string file, IEnumerable<AttendanceRecord> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Date,Day,Arrived,Network,Source");
            foreach (AttendanceRecord r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    r.Date.ToString("yyyy-MM-dd", Inv),
                    r.Date.ToString("dddd", Inv),
                    r.FirstSeen.HasValue ? r.FirstSeen.Value.ToString("HH:mm", Inv) : "",
                    Csv.Escape(r.Network),
                    Csv.Escape(r.Source)
                }));
            }
            FileUtil.WriteAllTextAtomic(file, sb.ToString());
        }

        static DateTime? ParseTime(DateTime date, List<string> fields, int index)
        {
            TimeSpan time;
            if (fields.Count > index && TimeSpan.TryParseExact(fields[index].Trim(), @"hh\:mm", Inv, out time)) return date + time;
            return null;
        }
    }

    internal static class Csv
    {
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static List<string> Split(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') current.Append(c);
                    else if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Length = 0; }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
