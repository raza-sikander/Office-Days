using System;
using System.Runtime.InteropServices;

namespace OfficeAttendance
{
    /// <summary>
    /// Hard ceiling on how much memory this process may commit. Past the cap, Windows refuses further allocations.
    /// </summary>
    internal static class MemoryCap
    {
        public const int LimitMegabytes = 512;

        const uint LimitWorkingSet = 0x00000001;
        const uint LimitProcessMemory = 0x00000100;
        const uint LimitJobMemory = 0x00000200;
        const int JobObjectExtendedLimitInformation = 9;

        static IntPtr job;

        public static bool Apply()
        {
            ulong bytes = (ulong)LimitMegabytes * 1024UL * 1024UL;
            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                Log.Write("Memory cap was not set (CreateJobObject error " + Marshal.GetLastWin32Error() + ").");
                return false;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = LimitProcessMemory | LimitJobMemory | LimitWorkingSet;
            info.BasicLimitInformation.MinimumWorkingSetSize = new UIntPtr(4 * 1024 * 1024);
            info.BasicLimitInformation.MaximumWorkingSetSize = new UIntPtr(bytes);
            info.ProcessMemoryLimit = new UIntPtr(bytes);
            info.JobMemoryLimit = new UIntPtr(bytes);

            uint size = (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, size))
            {
                int withWorkingSet = Marshal.GetLastWin32Error();
                info.BasicLimitInformation.LimitFlags = LimitProcessMemory | LimitJobMemory;
                info.BasicLimitInformation.MinimumWorkingSetSize = UIntPtr.Zero;
                info.BasicLimitInformation.MaximumWorkingSetSize = UIntPtr.Zero;
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, size))
                {
                    Log.Write("Memory cap was not set (SetInformationJobObject error " + withWorkingSet + ", then " + Marshal.GetLastWin32Error() + ").");
                    CloseHandle(job);
                    job = IntPtr.Zero;
                    return false;
                }
            }

            if (!AssignProcessToJobObject(job, GetCurrentProcess()))
            {
                Log.Write("Memory cap was not set (AssignProcessToJobObject error " + Marshal.GetLastWin32Error() + ").");
                CloseHandle(job);
                job = IntPtr.Zero;
                return false;
            }

            long confirmed = ReadProcessLimitBytes();
            Log.Write("Memory cap: " + LimitMegabytes + " MB"
                + (confirmed == (long)bytes ? " (active)." : " (requested; Windows reports " + confirmed + " bytes)."));
            return confirmed < 0 || confirmed == (long)bytes;
        }

        public static long ReadProcessLimitBytes()
        {
            JOBOBJECT_EXTENDED_LIMIT_INFORMATION info;
            uint size = (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            if (!QueryInformationJobObject(IntPtr.Zero, JobObjectExtendedLimitInformation, out info, size, IntPtr.Zero))
                return -1;
            return (long)info.ProcessMemoryLimit.ToUInt64();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool QueryInformationJobObject(IntPtr hJob, int jobObjectInfoClass,
            out JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength, IntPtr lpReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();
    }
}
