using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PortFlow.Runner.Steps;

public sealed class RobocopyBackupStep
{
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
}
