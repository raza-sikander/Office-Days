using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace OfficeAttendance
{
    internal sealed class MonthBoard : UserControl
    {
        readonly Button previous, next;
        readonly Label title, count, detail;
        readonly CalendarGrid grid;
        DateTime month;

        public MonthBoard()
        {
            month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            Dock = DockStyle.Fill;
            BackColor = Color.White;

            previous = Ui.Button("\u2039", (s, e) => ShowMonth(month.AddMonths(-1)));
            next = Ui.Button("\u203A", (s, e) => ShowMonth(month.AddMonths(1)));
            previous.Width = Ui.Px(36);
            next.Width = Ui.Px(36);
            title = Ui.Label("", 12f, FontStyle.Bold);
            count = Ui.Label("");
            count.ForeColor = Ui.Grey;
            detail = Ui.Label("Office days are highlighted.");
            detail.ForeColor = Ui.Grey;

            grid = new CalendarGrid { Dock = DockStyle.Fill };
            grid.DayFocused += ShowDetail;

            var nav = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 4,
                Padding = new Padding(0, Ui.Px(8), 0, Ui.Px(4))
            };
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nav.Controls.Add(previous, 0, 0);
            nav.Controls.Add(title, 1, 0);
            nav.Controls.Add(next, 2, 0);
            nav.Controls.Add(count, 3, 0);
            title.Anchor = AnchorStyles.Left;
            count.Anchor = AnchorStyles.Left;
            count.Margin = new Padding(Ui.Px(12), 6, 3, 3);

            detail.Dock = DockStyle.Bottom;
            detail.Height = Ui.Px(28);
            detail.TextAlign = ContentAlignment.MiddleLeft;
            detail.Padding = new Padding(Ui.Px(4), 0, 0, 0);

            Controls.Add(grid);
            Controls.Add(detail);
            Controls.Add(nav);
        }

        public void Reload(AttendanceStore store)
        {
            grid.Store = store;
            ShowMonth(month);
        }

        void ShowMonth(DateTime anyDay)
        {
            month = new DateTime(anyDay.Year, anyDay.Month, 1);
            DateTime current = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            if (month > current) month = current;
            title.Text = month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            next.Enabled = month < current;
            int days = grid.Store == null ? 0 : grid.Store.InRange(month, month.AddMonths(1).AddDays(-1)).Count;
            count.Text = DateText.Count(days, "office day");
            grid.Month = month;
            grid.Invalidate();
            detail.Text = "Office days are highlighted.";
        }

        void ShowDetail(DateTime day)
        {
            if (day == DateTime.MinValue)
            {
                detail.Text = "Office days are highlighted.";
                return;
            }
            AttendanceRecord record = grid.Store == null ? null : grid.Store.Get(day);
            detail.Text = record == null
                ? DateText.Day(day)
                : DateText.Day(day) + (record.FirstSeen.HasValue ? "   \u00B7   arrived " + DateText.Time(record.FirstSeen) : "   \u00B7   in the office");
        }
    }

    internal sealed class CalendarGrid : Control
    {
        public AttendanceStore Store;
        public DateTime Month;
        public event Action<DateTime> DayFocused;

        DateTime hot = DateTime.MinValue;

        public CalendarGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            DateTime day = Hit(e.Location);
            if (day == hot) return;
            hot = day;
            Cursor = day == DateTime.MinValue ? Cursors.Default : Cursors.Hand;
            if (DayFocused != null) DayFocused(day);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hot = DateTime.MinValue;
            Cursor = Cursors.Default;
            if (DayFocused != null) DayFocused(DateTime.MinValue);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (Width < 20 || Height < 20) return;

            int header = Ui.Px(26);
            int cols = 7;
            int rows = 6;
            int cellW = Width / cols;
            int cellH = Math.Max(1, (Height - header) / rows);
            using (var names = new Font("Segoe UI", 8.5f))
            using (var dayFont = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (var timeFont = new Font("Segoe UI", 8f))
            using (var fill = new SolidBrush(Color.FromArgb(214, 234, 214)))
            using (var hotFill = new SolidBrush(Color.FromArgb(232, 240, 232)))
            using (var todayPen = new Pen(Ui.Green, 2f))
            {
                string[] days = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames;
                for (int c = 0; c < cols; c++)
                {
                    int dow = (c + 1) % 7;
                    var box = new Rectangle(c * cellW, 0, cellW, header);
                    TextRenderer.DrawText(g, days[dow], names, box, Ui.Grey,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                int lead = ((int)Month.DayOfWeek + 6) % 7;
                for (int i = 0; i < rows * cols; i++)
                {
                    if (i < lead) continue;
                    DateTime day = Month.AddDays(i - lead);
                    if (day.Month != Month.Month) break;
                    int col = i % cols;
                    int row = i / cols;
                    var cell = new Rectangle(col * cellW, header + row * cellH, cellW, cellH);
                    var inner = Rectangle.Inflate(cell, -Ui.Px(4), -Ui.Px(3));
                    AttendanceRecord record = Store == null ? null : Store.Get(day);
                    bool office = record != null;
                    bool today = day.Date == DateTime.Today;
                    bool hover = day == hot;

                    if (office) g.FillRectangle(fill, inner);
                    else if (hover) g.FillRectangle(hotFill, inner);
                    if (today) g.DrawRectangle(todayPen, inner.X + 1, inner.Y + 1, inner.Width - 2, inner.Height - 2);

                    bool weekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
                    Color number = office ? Ui.Green : weekend ? Ui.Grey : Color.FromArgb(40, 40, 40);
                    var numberBox = new Rectangle(inner.X, inner.Y + Ui.Px(4), inner.Width, Ui.Px(22));
                    TextRenderer.DrawText(g, day.Day.ToString(CultureInfo.InvariantCulture), dayFont, numberBox, number,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);

                    if (office && record.FirstSeen.HasValue && inner.Height > Ui.Px(40))
                    {
                        var timeBox = new Rectangle(inner.X, numberBox.Bottom, inner.Width, inner.Height - numberBox.Height);
                        TextRenderer.DrawText(g, DateText.Time(record.FirstSeen), timeFont, timeBox, Color.FromArgb(46, 100, 50),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                    }
                }
            }
        }

        DateTime Hit(Point point)
        {
            if (Width < 20 || Height < 20) return DateTime.MinValue;
            int header = Ui.Px(26);
            if (point.Y < header) return DateTime.MinValue;
            int cellW = Width / 7;
            int cellH = Math.Max(1, (Height - header) / 6);
            if (cellW <= 0) return DateTime.MinValue;
            int col = point.X / cellW;
            int row = (point.Y - header) / cellH;
            if (col < 0 || col > 6 || row < 0 || row > 5) return DateTime.MinValue;
            int lead = ((int)Month.DayOfWeek + 6) % 7;
            int index = row * 7 + col;
            if (index < lead) return DateTime.MinValue;
            DateTime day = Month.AddDays(index - lead);
            return day.Month == Month.Month ? day : DateTime.MinValue;
        }
    }
}
