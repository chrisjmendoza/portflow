using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PortFlow.Runner.Steps;
using PortFlow.Runner.Usb;

namespace PortFlow.Runner;

internal static class Program
{
	private sealed record RunnerOptions(string ConfigPath, string? UsbRoot, bool RunOnce, bool Silent, bool Tray);
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
			return 1;
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
			cfg = BackupConfig.Load(Path.GetFullPath(options.ConfigPath));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to load config: {ex.Message}");
			return 1;
		}

		var log = CreateLogger(cfg.LogPath, writeToConsole: !(options.Silent || options.Tray));
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
			log("Cancellation requested. Attempting graceful shutdown.");
			eventArgs.Cancel = true;
			cts.Cancel();
		};
		AppDomain.CurrentDomain.ProcessExit += (_, _) =>
		{
			// Best-effort: ensure we stop watchers/loops cleanly.
			cts.Cancel();
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

		// Protect against duplicated Windows events and ensure only one backup runs at a time.
		var debounceWindow = TimeSpan.FromSeconds(4);
		var debounceSync = new object();
		var lastAcceptedTrigger = DateTimeOffset.MinValue;
		using var oneBackupAtATime = new SemaphoreSlim(1, 1);

		// WMI query: volume arrival
		var query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2");
		using var watcher = new ManagementEventWatcher(query);

		watcher.EventArrived += (_, _) =>
		{
			var now = DateTimeOffset.Now;
			lock (debounceSync)
			{
				if (now - lastAcceptedTrigger < debounceWindow)
				{
					return;
				}
				lastAcceptedTrigger = now;
			}

			// Never block the WMI event thread; run backup work in the background.
			_ = Task.Run(async () =>
			{
				if (!await oneBackupAtATime.WaitAsync(0, ct).ConfigureAwait(false))
				{
					log("Backup already running; ignoring trigger.");
					return;
				}

				try
				{
					log("Volume arrival detected; scanning for sentinel...");
					var resolveResult = UsbTargetResolver.ResolveUsbRoot(
						explicitUsbRoot: options.UsbRoot,
						markerFileName: MarkerFileName,
						log: log
					);

					if (!resolveResult.Success || string.IsNullOrWhiteSpace(resolveResult.UsbRoot))
					{
						// Keep logs informative but not spammy: only log on events.
						if (resolveResult.ExitCode == 3)
						{
							log("Multiple target drives detected; skipping backup.");
						}
						else
						{
							log("No target drive detected; skipping.");
						}
						return;
					}

					var usbRoot = resolveResult.UsbRoot;
					log($"Target drive detected at {usbRoot}; starting backup...");

					var runResult = await step.RunAsync(usbRoot, cfg, log, ct).ConfigureAwait(false);
					if (runResult != 0)
					{
						log("Backup step reported failure.");
						return;
					}

					log("Backup completed successfully.");
				}
				catch (OperationCanceledException)
				{
					log("Backup canceled.");
				}
				catch (Exception ex)
				{
					log($"Unexpected error while handling volume arrival: {ex}");
				}
				finally
				{
					oneBackupAtATime.Release();
				}
			}, CancellationToken.None);
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
				watcher.Stop();
			}
			catch
			{
				// ignored
			}
		}

		return 0;
	}

	private static RunnerOptions ParseArguments(IReadOnlyList<string> args)
	{
		string? configPath = null;
		string? usbRoot = null;
		var runOnce = false;
		var silent = false;
		var tray = false;

		for (var i = 0; i < args.Count; i++)
		{
			var arg = args[i];
			switch (arg)
			{
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

		if (string.IsNullOrWhiteSpace(configPath))
		{
			throw new ArgumentException("--config <path> is required.");
		}

		return new RunnerOptions(configPath, usbRoot, runOnce, silent, tray);
	}

	private static Action<string> CreateLogger(string logPath, bool writeToConsole)
	{
		var absolutePath = Path.GetFullPath(logPath);
		var sync = new object();
		var directory = Path.GetDirectoryName(absolutePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		return message =>
		{
			var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}";
			if (writeToConsole)
			{
				Console.WriteLine(line);
			}
			lock (sync)
			{
				File.AppendAllText(absolutePath, line + Environment.NewLine);
			}
		};
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
