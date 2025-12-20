using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using PortFlow.Runner.Steps;
using PortFlow.Runner.Usb;

namespace PortFlow.Runner;

internal static class Program
{
	private sealed record RunnerOptions(string ConfigPath, string? UsbRoot, bool RunOnce);
	private const string MarkerFileName = "PORTFLOW_TARGET.txt";

	public static async Task<int> Main(string[] args)
	{
		const string MutexName = "Global\\PortFlow_USB_Backup_Mutex";
		bool createdNew;

		using var mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);

		if (!createdNew)
		{
			Console.WriteLine("Another instance of PortFlow is already running. Exiting.");
			return 0;
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

		var log = CreateLogger(cfg.LogPath);
		log("Configuration loaded.");

		using var cts = new CancellationTokenSource();
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

		var step = new RobocopyBackupStep();

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
				var runResult = await step.RunAsync(usbRoot, cfg, log, cts.Token).ConfigureAwait(false);
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

		return await RunWatcherModeAsync(options, cfg, step, log, cts.Token).ConfigureAwait(false);
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
				default:
					throw new ArgumentException($"Unknown argument: {arg}");
			}
		}

		if (string.IsNullOrWhiteSpace(configPath))
		{
			throw new ArgumentException("--config <path> is required.");
		}

		return new RunnerOptions(configPath, usbRoot, runOnce);
	}

	private static Action<string> CreateLogger(string logPath)
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
			Console.WriteLine(line);
			lock (sync)
			{
				File.AppendAllText(absolutePath, line + Environment.NewLine);
			}
		};
	}
}
