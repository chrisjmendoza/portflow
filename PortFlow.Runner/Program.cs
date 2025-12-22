using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using PortFlow.Runner.Steps;
using PortFlow.Runner.Usb;

namespace PortFlow.Runner;

/// <summary>
/// Entry point for the PortFlow headless backup runner.
/// </summary>
/// <remarks>
/// Modes:
/// - Run-once: selects a target drive (explicit or sentinel discovery) and executes a backup.
/// - Watcher: stays running, listens for WMI volume arrival/removal events, and executes a backup when exactly one
///   sentinel drive is present.
///
/// Performance constraints (by design):
/// - WMI event handlers enqueue work and return immediately.
/// - Drive evaluation in watcher mode is scoped to the event drive only.
/// - Logging is buffered to avoid disk thrash.
/// </remarks>
internal static class Program
{
	private enum VolumeEventKind
	{
		Arrival,
		Removal
	}

	private readonly record struct VolumeEvent(VolumeEventKind Kind, string DriveRoot);

	private sealed record RunnerOptions(string? ConfigPath, string? UsbRoot, bool RunOnce, bool Silent, bool Tray, bool Help);
	private const string MarkerFileName = "PORTFLOW_TARGET.txt";

	[STAThread]
	public static async Task<int> Main(string[] args)
	{
		if (OperatingSystem.IsWindows() && ShouldHideConsoleEarly(args))
		{
			TryHideConsoleWindow();
		}

		RunnerOptions options;
		try
		{
			options = ParseArguments(args);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			Console.Error.WriteLine();
			Console.Error.WriteLine(GetUsageText());
			return 1;
		}

		if (options.Help)
		{
			Console.WriteLine(GetUsageText());
			return 0;
		}

		const string MutexName = "Global\\PortFlow_USB_Backup_Mutex";
		bool createdNew;
		using var mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);

		if (!createdNew)
		{
			if (!options.Silent && !options.Tray)
			{
				Console.WriteLine("Another instance of PortFlow is already running. Exiting.");
			}
			return 0;
		}

		if (options.Tray && !OperatingSystem.IsWindows())
		{
			Console.Error.WriteLine("--tray is only supported on Windows.");
			return 1;
		}

		BackupConfig cfg;
		try
		{
			cfg = BackupConfig.Load(Path.GetFullPath(options.ConfigPath!));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to load config: {ex.Message}");
			return 1;
		}

		await using var logger = AsyncFileLogger.Create(cfg.LogPath, writeToConsole: !(options.Silent || options.Tray));
		Action<string> log = logger.Log;
		log("Configuration loaded.");

		if (options.Tray)
		{
			return await RunWithTrayAsync(options, cfg, log).ConfigureAwait(false);
		}

