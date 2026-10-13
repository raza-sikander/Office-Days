using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OfficeAttendance
{
    internal sealed class TrayApp : ApplicationContext
    {
        public readonly AppSettings Settings;
        public readonly AttendanceStore Store;
        public WifiStatus LastStatus { get; private set; }
        public bool PaceNoticeShown { get; private set; }
        public bool WeekNoticeShown { get; private set; }

        readonly Control invoker = new Control();
        NotifyIcon tray;
        System.Windows.Forms.Timer dayTimer, weekTimer, settleTimer;
        Icon trayIcon;
        string trayIconKey;
        MainForm mainForm;
        DateTime lastSaved = DateTime.MinValue;
        DateTime nextCheckAt = DateTime.MinValue;
        bool closeTipShown, dialogOpen, exiting, watching, waitingForMidnight;

        public TrayApp()
        {
            Settings = AppSettings.Load();
            Store = AttendanceStore.Load(AttendanceStore.DefaultPath);
            IntPtr handle = invoker.Handle;
        }

        public void ListenForSignals(WaitHandle show, WaitHandle exit)
        {
            ThreadPool.RegisterWaitForSingleObject(show, (s, t) => invoker.BeginInvoke((MethodInvoker)ShowMainWindow), null, -1, false);
            ThreadPool.RegisterWaitForSingleObject(exit, (s, t) => invoker.BeginInvoke((MethodInvoker)ExitApp), null, -1, true);
        }

        public void Start(bool showWindow)
        {
            tray = new NotifyIcon { Icon = Icons.Fraction(0, Pace.DaysPerWeek, false, SystemInformation.SmallIconSize.Width), Text = "Office Days", ContextMenuStrip = BuildMenu(), Visible = true };
            trayIcon = tray.Icon;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMainWindow(); };
            tray.BalloonTipClicked += (s, e) => ShowMainWindow();

            settleTimer = new System.Windows.Forms.Timer { Interval = 5 * 1000 };
            settleTimer.Tick += (s, e) => { settleTimer.Stop(); if (watching) CheckNow(); };
            dayTimer = new System.Windows.Forms.Timer();
            dayTimer.Tick += (s, e) => OnDayTimer();
            weekTimer = new System.Windows.Forms.Timer();
            weekTimer.Tick += (s, e) => { weekTimer.Stop(); ConsiderWeekNotice(false); };

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.SessionEnding += OnSessionEnding;

            invoker.BeginInvoke((MethodInvoker)delegate
            {
                if (!Settings.IsConfigured)
                {
                    RunFirstTimeSetup();
                    return;
                }
                BeginDay();
                if (showWindow) ShowMainWindow();
            });
        }

        /// <summary>Watches for office Wi-Fi until today is logged, then sleeps until midnight.</summary>
        void BeginDay()
        {
            waitingForMidnight = false;
            RefreshTray();
            if (Settings.IsConfigured && Store.Contains(DateTime.Today))
            {
                FinishToday();
                ConsiderPaceNotice(false);
                ScheduleWeekNotice();
                if (mainForm != null && !mainForm.IsDisposed) mainForm.RefreshAll(false);
                return;
            }
            StartWatching();
            CheckNow();
        }

        void FinishToday()
        {
            StopWatching();
            nextCheckAt = DateTime.Today.AddDays(1);
            ArmDayTimer();
            if (waitingForMidnight) return;
            waitingForMidnight = true;
            Log.Write("Attendance logged for today. Next Wi-Fi check at midnight.");
        }

        void ArmDayTimer()
        {
            if (dayTimer == null || exiting) return;
            double ms = (nextCheckAt - DateTime.Now).TotalMilliseconds;
            if (ms < 1000) ms = 1000;
            if (ms > int.MaxValue) ms = int.MaxValue;
            dayTimer.Stop();
            dayTimer.Interval = (int)ms;
            dayTimer.Start();
        }

        void OnDayTimer()
        {
            if (dayTimer != null) dayTimer.Stop();
            if (exiting) return;
            if (DateTime.Now < nextCheckAt)
            {
                ArmDayTimer();
                return;
            }
            Log.Write("Midnight: watching for office Wi-Fi.");
            BeginDay();
        }

        void StartWatching()
        {
            if (watching || exiting) return;
            watching = true;
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        }

        void StopWatching()
        {
            if (settleTimer != null) settleTimer.Stop();
            if (!watching) return;
            watching = false;
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        }

        /// <summary>Reads the current Wi-Fi and marks today if it is an office network. Returns true when today was newly marked.</summary>
        public bool CheckNow()
        {
            WifiStatus status = WifiMonitor.GetStatus();
            LastStatus = status;
            string office = Settings.IsConfigured ? status.FindOfficeNetwork(Settings) : null;
            bool firstToday = false;
            if (office != null)
            {
                DateTime now = DateTime.Now;
                firstToday = !Store.Contains(now);
                DateTime? since = firstToday ? WifiMonitor.FirstOfficeConnectionToday(Settings) : null;
                Store.MarkPresent(now, office, since);
                if (firstToday || now - lastSaved >= TimeSpan.FromMinutes(5)) SaveStore();
                if (firstToday) Log.Write("Marked present on " + office);
            }
            PaceNoticeShown = ConsiderPaceNotice(firstToday);
            if (firstToday && Settings.ShowNotifications && tray != null && !PaceNoticeShown)
                tray.ShowBalloonTip(6000, "Attendance marked", "You're in the office today, " + DateText.Day(DateTime.Now) + " (connected to "
                    + office + ").\n" + QuarterSoFar(), ToolTipIcon.Info);
            RefreshTray();
            if (mainForm != null && !mainForm.IsDisposed) mainForm.RefreshAll(firstToday);
            if (Settings.IsConfigured && Store.Contains(DateTime.Today)) FinishToday();
            else if (Settings.IsConfigured) StartWatching();
            ScheduleWeekNotice(firstToday);
            return firstToday;
        }

        public bool SaveStore()
        {
            try
            {
                Store.Save();
                lastSaved = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Could not save attendance: " + ex.Message);
                return false;
            }
        }

        public string ImportHistory()
        {
            try
            {
                List<WifiEvent> events = WifiMonitor.ReadHistory(null);
                if (events.Count == 0) return "Windows has no Wi-Fi history to import.";
                List<AttendanceRecord> found = WifiMonitor.FindOfficeDays(events, Settings);
                int added = Store.Merge(found);
                SaveStore();
                ConsiderPaceNotice(false);
                RefreshTray();
                if (Store.Contains(DateTime.Today)) FinishToday();
                else StartWatching();
                ScheduleWeekNotice();
                return "Windows keeps Wi-Fi history back to " + DateText.Long(events[0].Time) + ". It shows "
                    + DateText.Count(found.Count, "office day") + " on " + Settings.NetworksText + "; "
                    + (added == 0 ? "all of them were already recorded." : DateText.Count(added, "new day") + " added.");
            }
            catch (Exception ex)
            {
                Log.Write("History import failed: " + ex);
                return "Couldn't read the Windows Wi-Fi history: " + ex.Message;
            }
        }

        public string QuarterSoFar()
        {
            Quarter quarter = Settings.QuarterFor(DateTime.Today);
            int days = Store.InRange(quarter.Start, quarter.End).Count;
            return quarter.Name + ": " + days + " of " + Pace.TargetDays + " days.";
        }

        /// <summary>
        /// Once a week — on Monday, or the first time the laptop is on after a missed Monday —
        /// warns when 3 days a week will no longer reach 39, and says you can skip once 39 is reached.
        /// Returns true when a notification was shown.
        /// </summary>
        public bool ConsiderPaceNotice(bool arrivedToday)
        {
            if (!Settings.IsConfigured || exiting) return false;
            Quarter quarter = Settings.QuarterFor(DateTime.Today);
            Pace pace = Pace.Assess(quarter, Store, DateTime.Today);
            bool changed = false;
            if (pace.Status != PaceStatus.Met && Settings.TargetMetQuarter != DateTime.MinValue)
            {
                Settings.TargetMetQuarter = DateTime.MinValue;
                changed = true;
            }

            bool concernsTarget = pace.Status == PaceStatus.Met || pace.Status == PaceStatus.Behind || pace.Status == PaceStatus.Short;
            DateTime monday = Pace.MondayOf(DateTime.Today);
            bool alreadyThisWeek = Settings.PaceNoticeWeek == monday;
            bool justMet = pace.Status == PaceStatus.Met && Settings.TargetMetQuarter != quarter.Start;
            bool tooEarly = DateTime.Now.Hour < 7 && !justMet;

            if (!concernsTarget || pace.Notice == null || tooEarly || (alreadyThisWeek && !justMet))
            {
                if (changed) SaveSettings();
                return false;
            }
            if (!Settings.ShowNotifications || tray == null)
            {
                if (changed) SaveSettings();
                return false;
            }

            string body = arrivedToday && pace.Status == PaceStatus.Met ? "You're in the office today. " + pace.Notice : pace.Notice;
            string title = pace.Status == PaceStatus.Met ? "You can skip the office" : "You may not reach 39 days";
            tray.ShowBalloonTip(12000, title, body, pace.Status == PaceStatus.Met ? ToolTipIcon.Info : ToolTipIcon.Warning);
            Settings.PaceNoticeWeek = monday;
            if (pace.Status == PaceStatus.Met) Settings.TargetMetQuarter = quarter.Start;
            SaveSettings();
            return true;
        }

        void SaveSettings()
        {
            try
            {
                Settings.Save();
            }
            catch (Exception ex)
            {
                Log.Write("Could not save settings: " + ex.Message);
            }
        }

        public void ShowMainWindow()
        {
            if (exiting || dialogOpen) return;
            if (!Settings.IsConfigured)
            {
                RunFirstTimeSetup();
                return;
            }
            if (mainForm == null || mainForm.IsDisposed)
            {
                mainForm = new MainForm(this);
                mainForm.FormClosed += (s, e) => { mainForm = null; ShowCloseTip(); };
                mainForm.Show();
            }
            if (mainForm.WindowState == FormWindowState.Minimized) mainForm.WindowState = FormWindowState.Normal;
            mainForm.Activate();
        }

        public void ShowSettings(IWin32Window owner)
        {
            if (dialogOpen) return;
            dialogOpen = true;
            try
            {
                using (var form = new SettingsForm(Settings, false))
                    if (form.ShowDialog(owner) != DialogResult.OK) return;
            }
            finally
            {
                dialogOpen = false;
            }
            CheckNow();
            if (mainForm != null && !mainForm.IsDisposed) mainForm.RefreshAll(true);
        }

        void RunFirstTimeSetup()
        {
            if (dialogOpen) return;
            dialogOpen = true;
            bool import;
            try
            {
                using (var form = new SettingsForm(Settings, true))
                {
                    if (form.ShowDialog() != DialogResult.OK)
                    {
                        ExitApp();
                        return;
                    }
                    import = form.ImportHistoryRequested;
                }
            }
            finally
            {
                dialogOpen = false;
            }

            string message = "You're all set. Office Days runs in the notification area (bottom-right of the taskbar). "
                + "It watches for " + Settings.NetworksText + " until today is marked, then waits until midnight to start the next day.";
            if (import) message += "\n\n" + ImportHistory();
            CheckNow();
            ShowMainWindow();
            MessageBox.Show(mainForm, message, "Office Days", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void RefreshTray()
        {
            if (tray == null) return;
            bool present = Store.Contains(DateTime.Today);
            int weekDays = Pace.DaysThisWeek(Store, DateTime.Today);
            bool filled = present || weekDays >= Pace.DaysPerWeek;
            string key = weekDays + (filled ? "1" : "0");
            if (key != trayIconKey)
            {
                Icon next = Icons.Fraction(weekDays, Pace.DaysPerWeek, filled, SystemInformation.SmallIconSize.Width);
                tray.Icon = next;
                if (trayIcon != null) trayIcon.Dispose();
                trayIcon = next;
                trayIconKey = key;
            }
            Quarter quarter = Settings.QuarterFor(DateTime.Today);
            string text = "Office Days\n" + weekDays + "/" + Pace.DaysPerWeek + " this week\n"
                + (present ? "In office today" : "Not in office today");
            int quarterDays = Store.InRange(quarter.Start, quarter.End).Count;
            string withQuarter = text + "\n" + quarter.Name + ": " + quarterDays + " of " + Pace.TargetDays;
            tray.Text = (withQuarter.Length <= 63 ? withQuarter : text);
        }

        /// <summary>Friday at 4pm, once, if this week is still under 3 days. Stays quiet once the quarter has 39.</summary>
        void ScheduleWeekNotice()
        {
            ScheduleWeekNotice(false);
        }

        void ScheduleWeekNotice(bool arrivedToday)
        {
            WeekNoticeShown = false;
            if (weekTimer == null || exiting || !Settings.IsConfigured) return;
            if (!WeekIsShort())
            {
                weekTimer.Stop();
                ConsiderWeekSkip(arrivedToday);
                return;
            }
            DateTime at = Pace.MondayOf(DateTime.Today).AddDays(4).AddHours(16);
            if (DateTime.Now >= at)
            {
                weekTimer.Stop();
                ConsiderWeekNotice(arrivedToday);
                return;
            }
            double ms = (at - DateTime.Now).TotalMilliseconds;
            if (ms < 1000) ms = 1000;
            if (ms > int.MaxValue) ms = int.MaxValue;
            weekTimer.Stop();
            weekTimer.Interval = (int)ms;
            weekTimer.Start();
        }

        bool WeekIsShort()
        {
            if (QuarterDays() >= Pace.TargetDays) return false;
            if (Pace.DaysThisWeek(Store, DateTime.Today) >= Pace.DaysPerWeek) return false;
            return Settings.WeekShortNotice != Pace.MondayOf(DateTime.Today);
        }

        int QuarterDays()
        {
            Quarter quarter = Settings.QuarterFor(DateTime.Today);
            return Store.InRange(quarter.Start, quarter.End).Count;
        }

        void ConsiderWeekNotice(bool arrivedToday)
        {
            WeekNoticeShown = false;
            if (!Settings.IsConfigured || exiting || !WeekIsShort()) return;
            if (DateTime.Now < Pace.MondayOf(DateTime.Today).AddDays(4).AddHours(16)) return;
            if (!Settings.ShowNotifications || tray == null) return;
            int days = Pace.DaysThisWeek(Store, DateTime.Today);
            string body = "You have " + days + " of " + Pace.DaysPerWeek + " office days this week.";
            if (arrivedToday) body = "You're in the office today. " + body;
            tray.ShowBalloonTip(12000, "This week is not complete", body, ToolTipIcon.Warning);
            Settings.WeekShortNotice = Pace.MondayOf(DateTime.Today);
            SaveSettings();
            WeekNoticeShown = true;
        }

        /// <summary>Once the week reaches 3, one notice that the remaining days can be skipped. Quiet once the quarter has 39.</summary>
        void ConsiderWeekSkip(bool arrivedToday)
        {
            if (!Settings.IsConfigured || exiting) return;
            if (QuarterDays() >= Pace.TargetDays) return;
            int days = Pace.DaysThisWeek(Store, DateTime.Today);
            if (days < Pace.DaysPerWeek || Pace.WeekdaysAfter(DateTime.Today) <= 0) return;
            DateTime monday = Pace.MondayOf(DateTime.Today);
            if (Settings.WeekSkipNotice == monday) return;
            if (!Settings.ShowNotifications || tray == null) return;
            string body = days + " of " + Pace.DaysPerWeek + " this week, you can skip the remaining days.";
            if (arrivedToday) body = "You're in the office today. " + body;
            tray.ShowBalloonTip(12000, "You can skip the rest of this week", body, ToolTipIcon.Info);
            Settings.WeekSkipNotice = monday;
            SaveSettings();
            WeekNoticeShown = true;
        }

        void ShowCheckResult()
        {
            if (PaceNoticeShown || WeekNoticeShown) return;
            WifiStatus status = LastStatus;
            string office = status.FindOfficeNetwork(Settings);
            string text = office != null
                ? "Connected to " + office + ". You're marked in the office today."
                : "Wi-Fi: " + status.Describe() + ". That isn't an office network, so today isn't marked.";
            tray.ShowBalloonTip(5000, "Office Days", text + "\n" + QuarterSoFar(), ToolTipIcon.Info);
        }

        void ShowCloseTip()
        {
            if (closeTipShown || exiting || tray == null) return;
            closeTipShown = true;
            tray.ShowBalloonTip(5000, "Office Days is still running",
                "It stays in the notification area and keeps marking your office days. Right-click the icon to exit.", ToolTipIcon.Info);
        }

        ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            var open = new ToolStripMenuItem("Open Office Days", null, (s, e) => ShowMainWindow());
            open.Font = new Font(open.Font, FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add("Check Wi-Fi now", null, (s, e) => { CheckNow(); ShowCheckResult(); });
            menu.Items.Add("Settings\u2026", null, (s, e) => ShowSettings(mainForm));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitApp());
            return menu;
        }

        void ScheduleCheck()
        {
            if (settleTimer == null || exiting || !watching) return;
            settleTimer.Stop();
            settleTimer.Start();
        }

        void OnNetworkChanged(object sender, EventArgs e)
        {
            invoker.BeginInvoke((MethodInvoker)ScheduleCheck);
        }

        void OnWakeOrUnlock()
        {
            if (exiting || !Settings.IsConfigured) return;
            if (!Store.Contains(DateTime.Today)) BeginDay();
            else
            {
                ConsiderPaceNotice(false);
                if (dayTimer == null || !dayTimer.Enabled) FinishToday();
                ScheduleWeekNotice();
            }
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) invoker.BeginInvoke((MethodInvoker)OnWakeOrUnlock);
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
                invoker.BeginInvoke((MethodInvoker)OnWakeOrUnlock);
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            SaveStore();
        }

        public void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            StopWatching();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.SessionEnding -= OnSessionEnding;
            if (dayTimer != null) dayTimer.Stop();
            if (weekTimer != null) weekTimer.Stop();
            if (settleTimer != null) settleTimer.Stop();
            if (Settings.IsConfigured) SaveStore();
            if (mainForm != null && !mainForm.IsDisposed) mainForm.Close();
            if (tray != null)
            {
                tray.Visible = false;
                tray.Icon = null;
                tray.Dispose();
            }
            if (trayIcon != null) trayIcon.Dispose();
            ExitThread();
        }
    }
}
