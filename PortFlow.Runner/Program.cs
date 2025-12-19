using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PortFlow.Runner.Steps;

namespace PortFlow.Runner;

internal static class Program
{
	private sealed record RunnerOptions(string ConfigPath, string? UsbRoot, bool RunOnce);

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

		if (string.IsNullOrWhiteSpace(options.UsbRoot))
		{
			log("USB detection not implemented yet. Please supply --usb-root <path>.");
			return 1;
		}

		var usbRoot = Path.GetFullPath(options.UsbRoot);
		var step = new RobocopyBackupStep();

		using var cts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			log("Cancellation requested. Attempting graceful shutdown.");
			eventArgs.Cancel = true;
			cts.Cancel();
		};

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

		if (options.RunOnce || !cfg.StayRunning)
		{
			log("Exiting after single run.");
			return 0;
		}

		log("StayRunning enabled; idling until cancellation. Further USB detection not yet implemented.");

		try
		{
			await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			log("Shutdown requested. Exiting runner.");
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
