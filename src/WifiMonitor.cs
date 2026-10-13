using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OfficeAttendance
{
    internal sealed class WifiConnection
    {
        public string Ssid = "";
        public string Profile = "";

        public string Name
        {
            get { return Ssid.Length > 0 ? Ssid : Profile; }
        }
    }

    internal sealed class WifiStatus
    {
        public readonly List<WifiConnection> Connections = new List<WifiConnection>();
        public readonly DateTime CheckedAt = DateTime.Now;
        public bool HasAdapter;
        public bool UsedEventLog;
        public string Error;

        public string Describe()
        {
            if (Connections.Count > 0) return string.Join(", ", Connections.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            if (Error != null) return "unknown (" + Error + ")";
            return HasAdapter ? "not connected" : "no Wi-Fi adapter found";
        }

        public string FindOfficeNetwork(AppSettings settings)
        {
            foreach (WifiConnection c in Connections)
                if (settings.IsOfficeNetwork(c.Ssid) || settings.IsOfficeNetwork(c.Profile)) return c.Name;
            return null;
        }
    }

    internal sealed class WifiEvent
    {
        public DateTime Time;
        public bool Connected;
        public string Ssid = "";
        public string Profile = "";
        public Guid Interface;

        public bool IsOffice(AppSettings settings)
        {
            return settings.IsOfficeNetwork(Ssid) || settings.IsOfficeNetwork(Profile);
        }
    }

    internal static class WifiMonitor
    {
        const string LogName = "Microsoft-Windows-WLAN-AutoConfig/Operational";
        const string ConnectEventsXPath = "*[System[(EventID=8001 or EventID=8003)]]";
        const int EventConnected = 8001;
        const int StateConnected = 1;
        const int OpcodeCurrentConnection = 7;
        const int InterfaceInfoSize = 532;
        const int ProfileInfoSize = 516;

        static readonly string[] EventFields =
        {
            "Event/EventData/Data[@Name='SSID']",
            "Event/EventData/Data[@Name='ProfileName']",
            "Event/EventData/Data[@Name='InterfaceGuid']"
        };

        public static WifiStatus GetStatus()
        {
            var status = new WifiStatus();
            var unresolved = new List<Guid>();
            IntPtr client = IntPtr.Zero, list = IntPtr.Zero;
            try
            {
                uint version;
                uint rc = WlanOpenHandle(2, IntPtr.Zero, out version, out client);
                if (rc != 0) throw new Win32Exception((int)rc);
                rc = WlanEnumInterfaces(client, IntPtr.Zero, out list);
                if (rc != 0) throw new Win32Exception((int)rc);

                int count = Marshal.ReadInt32(list, 0);
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = Offset(list, 8 + i * InterfaceInfoSize);
                    status.HasAdapter = true;
                    if (Marshal.ReadInt32(item, 528) != StateConnected) continue;
                    Guid id = ReadGuid(item);
                    WifiConnection connection = QueryConnection(client, id);
                    if (connection != null) status.Connections.Add(connection);
                    else unresolved.Add(id);
                }
            }
            catch (Exception ex)
            {
                status.Error = ex.Message;
            }
            finally
            {
                if (list != IntPtr.Zero) WlanFreeMemory(list);
                if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
            }

            // Windows 11 24H2+ hides the network name from desktop apps unless location access is allowed,
            // but the WLAN event log still records it.
            if (unresolved.Count > 0) FillFromEventLog(status, unresolved);
            return status;
        }

        internal static void FillFromEventLog(WifiStatus status, List<Guid> interfaces)
        {
            status.UsedEventLog = true;
            try
            {
                var pending = new List<Guid>(interfaces);
                var query = new EventLogQuery(LogName, PathType.LogName, ConnectEventsXPath) { ReverseDirection = true };
                using (var selector = new EventLogPropertySelector(EventFields))
                using (var reader = new EventLogReader(query))
                {
                    for (int scanned = 0; pending.Count > 0 && scanned < 1000; scanned++)
                    {
                        WifiEvent e = ReadNext(reader, selector);
                        if (e == null) break;
                        Guid match = pending.Contains(e.Interface) ? e.Interface : (e.Interface == Guid.Empty ? pending[0] : Guid.Empty);
                        if (match == Guid.Empty) continue;
                        pending.Remove(match);
                        if (e.Connected) status.Connections.Add(new WifiConnection { Ssid = e.Ssid, Profile = e.Profile });
                    }
                }
            }
            catch (Exception ex)
            {
                status.Error = "Windows is hiding the Wi-Fi name: " + ex.Message;
            }
        }

        /// <summary>Wi-Fi connect (8001) and disconnect (8003) events, oldest first. Windows keeps a few weeks of them.</summary>
        public static List<WifiEvent> ReadHistory(DateTime? since)
        {
            string xpath = ConnectEventsXPath;
            if (since.HasValue)
            {
                string utc = since.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                xpath = "*[System[(EventID=8001 or EventID=8003) and TimeCreated[@SystemTime>='" + utc + "']]]";
            }
            var events = new List<WifiEvent>();
            using (var selector = new EventLogPropertySelector(EventFields))
            using (var reader = new EventLogReader(new EventLogQuery(LogName, PathType.LogName, xpath)))
            {
                WifiEvent e;
                while ((e = ReadNext(reader, selector)) != null) events.Add(e);
            }
            return events;
        }

        public static DateTime? OldestHistory()
        {
            using (var reader = new EventLogReader(new EventLogQuery(LogName, PathType.LogName, "*")))
            using (EventRecord record = reader.ReadEvent())
            {
                return record == null ? (DateTime?)null : record.TimeCreated;
            }
        }

        public static List<AttendanceRecord> FindOfficeDays(List<WifiEvent> events, AppSettings settings)
        {
            var days = new SortedDictionary<DateTime, AttendanceRecord>();
            // Only a connect counts. A disconnect can be logged later, after the laptop wakes up somewhere else.
            foreach (WifiEvent e in events.Where(x => x.Connected && x.IsOffice(settings)))
            {
                AttendanceRecord day;
                if (!days.TryGetValue(e.Time.Date, out day))
                {
                    day = new AttendanceRecord
                    {
                        Date = e.Time.Date,
                        FirstSeen = e.Time,
                        Network = e.Ssid.Length > 0 ? e.Ssid : e.Profile,
                        Source = AttendanceStore.SourceHistory
                    };
                    days[day.Date] = day;
                }
                else if (e.Time < day.FirstSeen.Value)
                {
                    day.FirstSeen = e.Time;
                }
            }
            return days.Values.ToList();
        }

        public static DateTime? FirstOfficeConnectionToday(AppSettings settings)
        {
            try
            {
                DateTime? first = null;
                foreach (WifiEvent e in ReadHistory(DateTime.Today))
                    if (e.Connected && e.Time.Date == DateTime.Today && e.IsOffice(settings) && (!first.HasValue || e.Time < first.Value))
                        first = e.Time;
                return first;
            }
            catch (Exception ex)
            {
                Log.Write("Could not read Wi-Fi history: " + ex.Message);
                return null;
            }
        }

        public static List<string> GetSavedNetworks()
        {
            var names = new List<string>();
            IntPtr client = IntPtr.Zero, list = IntPtr.Zero;
            try
            {
                uint version;
                if (WlanOpenHandle(2, IntPtr.Zero, out version, out client) != 0) return names;
                if (WlanEnumInterfaces(client, IntPtr.Zero, out list) != 0) return names;
                int count = Marshal.ReadInt32(list, 0);
                for (int i = 0; i < count; i++)
                {
                    Guid id = ReadGuid(Offset(list, 8 + i * InterfaceInfoSize));
                    IntPtr profiles;
                    if (WlanGetProfileList(client, ref id, IntPtr.Zero, out profiles) != 0) continue;
                    try
                    {
                        int n = Marshal.ReadInt32(profiles, 0);
                        for (int j = 0; j < n; j++)
                        {
                            string name = Marshal.PtrToStringUni(Offset(profiles, 8 + j * ProfileInfoSize));
                            if (!string.IsNullOrEmpty(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                        }
                    }
                    finally
                    {
                        WlanFreeMemory(profiles);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Could not list saved Wi-Fi networks: " + ex.Message);
            }
            finally
            {
                if (list != IntPtr.Zero) WlanFreeMemory(list);
                if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
            }
            return names;
        }

        static WifiConnection QueryConnection(IntPtr client, Guid id)
        {
            uint size;
            IntPtr data;
            if (WlanQueryInterface(client, ref id, OpcodeCurrentConnection, IntPtr.Zero, out size, out data, IntPtr.Zero) != 0) return null;
            try
            {
                // WLAN_CONNECTION_ATTRIBUTES: profile name (WCHAR[256]) at 8, DOT11_SSID length at 520, bytes at 524.
                var connection = new WifiConnection { Profile = (Marshal.PtrToStringUni(Offset(data, 8)) ?? "").Trim() };
                int length = Math.Max(0, Math.Min(32, Marshal.ReadInt32(data, 520)));
                var ssid = new byte[length];
                Marshal.Copy(Offset(data, 524), ssid, 0, length);
                connection.Ssid = Encoding.UTF8.GetString(ssid).TrimEnd('\0').Trim();
                return connection.Name.Length > 0 ? connection : null;
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }

        static WifiEvent ReadNext(EventLogReader reader, EventLogPropertySelector selector)
        {
            using (EventRecord record = reader.ReadEvent())
            {
                if (record == null) return null;
                IList<object> values = ((EventLogRecord)record).GetPropertyValues(selector);
                return new WifiEvent
                {
                    Time = record.TimeCreated ?? DateTime.MinValue,
                    Connected = record.Id == EventConnected,
                    Ssid = (Convert.ToString(values[0]) ?? "").Trim(),
                    Profile = (Convert.ToString(values[1]) ?? "").Trim(),
                    Interface = ToGuid(values[2])
                };
            }
        }

        static Guid ToGuid(object value)
        {
            if (value is Guid) return (Guid)value;
            Guid id;
            return Guid.TryParse(Convert.ToString(value), out id) ? id : Guid.Empty;
        }

        static IntPtr Offset(IntPtr pointer, int bytes)
        {
            return new IntPtr(pointer.ToInt64() + bytes);
        }

        static Guid ReadGuid(IntPtr pointer)
        {
            var bytes = new byte[16];
            Marshal.Copy(pointer, bytes, 0, 16);
            return new Guid(bytes);
        }

        [DllImport("wlanapi.dll")]
        static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

        [DllImport("wlanapi.dll")]
        static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved,
            out uint dataSize, out IntPtr data, IntPtr opcodeValueType);

        [DllImport("wlanapi.dll")]
        static extern uint WlanGetProfileList(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr reserved, out IntPtr profileList);

        [DllImport("wlanapi.dll")]
        static extern void WlanFreeMemory(IntPtr memory);
    }
}