		return await RunHeadlessAsync(options, cfg, log).ConfigureAwait(false);
	}

	private static async Task<int> RunHeadlessAsync(RunnerOptions options, BackupConfig cfg, Action<string> log)
	{
		using var cts = new CancellationTokenSource();
		RegisterShutdownHandlers(log, cts);

		var step = new RobocopyBackupStep();
		return await RunCoreAsync(options, cfg, step, log, cts.Token).ConfigureAwait(false);
	}

	[SupportedOSPlatform("windows")]
	private static async Task<int> RunWithTrayAsync(RunnerOptions options, BackupConfig cfg, Action<string> log)
	{
		using var cts = new CancellationTokenSource();
		RegisterShutdownHandlers(log, cts);

		var step = new RobocopyBackupStep();
		var runnerTask = RunCoreAsync(options, cfg, step, log, cts.Token);

		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);

		using var trayContext = new TrayIconContext(
			toolTipText: "PortFlow Backup",
			logFilePath: cfg.LogPath,
			log: log,
			onExitRequested: () =>
			{
				log("Exit requested from tray.");
				cts.Cancel();
			}
		);

		_ = runnerTask.ContinueWith(_ =>
		{
			try
			{
				trayContext.ExitThread();
			}
			catch
			{
				// ignored
			}
		}, CancellationToken.None);

		Application.Run(trayContext);

		try
		{
			return await runnerTask.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return 0;
		}
	}

	private static void RegisterShutdownHandlers(Action<string> log, CancellationTokenSource cts)
	{
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			try
			{
				log("Cancellation requested. Attempting graceful shutdown.");
				eventArgs.Cancel = true;
				cts.Cancel();
			}
			catch
			{
				// ignored
			}
		};
		AppDomain.CurrentDomain.ProcessExit += (_, _) =>
		{
			// Best-effort: ensure we stop watchers/loops cleanly.
			try
			{
				cts.Cancel();
			}
			catch
			{
				// ignored
			}
		};
	}

	private static async Task<int> RunCoreAsync(
		RunnerOptions options,
		BackupConfig cfg,
		RobocopyBackupStep step,
		Action<string> log,
		CancellationToken ct)
	{
		if (options.RunOnce || !cfg.StayRunning)
		{
			var resolveResult = UsbTargetResolver.ResolveUsbRoot(
				explicitUsbRoot: options.UsbRoot,
				markerFileName: MarkerFileName,
				log: log
			);
			log(resolveResult.Message);
			if (!resolveResult.Success || string.IsNullOrWhiteSpace(resolveResult.UsbRoot))
			{
				return resolveResult.ExitCode != 0 ? resolveResult.ExitCode : 2;
			}

			var usbRoot = resolveResult.UsbRoot;
			try
			{
				var runResult = await step.RunAsync(usbRoot, cfg, log, ct).ConfigureAwait(false);
				if (runResult != 0)
				{
					log("Backup step reported failure.");
					return runResult;
				}

				log("Backup completed successfully.");
			}
			catch (OperationCanceledException)
			{
				log("Backup canceled by user.");
				return 1;
			}
			catch (Exception ex)
			{
				log($"Unexpected error: {ex}");
				return 1;
			}

			log("Exiting after single run.");
			return 0;
		}

		if (!OperatingSystem.IsWindows())
		{
			log("Watcher mode is only supported on Windows. Exiting.");
			return 0;
		}

		return await RunWatcherModeAsync(options, cfg, step, log, ct).ConfigureAwait(false);
	}

	[SupportedOSPlatform("windows")]
	private static async Task<int> RunWatcherModeAsync(
		RunnerOptions options,
		BackupConfig cfg,
		RobocopyBackupStep step,
		Action<string> log,
		CancellationToken ct)
	{
		log("Watcher started (stayRunning=true). Waiting for USB insertion...");

		var volumeEvents = Channel.CreateBounded<VolumeEvent>(new BoundedChannelOptions(capacity: 256)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.DropOldest
		});

		using var oneBackupAtATime = new SemaphoreSlim(1, 1);
		var workerTask = Task.Run(
			() => ProcessVolumeEventsAsync(options, cfg, step, log, volumeEvents.Reader, oneBackupAtATime, ct),
			ct
		);

		// WMI query: volume arrival + removal (EventType: 2=arrival, 3=removal)
		var query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2 OR EventType = 3");
		using var watcher = new ManagementEventWatcher(query);

		watcher.EventArrived += (_, e) =>
		{
			try
			{
				var newEvent = e.NewEvent;
				var driveName = newEvent?.Properties["DriveName"]?.Value as string;
				var eventTypeObj = newEvent?.Properties["EventType"]?.Value;

				if (string.IsNullOrWhiteSpace(driveName)) return;
				if (!TryNormalizeDriveRoot(driveName, out var driveRoot)) return;

				int eventType;
				try
				{
					eventType = eventTypeObj is null ? 0 : Convert.ToInt32(eventTypeObj);
				}
				catch
				{
					return;
				}

				var kind = eventType switch
				{
					2 => VolumeEventKind.Arrival,
					3 => VolumeEventKind.Removal,
					_ => (VolumeEventKind?)null
				};

				if (kind is null) return;
				_ = volumeEvents.Writer.TryWrite(new VolumeEvent(kind.Value, driveRoot));
			}
			catch
			{
				// ignored: never crash WMI event thread
			}
		};

		try
		{
			watcher.Start();
			await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			log("Shutdown requested. Exiting runner.");
		}
		finally
		{
			try
			{
				volumeEvents.Writer.TryComplete();
			}
			catch
			{
				// ignored
			}

			try
			{
				watcher.Stop();
			}
			catch
			{
				// ignored
			}

			try
			{
				await workerTask.ConfigureAwait(false);
			}
			catch
			{
				// ignored
			}
		}

		return 0;
	}

	private static async Task ProcessVolumeEventsAsync(
		RunnerOptions options,
		BackupConfig cfg,
		RobocopyBackupStep step,
		Action<string> log,
		ChannelReader<VolumeEvent> reader,
		SemaphoreSlim oneBackupAtATime,
		CancellationToken ct)
	{
		var sentinelDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var lastArrivalByDrive = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
		var inFlightSentinelChecks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var warnedSentinelCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		var explicitRoot = string.IsNullOrWhiteSpace(options.UsbRoot) ? null : NormalizeExplicitUsbRoot(options.UsbRoot);
		var debounceWindow = TimeSpan.FromSeconds(8);
		var conflictActive = false;

		await foreach (var ev in reader.ReadAllAsync(ct).ConfigureAwait(false))
		{
			if (ct.IsCancellationRequested) break;

			var driveRoot = ev.DriveRoot;
			if (explicitRoot is not null && !string.Equals(driveRoot, explicitRoot, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			switch (ev.Kind)
			{
				case VolumeEventKind.Removal:
				{
					var removed = sentinelDrives.Remove(driveRoot);
					_ = lastArrivalByDrive.Remove(driveRoot);
					_ = inFlightSentinelChecks.Remove(driveRoot);
					if (removed)
					{
						log($"Volume removed: {driveRoot}");
					}
					break;
				}

				case VolumeEventKind.Arrival:
				{
					var now = DateTimeOffset.UtcNow;
					if (lastArrivalByDrive.TryGetValue(driveRoot, out var last) && now - last < debounceWindow)
					{
						break;
					}
					lastArrivalByDrive[driveRoot] = now;

					var hasSentinel = false;
					if (explicitRoot is not null)
					{
						hasSentinel = true;
					}
					else
					{
						if (inFlightSentinelChecks.Contains(driveRoot))
						{
							break;
						}

						inFlightSentinelChecks.Add(driveRoot);
						try
						{
							hasSentinel = await HasSentinelAsync(driveRoot, MarkerFileName, log, warnedSentinelCheck, ct)
								.ConfigureAwait(false);
						}
						finally
						{
							inFlightSentinelChecks.Remove(driveRoot);
						}
					}

					if (hasSentinel)
					{
						var added = sentinelDrives.Add(driveRoot);
						if (added && explicitRoot is null)
						{
							log($"Sentinel detected on {driveRoot}.");
						}
					}

					if (sentinelDrives.Count > 1)
					{
						if (!conflictActive)
						{
							conflictActive = true;
							log($"Conflict: multiple sentinel drives detected ({string.Join(", ", sentinelDrives)}). Refusing to run backup.");
						}
						break;
					}

					if (conflictActive && sentinelDrives.Count <= 1)
					{
						conflictActive = false;
					}

					if (sentinelDrives.Count == 1 && sentinelDrives.Contains(driveRoot))
					{
						if (!await oneBackupAtATime.WaitAsync(0, ct).ConfigureAwait(false))
						{
							log("Backup already running; ignoring trigger.");
							break;
						}

						try
						{
							log($"Target drive ready at {driveRoot}; starting backup...");
							var runResult = await step.RunAsync(driveRoot, cfg, log, ct).ConfigureAwait(false);
							if (runResult != 0)
							{
								log("Backup step reported failure.");
							}
							else
							{
								log("Backup completed successfully.");
							}
						}
						catch (OperationCanceledException)
						{
							log("Backup canceled.");
						}
						catch (Exception ex)
						{
							log($"Unexpected error while running backup: {ex}");
						}
						finally
						{
							oneBackupAtATime.Release();
						}
					}

					break;
				}
			}
		}
	}

	private static async Task<bool> HasSentinelAsync(
		string driveRoot,
		string markerFileName,
		Action<string> log,
		HashSet<string> warnedSentinelCheck,
		CancellationToken ct)
	{
		if (!IsValidLocalDriveRoot(driveRoot))
		{
			return false;
		}

		var markerPath = Path.Combine(driveRoot, markerFileName);
		try
		{
			var existsTask = Task.Run(() =>
			{
				try
				{
					var drive = new DriveInfo(driveRoot);
					if (drive.DriveType == DriveType.Network || drive.DriveType == DriveType.NoRootDirectory)
						return false;

					if (!drive.IsReady)
						return false;

					return File.Exists(markerPath);
				}
				catch
				{
					return false;
				}
			}, ct);

			return await existsTask.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			if (warnedSentinelCheck.Add(driveRoot))
			{
				log($"Sentinel check timed out for {driveRoot}; treating as no sentinel.");
			}
			return false;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			if (warnedSentinelCheck.Add(driveRoot))
			{
				log($"Sentinel check failed for {driveRoot}: {ex.Message}");
			}
			return false;
		}
	}

	private static bool TryNormalizeDriveRoot(string driveName, out string driveRoot)
	{
		driveRoot = string.Empty;
		if (string.IsNullOrWhiteSpace(driveName)) return false;

		var trimmed = driveName.Trim();
		if (trimmed.StartsWith("\\\\", StringComparison.Ordinal)) return false;

		if (trimmed.Length == 2 && trimmed[1] == ':')
		{
			trimmed += "\\";
		}

		if (!IsValidLocalDriveRoot(trimmed)) return false;
		driveRoot = $"{char.ToUpperInvariant(trimmed[0])}:\\";
		return true;
	}

	private static bool IsValidLocalDriveRoot(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return false;
		if (value.Length != 3) return false;
		if (!char.IsLetter(value[0])) return false;
		if (value[1] != ':') return false;
		if (value[2] != '\\') return false;
		return true;
	}

	private static string NormalizeExplicitUsbRoot(string path)
	{
		var trimmed = path.Trim();
		if (trimmed.Length == 2 && trimmed[1] == ':')
		{
			trimmed += "\\";
		}

		if (IsValidLocalDriveRoot(trimmed))
		{
			return $"{char.ToUpperInvariant(trimmed[0])}:\\";
		}

		return Path.GetFullPath(trimmed);
	}

	private static RunnerOptions ParseArguments(IReadOnlyList<string> args)
	{
		string? configPath = null;
		string? usbRoot = null;
		var runOnce = false;
		var silent = false;
		var tray = false;
		var help = false;

		for (var i = 0; i < args.Count; i++)
		{
			var arg = args[i];
			switch (arg)
			{
				case "--help":
				case "-h":
				case "/?":
					help = true;
					break;
				case "--config":
					if (i + 1 >= args.Count) throw new ArgumentException("--config requires a path argument.");
					configPath = args[++i];
					break;
				case "--usb-root":
					if (i + 1 >= args.Count) throw new ArgumentException("--usb-root requires a path argument.");
					usbRoot = args[++i];
					break;
				case "--run-once":
					runOnce = true;
					break;
				case "--silent":
					silent = true;
					break;
				case "--tray":
					tray = true;
					break;
				default:
					throw new ArgumentException($"Unknown argument: {arg}");
			}
		}

		if (help)
		{
			return new RunnerOptions(ConfigPath: null, UsbRoot: usbRoot, RunOnce: runOnce, Silent: silent, Tray: tray, Help: true);
		}

		if (string.IsNullOrWhiteSpace(configPath))
		{
			throw new ArgumentException("--config <path> is required.");
		}

		return new RunnerOptions(configPath, usbRoot, runOnce, silent, tray, help);
	}

	private static string GetUsageText()
	{
		return
			"PortFlow.Runner (PortFlow Backup)\n" +
			"\n" +
			"Usage:\n" +
			"  PortFlow.Runner.exe --config <path> [--tray] [--silent] [--run-once] [--usb-root <root>]\n" +
			"\n" +
			"Options:\n" +
			"  --config <path>     Path to portflow.backup.json (required for normal execution)\n" +
			"  --tray              Run in system tray (Windows only)\n" +
			"  --silent            Do not write logs to console (still writes to log file)\n" +
			"  --run-once          Run one backup attempt and exit\n" +
			"  --usb-root <root>   Explicit target drive root (e.g. E:\\)\n" +
			"  --help, -h, /?      Show this help and exit\n" +
			"\n" +
			"Notes:\n" +
			"  - In watcher mode (stayRunning=true), the runner listens for USB volume arrival/removal events.\n" +
			"  - The sentinel file 'PORTFLOW_TARGET.txt' in the drive root selects the backup target when --usb-root is not used.\n";
	}

	private static void TryHideConsoleWindow()
	{
		try
		{
			var consoleHandle = GetConsoleWindow();
			if (consoleHandle != IntPtr.Zero)
			{
				_ = ShowWindow(consoleHandle, SW_HIDE);
			}
		}
		catch
		{
			// ignored
		}
	}

	private const int SW_HIDE = 0;

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetConsoleWindow();

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	private static bool ShouldHideConsoleEarly(IReadOnlyList<string> args)
	{
		for (var i = 0; i < args.Count; i++)
		{
			var arg = args[i];
			if (string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
