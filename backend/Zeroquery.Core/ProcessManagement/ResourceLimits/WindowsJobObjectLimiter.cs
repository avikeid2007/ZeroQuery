using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Zeroquery.Core.ProcessManagement.ResourceLimits;

/// <summary>
/// Win32 Job Object implementation of process limits for Windows.
/// Caps subprocess memory, CPU rate, and guarantees termination on parent exit via
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE (doc/Plan.md Section 3 &amp; Phase 6).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsJobObjectLimiter : IProcessResourceLimiter
{
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x0100;
    private const uint JOB_OBJECT_LIMIT_JOB_MEMORY = 0x0200;

    private const int JobObjectExtendedLimitInformation = 9;
    private const int JobObjectCpuRateControlInformation = 15;

    private const uint JOB_OBJECT_CPU_RATE_CONTROL_ENABLE = 0x1;
    private const uint JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInformationClass, IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private readonly List<IntPtr> _jobHandles = new();
    private readonly ILogger _logger;
    private bool _disposed;

    public WindowsJobObjectLimiter(ILogger logger)
    {
        _logger = logger;
    }

    public void ApplyLimits(Process process, int maxMemoryMb, int cpuLimitPercent)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsJobObjectLimiter));

        var hJob = CreateJobObject(IntPtr.Zero, null);
        if (hJob == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            _logger.LogWarning("Failed to create Win32 Job Object for process {ProcessId}, error {ErrorCode}", process.Id, err);
            return;
        }

        lock (_jobHandles)
        {
            _jobHandles.Add(hJob);
        }

        // Configure memory limits and kill-on-close
        var extInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation =
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        if (maxMemoryMb > 0)
        {
            extInfo.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_PROCESS_MEMORY | JOB_OBJECT_LIMIT_JOB_MEMORY;
            var memoryBytes = (UIntPtr)((ulong)maxMemoryMb * 1024 * 1024);
            extInfo.ProcessMemoryLimit = memoryBytes;
            extInfo.JobMemoryLimit = memoryBytes;
        }

        var extLength = (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var extPtr = Marshal.AllocHGlobal((int)extLength);
        try
        {
            Marshal.StructureToPtr(extInfo, extPtr, false);
            if (!SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, extPtr, extLength))
            {
                _logger.LogWarning("Failed to set memory limits on Job Object: error {ErrorCode}", Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extPtr);
        }

        // Configure CPU limits
        if (cpuLimitPercent is > 0 and <= 100)
        {
            var cpuInfo = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
            {
                ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
                CpuRate = (uint)(cpuLimitPercent * 100) // 100 = 1.00%, so 50% = 5000
            };

            var cpuLength = (uint)Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
            var cpuPtr = Marshal.AllocHGlobal((int)cpuLength);
            try
            {
                Marshal.StructureToPtr(cpuInfo, cpuPtr, false);
                if (!SetInformationJobObject(hJob, JobObjectCpuRateControlInformation, cpuPtr, cpuLength))
                {
                    _logger.LogWarning("Failed to set CPU rate limits on Job Object: error {ErrorCode}", Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(cpuPtr);
            }
        }

        // Assign process to Job Object
        if (!AssignProcessToJobObject(hJob, process.Handle))
        {
            _logger.LogWarning("Failed to assign process {ProcessId} to Job Object: error {ErrorCode}", process.Id, Marshal.GetLastWin32Error());
        }
        else
        {
            _logger.LogInformation("Assigned process {ProcessId} to Job Object with {MaxMemoryMb}MB limit and {CpuLimitPercent}% CPU cap.",
                process.Id, maxMemoryMb, cpuLimitPercent);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_jobHandles)
        {
            foreach (var handle in _jobHandles)
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
            _jobHandles.Clear();
        }
    }
}
