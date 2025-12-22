using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PortFlow.Runner.Usb;

/// <summary>
/// Resolves the target backup drive root.
/// </summary>
/// <remarks>
/// This resolver supports:
/// - Explicit selection via <c>--usb-root</c>
/// - Fallback discovery by scanning drives for a sentinel marker file
///
/// Watcher mode intentionally avoids full drive enumeration and instead evaluates only
/// the WMI-reported volume root for each insertion event.
/// </remarks>
internal static class UsbTargetResolver
{
    internal sealed record ResolveResult(bool Success, string? UsbRoot, string Message, int ExitCode);

    /// <summary>
    /// Resolves the USB root used for backups.
    /// </summary>
    /// <param name="explicitUsbRoot">Explicit drive root passed by the user (optional).</param>
    /// <param name="markerFileName">Sentinel marker file name expected at the drive root.</param>
    /// <param name="log">Log sink for drive enumeration warnings.</param>
    /// <returns>
    /// A result containing the chosen root (if any) and an exit code:
    /// 0 = success, 2 = no match, 3 = conflict (multiple matches).
    /// </returns>
    public static ResolveResult ResolveUsbRoot(string? explicitUsbRoot, string markerFileName, Action<string> log)
    {
        if (!string.IsNullOrWhiteSpace(explicitUsbRoot))
        {
            var root = NormalizeRoot(explicitUsbRoot);
            if (!Directory.Exists(root))
                return new ResolveResult(false, null, $"Provided --usb-root does not exist: {root}", 2);

            return new ResolveResult(true, root, $"Using explicit USB root: {root}", 0);
        }

        // Auto-detect by sentinel/marker file
        var candidates = FindMarkerMatches(markerFileName, log);

        if (candidates.Count == 0)
        {
            return new ResolveResult(
                false,
                null,
                $"No backup drive detected. Plug in the USB drive containing '{markerFileName}' in its root.",
                2
            );
        }

        if (candidates.Count > 1)
        {
            var list = string.Join(", ", candidates);
            return new ResolveResult(
                false,
                null,
                $"Multiple backup drives detected (marker '{markerFileName}' found on: {list}). Remove extra drives and try again.",
                3
            );
        }

        var chosen = candidates[0];
        return new ResolveResult(true, chosen, $"Detected backup drive: {chosen}", 0);
    }

    private static List<string> FindMarkerMatches(string markerFileName, Action<string> log)
    {
        var matches = new List<string>();

        DriveInfo[] drives;
        try
        {
            // GetDrives() can block on network drives; add timeout
            var getDrivesTask = Task.Run(() => DriveInfo.GetDrives());
            if (!getDrivesTask.Wait(TimeSpan.FromSeconds(5)))
            {
                log("Drive enumeration timed out (network drive issue?). Using empty list.");
                return matches;
            }
            drives = getDrivesTask.Result;
        }
        catch (Exception ex)
        {
            log($"Failed to enumerate drives: {ex.Message}");
            return matches;
        }

        // Process drives in parallel with per-drive timeout
        var driveTasks = drives.Select(drive => Task.Run(async () =>
        {
            try
            {
                // Scan Removable only to avoid touching internal drives
                // (External USB SSDs that appear as Fixed require --usb-root)
                if (drive.DriveType != DriveType.Removable)
                {
                    return null;
                }

                // IsReady can block on dying drives
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var isReadyTask = Task.Run(() => drive.IsReady, cts.Token);
                var isReady = await isReadyTask.ConfigureAwait(false);
                
                if (!isReady)
                    return null;

                var root = drive.RootDirectory.FullName; // e.g. "E:\\"
                var markerPath = Path.Combine(root, markerFileName);

                // File.Exists can also block
                var existsTask = Task.Run(() => File.Exists(markerPath), cts.Token);
                var exists = await existsTask.ConfigureAwait(false);
                
                return exists ? root : null;
            }
            catch (OperationCanceledException)
            {
                log($"Drive scan timed out ({drive.Name})");
                return null;
            }
            catch (Exception ex)
            {
                // Never crash because one drive is weird
                log($"Drive scan skipped ({drive.Name}): {ex.Message}");
                return null;
            }
        })).ToArray();

        try
        {
            Task.WaitAll(driveTasks, TimeSpan.FromSeconds(10));
            foreach (var task in driveTasks)
            {
                if (task.IsCompletedSuccessfully && task.Result != null)
                {
                    matches.Add(task.Result);
                }
            }
        }
        catch
        {
            // Timeout or other error - use what we have
            foreach (var task in driveTasks)
            {
                if (task.IsCompletedSuccessfully && task.Result != null)
                {
                    matches.Add(task.Result);
                }
            }
        }

        // Stable ordering for deterministic behavior
        return matches.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeRoot(string path)
    {
        var trimmed = path.Trim();

        // Allow "E:" or "E:\" or full path; normalize to root with trailing slash when possible
        if (trimmed.Length == 2 && trimmed[1] == ':')
            trimmed += "\\";

        return trimmed;
    }
}
