using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OfficeAttendance
{
    internal static class DateText
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Day(DateTime d) { return d.ToString("ddd, d MMM yyyy", Inv); }
        public static string Long(DateTime d) { return d.ToString("d MMM yyyy", Inv); }
        public static string Short(DateTime d) { return d.ToString("d MMM", Inv); }

        public static string Time(DateTime? t)
        {
            return t.HasValue ? t.Value.ToString("t", CultureInfo.CurrentCulture) : "";
        }

        public static string Range(DateTime from, DateTime to)
        {
            if (from.Year != to.Year) return Long(from) + " \u2013 " + Long(to);
            if (from.Month != to.Month) return Short(from) + " \u2013 " + Long(to);
            return from.Day + " \u2013 " + Long(to);
        }

        public static string RangeNoYear(DateTime from, DateTime to)
        {
            if (from == to) return Short(from);
            if (from.Month != to.Month) return Short(from) + " \u2013 " + Short(to);
            return from.Day + " \u2013 " + Short(to);
        }

        public static string Count(int n, string word)
        {
            return n + " " + word + (n == 1 ? "" : "s");
        }
    }

    internal static class FileUtil
    {
        public static void WriteAllTextAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(true));
            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }
            try
            {
                File.Replace(tmp, path, null);
            }
            catch (IOException)
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }
    }

    internal static class Log
    {
        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataFolder);
                string path = Path.Combine(AppSettings.DataFolder, "log.txt");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 512 * 1024) info.Delete();
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    internal static class AutoStart
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "OfficeDays";
        const string LegacyValueName = "OfficeAttendance";

        public static bool IsEnabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return key != null && (key.GetValue(ValueName) != null || key.GetValue(LegacyValueName) != null);
                }
            }
        }

        public static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --background");
                else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                if (key.GetValue(LegacyValueName) != null) key.DeleteValue(LegacyValueName, false);
            }
        }
    }

    internal static class Ui
    {
        static float scale = -1;

        public static float Scale
        {
            get
            {
                if (scale < 0)
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                return scale;
            }
        }

        public static int Px(int value) { return (int)Math.Round(value * Scale); }

        public static readonly Color Green = Color.FromArgb(46, 125, 50);
        public static readonly Color Grey = Color.FromArgb(110, 110, 110);
        public static readonly Color Amber = Color.FromArgb(180, 95, 6);

        public static Label Label(string text, float size = 9f, FontStyle style = FontStyle.Regular)
        {
            var label = new Label { Text = text, AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
            if (size != 9f || style != FontStyle.Regular) label.Font = new Font("Segoe UI", size, style);
            return label;
        }

        public static Button Button(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(Px(80), 0) };
            button.Padding = new Padding(Px(6), Px(2), Px(6), Px(2));
            if (onClick != null) button.Click += onClick;
            return button;
        }

        public static FlowLayoutPanel Flow(params Control[] controls)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
            foreach (Control c in controls)
                if (c is Label) c.Anchor = AnchorStyles.Left;
            flow.Controls.AddRange(controls);
            return flow;
        }

        public static ListView List(params string[] columns)
        {
            var list = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                Dock = DockStyle.Fill,
                ShowItemToolTips = true
            };
            foreach (string c in columns) list.Columns.Add(c);
            return list;
        }

        public static void FitColumns(ListView list)
        {
            foreach (ColumnHeader column in list.Columns)
            {
                list.AutoResizeColumn(column.Index, ColumnHeaderAutoResizeStyle.HeaderSize);
                int header = column.Width;
                if (list.Items.Count > 0) list.AutoResizeColumn(column.Index, ColumnHeaderAutoResizeStyle.ColumnContent);
                column.Width = Math.Max(header, column.Width) + Px(12);
            }
        }
    }

    internal static class Icons
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        static Icon app;

        public static Icon App
        {
            get { return app ?? (app = Create(true, Ui.Px(32))); }
        }

        public static Icon Create(bool present, int size)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float s = size;
                    var box = new RectangleF(s * 0.06f, s * 0.06f, s * 0.88f, s * 0.88f);
                    using (GraphicsPath path = RoundedRect(box, s * 0.2f))
                    using (var fill = new SolidBrush(present ? Ui.Green : Ui.Grey))
                        g.FillPath(fill, path);

                    Color tick = present ? Color.White : Color.FromArgb(150, Color.White);
                    using (var pen = new Pen(tick, Math.Max(1.6f, s * 0.13f)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        g.DrawLines(pen, new[]
                        {
                            new PointF(s * 0.27f, s * 0.52f),
                            new PointF(s * 0.44f, s * 0.69f),
                            new PointF(s * 0.74f, s * 0.35f)
                        });
                    }
                }
                return IconFromBitmap(bmp);
            }
        }

        /// <summary>Tray icon reading like 2/3. Green once this week is done or today is an office day.</summary>
        public static Icon Fraction(int done, int goal, bool filled, int size)
        {
            size = Math.Max(16, size);
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    float s = size;
                    var box = new RectangleF(s * 0.04f, s * 0.04f, s * 0.92f, s * 0.92f);
                    using (GraphicsPath path = RoundedRect(box, s * 0.18f))
                    using (var fill = new SolidBrush(filled ? Ui.Green : Ui.Grey))
                        g.FillPath(fill, path);

                    string text = done + "/" + goal;
                    float fontPx = size * 0.34f;
                    using (var font = FindFittingFont(g, text, fontPx, size * 0.84f))
                    using (var brush = new SolidBrush(Color.White))
                    using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(text, font, brush, new RectangleF(0, size * 0.04f, size, size * 0.92f), format);
                }
                return IconFromBitmap(bmp);
            }
        }

        static Font FindFittingFont(Graphics g, string text, float startPx, float maxWidth)
        {
            float px = startPx;
            Font font = null;
            while (px >= 6f)
            {
                if (font != null) font.Dispose();
                font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
                if (g.MeasureString(text, font).Width <= maxWidth) return font;
                px -= 0.5f;
            }
            return font;
        }

        static Icon IconFromBitmap(Bitmap bmp)
        {
            IntPtr handle = bmp.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
