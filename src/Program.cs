using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace OfficeAttendance
{
    internal static class Program
    {
        const string MutexName = @"Local\OfficeAttendance.Running";
        const string ShowEventName = @"Local\OfficeAttendance.Show";
        const string ExitEventName = @"Local\OfficeAttendance.Exit";

        [STAThread]
        static void Main(string[] args)
        {
            bool background = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            bool exit = args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));

            bool firstInstance;
            using (var mutex = new Mutex(true, MutexName, out firstInstance))
            {
                if (!firstInstance)
                {
                    if (exit) Signal(ExitEventName);
                    else if (!background) Signal(ShowEventName);
                    return;
                }
                if (exit) return;

                MemoryCap.Apply();
                try
                {
                    SetProcessDPIAware();
                }
                catch (EntryPointNotFoundException)
                {
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) =>
                {
                    Log.Write("Error: " + e.Exception);
                    MessageBox.Show("Something went wrong: " + e.Exception.Message, "Office Days", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Fatal error: " + e.ExceptionObject);

                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                using (var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName))
                {
                    var app = new TrayApp();
                    app.ListenForSignals(show, exitSignal);
                    app.Start(!background);
                    Application.Run(app);
                }
            }
        }

        static void Signal(string eventName)
        {
            EventWaitHandle handle;
            if (!EventWaitHandle.TryOpenExisting(eventName, out handle)) return;
            using (handle)
            {
                AllowSetForegroundWindow(-1);
                handle.Set();
            }
        }

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);
    }
}
