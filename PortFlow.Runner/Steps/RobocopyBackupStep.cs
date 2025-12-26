using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PortFlow.Runner.Steps;

/// <summary>
/// Backup step implementation that shells out to <c>robocopy.exe</c>.
/// </summary>
/// <remarks>
/// Logging is intentionally throttled by default to avoid excessive disk I/O.
/// Set <see cref="BackupConfig.VerboseRobocopyLog"/> to enable more verbose output.
/// </remarks>
public sealed class RobocopyBackupStep
{
    /// <summary>
    /// Executes a backup from <see cref="BackupConfig.SourcePath"/> to the specified USB drive root.
    /// </summary>
    /// <param name="usbRoot">Drive root (e.g. <c>E:\</c>) that will receive the backup.</param>
    /// <param name="cfg">Backup configuration.</param>
    /// <param name="log">Log sink.</param>
    /// <param name="ct">Cancellation token used to stop the robocopy process.</param>
    /// <returns>
    /// 0 on success; 1 on failure. (Robocopy exit codes &gt;= 8 are treated as failures.)
    /// </returns>
    /// <exception cref="ArgumentException">usbRoot is null/empty.</exception>
    /// <exception cref="ArgumentNullException">cfg or log are null.</exception>
    /// <exception cref="DirectoryNotFoundException">SourcePath does not exist.</exception>
    public async Task<int> RunAsync(string usbRoot, BackupConfig cfg, Action<string> log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(usbRoot)) throw new ArgumentException("USB root is required.", nameof(usbRoot));
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        if (log is null) throw new ArgumentNullException(nameof(log));

        if (!Directory.Exists(cfg.SourcePath))
            throw new DirectoryNotFoundException($"Source path not found: {cfg.SourcePath}");

        Directory.CreateDirectory(usbRoot);
        var destination = Path.Combine(usbRoot, cfg.DestinationFolderName);
        Directory.CreateDirectory(destination);

        var arguments = BuildArguments(cfg, destination);
        log($"Executing robocopy {arguments}");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "robocopy",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true
        };

        var completionSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var verbose = cfg.VerboseRobocopyLog;
        var summaryBuffer = new Queue<string>(capacity: 64);
        var rateWindowStart = DateTimeOffset.UtcNow;
        var rateCount = 0;
        const int MaxVerboseLinesPerSecond = 5;

        static bool IsImportantLine(string line)
            => line.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 ||
               line.IndexOf("FAILED", StringComparison.OrdinalIgnoreCase) >= 0 ||
               line.IndexOf("WARNING", StringComparison.OrdinalIgnoreCase) >= 0;

        void AddSummaryLine(string line)
        {
            if (summaryBuffer.Count >= 30)
            {
                summaryBuffer.Dequeue();
            }
            summaryBuffer.Enqueue(line);
        }

        bool ShouldLogVerboseNow()
        {
            var now = DateTimeOffset.UtcNow;
            if (now - rateWindowStart >= TimeSpan.FromSeconds(1))
            {
                rateWindowStart = now;
                rateCount = 0;
            }

            if (rateCount >= MaxVerboseLinesPerSecond)
            {
                return false;
            }

            rateCount++;
            return true;
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                var line = e.Data;
                if (IsImportantLine(line))
                {
                    log(line);
                    return;
                }

                if (!verbose)
                {
                    AddSummaryLine(line);
                    return;
                }

                if (ShouldLogVerboseNow())
                {
                    log(line);
                }
                else
                {
                    AddSummaryLine(line);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                var line = e.Data;
                if (IsImportantLine(line) || verbose)
                {
                    log(line);
                }
                else
                {
                    AddSummaryLine(line);
                }
            }
        };

        process.Exited += (_, _) => completionSource.TrySetResult(process.ExitCode);

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start robocopy process.");
        }

        // Wrap in Job Object to ensure child processes are killed
        IntPtr jobHandle = IntPtr.Zero;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (jobHandle != IntPtr.Zero)
                {
                    // Configure job: kill all processes when job handle closes
                    var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                    {
                        BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                        {
                            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_PRIORITY_CLASS,
                            PriorityClass = BELOW_NORMAL_PRIORITY_CLASS // Below-normal CPU priority
                        }
                    };

                    int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
                    try
                    {
                        Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
                        if (SetInformationJobObject(jobHandle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, extendedInfoPtr, (uint)length))
                        {
                            if (AssignProcessToJobObject(jobHandle, process.Handle))
                            {
                                log("Robocopy process assigned to job object with below-normal CPU priority.");
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(extendedInfoPtr);
                    }
                }
            }
            catch (Exception ex)
            {
                log($"Failed to create job object (non-fatal): {ex.Message}");
            }
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignored
            }

            completionSource.TrySetCanceled(ct);
        });

        int exitCode;
        try
        {
            exitCode = await completionSource.Task.ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            log("Robocopy was canceled.");
            throw;
        }
        finally
        {
            try
            {
                process.CancelOutputRead();
                process.CancelErrorRead();
            }
            catch
            {
                // ignored
            }

            // Clean up job object
            if (jobHandle != IntPtr.Zero)
            {
                try
                {
                    CloseHandle(jobHandle);
                }
                catch
                {
                    // ignored
                }
            }
        }

        if (exitCode >= 8)
        {
            log($"Robocopy reported failure with exit code {exitCode}.");
            if (summaryBuffer.Count > 0)
            {
                log("Robocopy output (tail):");
                foreach (var line in summaryBuffer.TakeLast(15))
                {
                    log(line);
                }
            }
            return 1;
        }

        log($"Robocopy completed with exit code {exitCode}.");
        if (!verbose && summaryBuffer.Count > 0)
        {
            log("Robocopy output (tail):");
            foreach (var line in summaryBuffer.TakeLast(15))
            {
                log(line);
            }
        }
        return 0;
    }

    private static string BuildArguments(BackupConfig cfg, string destination)
    {
        var segments = new List<string>
        {
            Quote(cfg.SourcePath),
            Quote(destination),
            "/R:2",
            "/W:2",
            "/NP"
        };

        if (cfg.Mirror)
        {
            segments.Add("/MIR");
        }

        if (cfg.Exclude is { Count: > 0 })
        {
            segments.Add("/XF");
            segments.AddRange(cfg.Exclude.Select(Quote));
        }

        return string.Join(' ', segments);
    }

    private static string Quote(string value) => value.Contains(' ')
        ? $"\"{value}\""
        : value;

    // Win32 Job Object interop for reliable process cleanup
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JOBOBJECTINFOCLASS JobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JOBOBJECTINFOCLASS
    {
        JobObjectBasicLimitInformation = 2,
        JobObjectExtendedLimitInformation = 9
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
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const uint JOB_OBJECT_LIMIT_PRIORITY_CLASS = 0x00000020;
    private const uint BELOW_NORMAL_PRIORITY_CLASS = 0x4000;
}
