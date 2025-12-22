using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PortFlow.Runner;

/// <summary>
/// Buffered, asynchronous logger that writes log lines to a file from a single background writer.
/// </summary>
/// <remarks>
/// Designed for headless/background operation:
/// - Avoids per-line <see cref="File.AppendAllText(string,string)"/> calls.
/// - Applies bounded backpressure (drops oldest lines when saturated).
/// - Never throws to callers; logging must not crash the runner.
/// </remarks>
internal sealed class AsyncFileLogger : IAsyncDisposable
{
	private readonly Channel<string> _channel;
	private readonly CancellationTokenSource _cts = new();
	private readonly Task _writerTask;
	private readonly bool _writeToConsole;
	private readonly string _logPath;
	private readonly string _absoluteLogPath;

	private readonly int _flushEveryLines;
	private readonly TimeSpan _flushInterval;

	private long _droppedCount;
	private long _lastDropWarningCount;
	private int _disposed;

	private AsyncFileLogger(string logPath, bool writeToConsole, int bufferCapacity, int flushEveryLines, TimeSpan flushInterval)
	{
		_writeToConsole = writeToConsole;
		_logPath = logPath;
		_absoluteLogPath = Path.GetFullPath(logPath);
		_flushEveryLines = flushEveryLines;
		_flushInterval = flushInterval;

		var absolutePath = _absoluteLogPath;
		var directory = Path.GetDirectoryName(absolutePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		_channel = Channel.CreateBounded<string>(new BoundedChannelOptions(bufferCapacity)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.DropOldest
		});

		_writerTask = Task.Run(() => WriterLoopAsync(absolutePath, _cts.Token));
	}

	/// <summary>
	/// Creates a new logger instance and starts the writer task.
	/// </summary>
	/// <param name="logPath">Path to the log file (created/appended as needed).</param>
	/// <param name="writeToConsole">Whether to also echo lines to stdout.</param>
	public static AsyncFileLogger Create(string logPath, bool writeToConsole)
		=> new(logPath, writeToConsole, bufferCapacity: 4096, flushEveryLines: 50, flushInterval: TimeSpan.FromMilliseconds(400));

	/// <summary>
	/// Enqueues a message for logging.
	/// </summary>
	/// <remarks>
	/// This method is non-blocking. If the internal buffer is full, the oldest queued log line is dropped.
	/// </remarks>
	/// <param name="message">The message to log. Empty/whitespace messages are ignored.</param>
	public void Log(string message)
	{
		if (string.IsNullOrWhiteSpace(message)) return;
		if (Volatile.Read(ref _disposed) != 0) return;

		var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}";

		if (_writeToConsole)
		{
			try
			{
				Console.WriteLine(line);
			}
			catch
			{
				// ignored
			}
		}

		if (!_channel.Writer.TryWrite(line))
		{
			var dropped = Interlocked.Increment(ref _droppedCount);
			var lastWarning = Interlocked.Read(ref _lastDropWarningCount);
			
			// Warn on first drop and every 100 drops
			if (dropped == 1 || dropped - lastWarning >= 100)
			{
				Interlocked.Exchange(ref _lastDropWarningCount, dropped);
				var warningMsg = $"[WARNING] Log buffer saturated - {dropped} messages dropped";
				
				if (_writeToConsole)
				{
					try
					{
						Console.WriteLine(warningMsg);
					}
					catch
					{
						// ignored
					}
				}
				
				// Also write to file in silent/tray mode
				try
				{
					File.AppendAllText(_absoluteLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {warningMsg}\n");
				}
				catch
				{
					// Best effort only
				}
			}
		}
	}

	private async Task WriterLoopAsync(string absolutePath, CancellationToken ct)
	{
		try
		{
			// Adaptive buffer: 8KB fixed drives, 32KB removable/network, 16KB fallback
			var bufferSize = DetectOptimalBufferSize(absolutePath);
			
			await using var stream = new FileStream(
				absolutePath,
				FileMode.Append,
				FileAccess.Write,
				FileShare.Read,
				bufferSize: bufferSize,
				useAsync: true
			);
			await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

			var linesSinceFlush = 0;
			var nextFlush = DateTimeOffset.UtcNow + _flushInterval;

			while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
			{
				while (_channel.Reader.TryRead(out var line))
				{
					await writer.WriteLineAsync(line).ConfigureAwait(false);
					linesSinceFlush++;

					if (linesSinceFlush >= _flushEveryLines)
					{
						await writer.FlushAsync().ConfigureAwait(false);
						linesSinceFlush = 0;
						nextFlush = DateTimeOffset.UtcNow + _flushInterval;
					}
				}

				if (DateTimeOffset.UtcNow >= nextFlush)
				{
					await writer.FlushAsync().ConfigureAwait(false);
					linesSinceFlush = 0;
					nextFlush = DateTimeOffset.UtcNow + _flushInterval;
				}
			}

			var dropped = Interlocked.Exchange(ref _droppedCount, 0);
			if (dropped > 0)
			{
				await writer.WriteLineAsync(
					$"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] (logger) Dropped {dropped} log lines due to backpressure."
				).ConfigureAwait(false);
			}

			await writer.FlushAsync().ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// Normal shutdown
		}
		catch
		{
			// Last resort: never crash the runner due to logging.
		}
	}

	private static int DetectOptimalBufferSize(string filePath)
	{
		try
		{
			var drive = new DriveInfo(Path.GetPathRoot(filePath) ?? "C:\\");
			
			// Adaptive buffer: 8KB for Fixed, 32KB for Removable/Network, 16KB fallback
			if (drive.DriveType == DriveType.Fixed)
			{
				return 8 * 1024;
			}
			
			return 32 * 1024;
		}
		catch
		{
			// Fallback to safe middle ground
			return 16 * 1024;
		}
	}

	/// <summary>
	/// Stops the writer task and flushes any remaining buffered log lines (best-effort).
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

		try
		{
			_channel.Writer.TryComplete();
		}
		catch
		{
			// ignored
		}

		try
		{
			_cts.Cancel();
		}
		catch
		{
			// ignored
		}

		try
		{
			await _writerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
		}
		catch
		{
			// ignored
		}

		_cts.Dispose();
	}
}
