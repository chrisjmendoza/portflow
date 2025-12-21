using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PortFlow.Runner;

internal sealed class AsyncFileLogger : IAsyncDisposable
{
	private readonly Channel<string> _channel;
	private readonly CancellationTokenSource _cts = new();
	private readonly Task _writerTask;
	private readonly bool _writeToConsole;

	private readonly int _flushEveryLines;
	private readonly TimeSpan _flushInterval;

	private long _droppedCount;
	private int _disposed;

	private AsyncFileLogger(string logPath, bool writeToConsole, int bufferCapacity, int flushEveryLines, TimeSpan flushInterval)
	{
		_writeToConsole = writeToConsole;
		_flushEveryLines = flushEveryLines;
		_flushInterval = flushInterval;

		var absolutePath = Path.GetFullPath(logPath);
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

	public static AsyncFileLogger Create(string logPath, bool writeToConsole)
		=> new(logPath, writeToConsole, bufferCapacity: 4096, flushEveryLines: 50, flushInterval: TimeSpan.FromMilliseconds(400));

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
			Interlocked.Increment(ref _droppedCount);
		}
	}

	private async Task WriterLoopAsync(string absolutePath, CancellationToken ct)
	{
		try
		{
			await using var stream = new FileStream(
				absolutePath,
				FileMode.Append,
				FileAccess.Write,
				FileShare.Read,
				bufferSize: 64 * 1024,
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
