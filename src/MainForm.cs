using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace OfficeAttendance
{
    internal sealed class MainForm : Form
    {
        readonly TrayApp app;
        Label todayLabel, wifiLabel, hintLabel, countLabel, reportTitle, reportSummary;
        QuarterBanner daysBanner, calendarBanner, reportBanner;
        MonthBoard calendar;
        ComboBox filter;
        ListView days, quarters, details;
        QuarterReport shownReport;
        bool fillingFilter;

        sealed class FilterItem
        {
            public string Text;
            public Quarter? Period;

            public override string ToString()
            {
                return Text;
            }
        }

        public MainForm(TrayApp app)
        {
            this.app = app;
            Text = "Office Days";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            Icon = Icons.App;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.Px(900), Ui.Px(640));
            MinimumSize = new Size(Ui.Px(700), Ui.Px(500));

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(Ui.Px(14), Ui.Px(5)) };
            tabs.TabPages.Add(BuildDaysTab());
            tabs.TabPages.Add(BuildCalendarTab());
            tabs.TabPages.Add(BuildReportTab());

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(Ui.Px(10)) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(tabs, 0, 1);
            Controls.Add(root);

            RefreshAll(true);
        }

        public void RefreshAll(bool lists)
        {
            RefreshHeader();
            if (!lists) return;
            FillFilter();
            FillDays();
            if (calendar != null) calendar.Reload(app.Store);
            FillQuarters();
        }

        Control BuildHeader()
        {
            todayLabel = Ui.Label("", 14f, FontStyle.Bold);
            wifiLabel = Ui.Label("");
            wifiLabel.ForeColor = Ui.Grey;
            hintLabel = Ui.Label("");
            hintLabel.ForeColor = Ui.Amber;
            hintLabel.MaximumSize = new Size(Ui.Px(620), 0);

            var text = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            text.Controls.AddRange(new Control[] { todayLabel, wifiLabel, hintLabel });

            var buttons = Ui.Flow(Ui.Button("Check Wi-Fi now", (s, e) => app.CheckNow()), Ui.Button("Settings\u2026", (s, e) => app.ShowSettings(this)));
            buttons.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var header = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, Ui.Px(8))
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(text, 0, 0);
            header.Controls.Add(buttons, 1, 0);
            return header;
        }

        TabPage BuildDaysTab()
        {
            filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.Px(260) };
            filter.SelectedIndexChanged += (s, e) => FillDays();
            countLabel = Ui.Label("");
            countLabel.ForeColor = Ui.Grey;

            days = Ui.List("Date", "Arrived", "Network", "Recorded by");

            FlowLayoutPanel actions = Ui.Flow(
                Ui.Button("Import Wi-Fi history", (s, e) => ImportHistory()),
                Ui.Button("Export CSV\u2026", (s, e) => ExportDays()));
            actions.Margin = new Padding(0, Ui.Px(6), 0, 0);

            daysBanner = new QuarterBanner();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            FlowLayoutPanel filterBar = Ui.Flow(Ui.Label("Show:"), filter, countLabel);
            filterBar.Margin = new Padding(0, Ui.Px(8), 0, Ui.Px(6));
            layout.Controls.Add(daysBanner, 0, 0);
            layout.Controls.Add(filterBar, 0, 1);
            layout.Controls.Add(days, 0, 2);
            layout.Controls.Add(actions, 0, 3);
            return Page("Office days", layout);
        }

        TabPage BuildCalendarTab()
        {
            calendarBanner = new QuarterBanner();
            calendar = new MonthBoard { Dock = DockStyle.Fill };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(calendarBanner, 0, 0);
            layout.Controls.Add(calendar, 0, 1);
            return Page("Calendar", layout);
        }

        TabPage BuildReportTab()
        {
            quarters = Ui.List("Quarter", "Dates", "Office days", "Working days", "Pace");
            quarters.MultiSelect = false;
            AlignRight(quarters, 2, 3);
            quarters.SelectedIndexChanged += (s, e) => ShowSelectedQuarter();

            reportTitle = Ui.Label("", 12f, FontStyle.Bold);
            reportSummary = Ui.Label("");
            var titleText = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            titleText.Controls.AddRange(new Control[] { reportTitle, reportSummary });
            Button save = Ui.Button("Save report\u2026", (s, e) => SaveReport());
            save.Anchor = AnchorStyles.Right;

            var titleRow = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, Ui.Px(10), 0, Ui.Px(6))
            };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleRow.Controls.Add(titleText, 0, 0);
            titleRow.Controls.Add(save, 1, 0);

            details = Ui.List("Period", "Office days", "Working days", "Attendance", "Days in office");
            AlignRight(details, 1, 2, 3);

            reportBanner = new QuarterBanner();
            titleRow.Margin = new Padding(0, Ui.Px(8), 0, Ui.Px(6));
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            layout.Controls.Add(reportBanner, 0, 0);
            layout.Controls.Add(quarters, 0, 1);
            layout.Controls.Add(titleRow, 0, 2);
            layout.Controls.Add(details, 0, 3);
            return Page("Quarterly report", layout);
        }

        void RefreshHeader()
        {
            DateTime today = DateTime.Today;
            AttendanceRecord record = app.Store.Get(today);
            todayLabel.Text = record == null
                ? "Not in the office today"
                : "\u2714  In the office today" + (record.FirstSeen.HasValue ? " \u2013 arrived " + DateText.Time(record.FirstSeen) : "");
            todayLabel.ForeColor = record == null ? Ui.Grey : Ui.Green;

            QuarterReport report = QuarterReport.Build(app.Settings.QuarterFor(today), app.Store, today);
            int weekDays = Pace.DaysThisWeek(app.Store, today);
            bool quarterComplete = report.OfficeDays >= Pace.TargetDays;
            string weekLine = Pace.WeekLine(weekDays, quarterComplete, today);
            daysBanner.Apply(report.TargetPace, report.Quarter.Name, weekDays, weekLine);
            if (calendarBanner != null) calendarBanner.Apply(report.TargetPace, report.Quarter.Name, weekDays, weekLine);
            reportBanner.Apply(report.TargetPace, report.Quarter.Name, weekDays, weekLine);

            WifiStatus status = app.LastStatus;
            string hint = null;
            if (status == null)
            {
                wifiLabel.Text = "Office Wi-Fi: " + app.Settings.NetworksText;
            }
            else
            {
                wifiLabel.Text = "Wi-Fi now: " + status.Describe() + "   \u00B7   Office Wi-Fi: " + app.Settings.NetworksText
                    + "   \u00B7   Checked " + DateText.Time(status.CheckedAt);
                if (status.Error != null && status.Connections.Count == 0) hint = "Couldn't read the Wi-Fi status: " + status.Error;
                else if (status.UsedEventLog)
                    hint = "Windows is hiding Wi-Fi names from apps (location access is off), so the network was read from the Windows Wi-Fi log instead. Attendance still works.";
            }
            hintLabel.Text = hint ?? "";
            hintLabel.Visible = hint != null;
        }

        void FillFilter()
        {
            string previous = filter.SelectedItem != null ? filter.SelectedItem.ToString() : null;
            fillingFilter = true;
            try
            {
                filter.Items.Clear();
                filter.Items.Add(new FilterItem { Text = "All days" });
                foreach (Quarter q in Quarter.UpTo(DateTime.Today, app.Store, app.Settings.QuarterStartMonth))
                    filter.Items.Add(new FilterItem { Text = q.Name + "  (" + q.Range + ")", Period = q });
                int index = 0;
                for (int i = 0; i < filter.Items.Count; i++)
                    if (filter.Items[i].ToString() == previous) index = i;
                filter.SelectedIndex = index;
            }
            finally
            {
                fillingFilter = false;
            }
        }

        List<AttendanceRecord> FilteredDays()
        {
            var item = filter.SelectedItem as FilterItem;
            if (item == null || !item.Period.HasValue) return app.Store.Records.ToList();
            return app.Store.InRange(item.Period.Value.Start, item.Period.Value.End);
        }

        void FillDays()
        {
            if (fillingFilter) return;
            List<AttendanceRecord> rows = FilteredDays();
            rows.Reverse();

            days.BeginUpdate();
            days.Items.Clear();
            days.Groups.Clear();
            ListViewGroup group = null;
            foreach (AttendanceRecord r in rows)
            {
                string month = r.Date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
                if (group == null || group.Name != month)
                {
                    int inMonth = rows.Count(x => x.Date.Year == r.Date.Year && x.Date.Month == r.Date.Month);
                    group = new ListViewGroup(month, month + "  \u2013  " + DateText.Count(inMonth, "office day"));
                    days.Groups.Add(group);
                }
                var item = new ListViewItem(new[] { DateText.Day(r.Date), DateText.Time(r.FirstSeen), r.Network, r.Source }, group);
                item.Tag = r.Date;
                if (r.Date == DateTime.Today) item.ForeColor = Ui.Green;
                days.Items.Add(item);
            }
            days.EndUpdate();
            Ui.FitColumns(days);
            countLabel.Text = DateText.Count(rows.Count, "office day");
        }

        void FillQuarters()
        {
            Quarter? selected = shownReport != null ? shownReport.Quarter : (Quarter?)null;
            quarters.BeginUpdate();
            quarters.Items.Clear();
            foreach (Quarter q in Quarter.UpTo(DateTime.Today, app.Store, app.Settings.QuarterStartMonth))
            {
                QuarterReport r = QuarterReport.Build(q, app.Store, DateTime.Today);
                string working = r.InProgress ? r.WorkingDaysSoFar + " so far (of " + r.WorkingDays + ")" : r.WorkingDays.ToString(CultureInfo.InvariantCulture);
                var item = new ListViewItem(new[] { q.Name, q.Range, r.OfficeDays + " of " + Pace.TargetDays, working, r.TargetPace.ShortLabel });
                item.Tag = r;
                quarters.Items.Add(item);
            }
            quarters.EndUpdate();
            Ui.FitColumns(quarters);

            ListViewItem pick = quarters.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => selected.HasValue && ((QuarterReport)i.Tag).Quarter.Equals(selected.Value));
            if (pick == null && quarters.Items.Count > 0) pick = quarters.Items[0];
            if (pick != null)
            {
                pick.Selected = true;
                pick.Focused = true;
            }
            ShowSelectedQuarter();
        }

        void ShowSelectedQuarter()
        {
            if (quarters.SelectedItems.Count == 0) return;
            shownReport = (QuarterReport)quarters.SelectedItems[0].Tag;
            reportTitle.Text = shownReport.Quarter.Name + "   \u00B7   " + shownReport.Quarter.Range;
            reportSummary.Text = shownReport.Summary;

            details.BeginUpdate();
            details.Items.Clear();
            details.Groups.Clear();
            AddRows("By month", shownReport.Months);
            AddRows("By week", shownReport.Weeks);
            details.EndUpdate();
            Ui.FitColumns(details);
        }

        void AddRows(string title, List<PeriodRow> rows)
        {
            var group = new ListViewGroup(title, title);
            details.Groups.Add(group);
            foreach (PeriodRow r in rows)
                details.Items.Add(new ListViewItem(new[]
                {
                    r.Label,
                    r.OfficeDays.ToString(CultureInfo.InvariantCulture),
                    r.WorkingDays.ToString(CultureInfo.InvariantCulture),
                    r.Percent,
                    r.Detail
                }, group));
        }

        void ImportHistory()
        {
            string message;
            Cursor = Cursors.WaitCursor;
            try
            {
                message = app.ImportHistory();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            RefreshAll(true);
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ExportDays()
        {
            var item = filter.SelectedItem as FilterItem;
            List<AttendanceRecord> rows = FilteredDays();
            using (var dialog = new SaveFileDialog
            {
                Filter = "CSV file (*.csv)|*.csv",
                FileName = "Office days" + (item != null && item.Period.HasValue ? " " + item.Period.Value.Name : "") + ".csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    AttendanceStore.ExportCsv(dialog.FileName, rows);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't save the file: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                MessageBox.Show(this, "Saved " + DateText.Count(rows.Count, "office day") + " to " + dialog.FileName, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void SaveReport()
        {
            if (shownReport == null) return;
            using (var dialog = new SaveFileDialog
            {
                Filter = "Web page (*.html)|*.html",
                FileName = "Office Days " + shownReport.Quarter.Name + ".html",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    FileUtil.WriteAllTextAtomic(dialog.FileName, shownReport.ToHtml());
                    Process.Start(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't save the report: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        static TabPage Page(string title, Control content)
        {
            var page = new TabPage(title) { Padding = new Padding(Ui.Px(8)), UseVisualStyleBackColor = true };
            page.Controls.Add(content);
            return page;
        }

        static void AlignRight(ListView list, params int[] columns)
        {
            foreach (int c in columns) list.Columns[c].TextAlign = HorizontalAlignment.Right;
        }
    }

    internal sealed class QuarterBanner : Panel
    {
        readonly Label caption;
        readonly Label value;
        readonly Label week;
        readonly Label detail;

        public QuarterBanner()
        {
            Height = Ui.Px(118);
            Dock = DockStyle.Top;
            Margin = new Padding(0, 0, 0, Ui.Px(4));
            Padding = new Padding(Ui.Px(14), Ui.Px(8), Ui.Px(14), Ui.Px(8));

            caption = new Label
            {
                Text = "this quarter",
                AutoSize = true,
                ForeColor = Ui.Grey,
                Location = new Point(Ui.Px(16), Ui.Px(8))
            };
            value = new Label
            {
                Text = "0 days",
                AutoSize = true,
                Font = new Font("Segoe UI", 28f, FontStyle.Bold),
                Location = new Point(Ui.Px(12), Ui.Px(26))
            };
            week = new Label
            {
                Text = "0 of 3 this week",
                AutoSize = true,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Location = new Point(Ui.Px(16), Ui.Px(78))
            };
            detail = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(Ui.Px(640), 0),
                Location = new Point(Ui.Px(16), Ui.Px(82))
            };
            Controls.Add(caption);
            Controls.Add(value);
            Controls.Add(week);
            Controls.Add(detail);
        }

        public void Apply(Pace pace, string quarterName, int weekDays, string weekLine)
        {
            caption.Text = "this quarter  \u00B7  " + quarterName;
            value.Text = DateText.Count(pace.OfficeDays, "day");
            week.Text = weekLine;
            week.ForeColor = weekDays >= Pace.DaysPerWeek ? Ui.Green : Color.FromArgb(55, 55, 55);
            detail.Text = pace.StatusLine;
            Color color = pace.Status == PaceStatus.Behind || pace.Status == PaceStatus.Short ? Ui.Amber
                : pace.Status == PaceStatus.Early ? Ui.Grey : Ui.Green;
            value.ForeColor = color;
            detail.ForeColor = color;
            BackColor = pace.Status == PaceStatus.Behind || pace.Status == PaceStatus.Short
                ? Color.FromArgb(255, 243, 224)
                : pace.Status == PaceStatus.Met ? Color.FromArgb(232, 245, 233)
                : Color.FromArgb(241, 248, 241);

            int y = Ui.Px(8);
            caption.Location = new Point(Ui.Px(16), y);
            y += Math.Max(caption.PreferredHeight, caption.Height) + Ui.Px(2);
            value.Location = new Point(Ui.Px(12), y);
            y += Math.Max(value.PreferredHeight, value.Height) + Ui.Px(0);
            week.Location = new Point(Ui.Px(16), y);
            y += Math.Max(week.PreferredHeight, week.Height) + Ui.Px(4);
            detail.Location = new Point(Ui.Px(16), y);
            y += Math.Max(detail.PreferredHeight, detail.Height) + Ui.Px(10);
            Height = y;
        }
    }
}
