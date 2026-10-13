using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OfficeAttendance
{
    internal sealed class SettingsForm : Form
    {
        readonly AppSettings settings;
        readonly CheckedListBox networks;
        readonly TextBox otherNetwork;
        readonly ComboBox fiscalStart;
        readonly Label quarterHint;
        readonly CheckBox autoStart, notify, importHistory;
        readonly Button save, add;

        sealed class NetworkItem
        {
            public string Name;
            public bool Connected;

            public override string ToString()
            {
                return Connected ? Name + "    (connected now)" : Name;
            }
        }

        public SettingsForm(AppSettings settings, bool firstRun)
        {
            this.settings = settings;
            int width = Ui.Px(470);

            Text = firstRun ? "Office Days \u2013 first-time setup" : "Office Days \u2013 settings";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            Icon = Icons.App;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = firstRun;
            StartPosition = firstRun ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Ui.Px(12));

            var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };

            if (firstRun) layout.Controls.Add(Wrapped(Ui.Label("Welcome to Office Days", 13f, FontStyle.Bold), width));
            layout.Controls.Add(Wrapped(Ui.Label(firstRun
                ? "Pick the Wi-Fi network that means you're in the office. This laptop checks it until today is marked, "
                  + "then waits until midnight to start the next day."
                : "A day is marked when this laptop connects to a ticked network. Checking stops once today is logged, and starts again at midnight."), width));

            layout.Controls.Add(Heading("Office Wi-Fi network"));
            networks = new CheckedListBox { CheckOnClick = true, IntegralHeight = false, Width = width, Height = Ui.Px(150) };
            layout.Controls.Add(networks);
            otherNetwork = new TextBox { Width = Ui.Px(230) };
            add = Ui.Button("Add", OnAdd);
            layout.Controls.Add(Ui.Flow(Ui.Label("Not listed? Type its name:"), otherNetwork, add));

            layout.Controls.Add(Heading("Quarterly reports"));
            fiscalStart = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.Px(130) };
            fiscalStart.Items.AddRange(CultureInfo.InvariantCulture.DateTimeFormat.MonthNames.Take(12).Cast<object>().ToArray());
            quarterHint = Ui.Label("");
            quarterHint.ForeColor = Ui.Grey;
            fiscalStart.SelectedIndexChanged += (s, e) => quarterHint.Text = Quarter.Describe(fiscalStart.SelectedIndex + 1);
            fiscalStart.SelectedIndex = settings.QuarterStartMonth - 1;
            layout.Controls.Add(Ui.Flow(Ui.Label("The year starts in"), fiscalStart));
            layout.Controls.Add(quarterHint);
            Label fiscalTip = Wrapped(Ui.Label("January gives calendar quarters. Choose April for India's financial year (April to March)."), width);
            fiscalTip.ForeColor = Ui.Grey;
            layout.Controls.Add(fiscalTip);
            Label targetTip = Wrapped(Ui.Label("Each quarter the target is 39 office days (3 a week). On Mondays you'll be told if that pace won't get you there, or if you've already reached 39 and can skip the office."), width);
            targetTip.ForeColor = Ui.Grey;
            layout.Controls.Add(targetTip);

            layout.Controls.Add(Heading("Options"));
            autoStart = new CheckBox { Text = "Start automatically when I sign in to Windows (recommended)", AutoSize = true, Checked = firstRun || AutoStart.IsEnabled };
            notify = new CheckBox { Text = "Notify me when attendance is marked, and on Mondays about the 39-day target", AutoSize = true, MaximumSize = new Size(width, 0), Checked = settings.ShowNotifications };
            layout.Controls.Add(autoStart);
            layout.Controls.Add(notify);

            DateTime? historyStart = firstRun ? HistoryStart() : null;
            if (historyStart.HasValue)
            {
                importHistory = new CheckBox
                {
                    Text = "Add past office days from Windows Wi-Fi history (back to " + DateText.Long(historyStart.Value) + ")",
                    AutoSize = true,
                    Checked = true
                };
                layout.Controls.Add(importHistory);
            }

            Label dataLabel = Ui.Label("Your attendance is saved in:");
            dataLabel.Margin = new Padding(3, Ui.Px(12), 3, 0);
            var dataLink = new LinkLabel { Text = AppSettings.DataFolder, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(3, 0, 3, 3) };
            dataLink.LinkClicked += (s, e) => OpenDataFolder();
            layout.Controls.Add(dataLabel);
            layout.Controls.Add(dataLink);

            save = Ui.Button(firstRun ? "Save and start" : "Save", OnSave);
            var cancel = Ui.Button("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            var buttons = Ui.Flow(save, cancel);
            buttons.Anchor = AnchorStyles.Right;
            buttons.Margin = new Padding(0, Ui.Px(12), 0, 0);
            layout.Controls.Add(buttons);

            AcceptButton = save;
            CancelButton = cancel;
            otherNetwork.Enter += (s, e) => AcceptButton = add;
            otherNetwork.Leave += (s, e) => AcceptButton = save;

            Controls.Add(layout);
            FillNetworks();
        }

        public bool ImportHistoryRequested
        {
            get { return importHistory != null && importHistory.Checked; }
        }

        void FillNetworks()
        {
            var items = new List<NetworkItem>();
            foreach (WifiConnection c in WifiMonitor.GetStatus().Connections) AddItem(items, c.Name, true);
            foreach (string name in settings.OfficeNetworks) AddItem(items, name, false);
            foreach (string name in WifiMonitor.GetSavedNetworks()) AddItem(items, name, false);

            foreach (NetworkItem item in items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                bool ticked = settings.IsConfigured && settings.IsOfficeNetwork(item.Name);
                networks.Items.Add(item, ticked);
            }
        }

        static void AddItem(List<NetworkItem> items, string name, bool connected)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return;
            NetworkItem existing = items.FirstOrDefault(i => string.Equals(i.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing == null) items.Add(new NetworkItem { Name = name.Trim(), Connected = connected });
            else existing.Connected |= connected;
        }

        void OnAdd(object sender, EventArgs e)
        {
            string name = otherNetwork.Text.Trim();
            if (name.Length == 0) return;
            int index = -1;
            for (int i = 0; i < networks.Items.Count; i++)
                if (string.Equals(((NetworkItem)networks.Items[i]).Name, name, StringComparison.OrdinalIgnoreCase)) index = i;
            if (index < 0) index = networks.Items.Add(new NetworkItem { Name = name });
            networks.SetItemChecked(index, true);
            networks.TopIndex = index;
            otherNetwork.Clear();
        }

        void OnSave(object sender, EventArgs e)
        {
            List<string> chosen = networks.CheckedItems.Cast<NetworkItem>().Select(i => i.Name).ToList();
            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one Wi-Fi network.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            settings.OfficeNetworks.Clear();
            settings.OfficeNetworks.AddRange(chosen);
            settings.QuarterStartMonth = fiscalStart.SelectedIndex + 1;
            settings.ShowNotifications = notify.Checked;
            try
            {
                settings.Save();
                AutoStart.Set(autoStart.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save the settings: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        static DateTime? HistoryStart()
        {
            try
            {
                return WifiMonitor.OldestHistory();
            }
            catch (Exception ex)
            {
                Log.Write("Wi-Fi history unavailable: " + ex.Message);
                return null;
            }
        }

        static void OpenDataFolder()
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            Process.Start("explorer.exe", "\"" + AppSettings.DataFolder + "\"");
        }

        static Label Heading(string text)
        {
            Label label = Ui.Label(text, 10f, FontStyle.Bold);
            label.Margin = new Padding(3, Ui.Px(14), 3, Ui.Px(4));
            return label;
        }

        static Label Wrapped(Label label, int width)
        {
            label.MaximumSize = new Size(width, 0);
            return label;
        }
    }
}
