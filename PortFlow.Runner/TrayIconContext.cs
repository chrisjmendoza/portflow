using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PortFlow.Runner;

internal sealed class TrayIconContext : ApplicationContext
{
	private readonly NotifyIcon _notifyIcon;
	private readonly Action _onExitRequested;
	private readonly Action<string> _log;
	private readonly string _logFilePath;
	private readonly string _logsFolderPath;

	public TrayIconContext(string toolTipText, string logFilePath, Action<string> log, Action onExitRequested)
	{
		_onExitRequested = onExitRequested;
		_log = log;
		_logFilePath = logFilePath;
		_logsFolderPath = ResolveLogsFolder(logFilePath);

		var contextMenu = new ContextMenuStrip();
		var openLogsFolderItem = new ToolStripMenuItem("Open Logs Folder");
		openLogsFolderItem.Click += (_, _) =>
		{
			try
			{
				Directory.CreateDirectory(_logsFolderPath);
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = _logsFolderPath,
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				_log($"Tray action failed (Open Logs Folder): {ex.Message}");
			}
		};

		var openLogFileItem = new ToolStripMenuItem("Open Log File");
		openLogFileItem.Click += (_, _) =>
		{
			try
			{
				Directory.CreateDirectory(_logsFolderPath);
				if (!File.Exists(_logFilePath))
				{
					File.AppendAllText(_logFilePath, string.Empty);
				}

				Process.Start(new ProcessStartInfo
				{
					FileName = "notepad.exe",
					Arguments = $"\"{_logFilePath}\"",
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				_log($"Tray action failed (Open Log File): {ex.Message}");
			}
		};

		var exitItem = new ToolStripMenuItem("Exit");
		exitItem.Click += (_, _) =>
		{
			try
			{
				_onExitRequested();
			}
			finally
			{
				ExitThread();
			}
		};
		contextMenu.Items.Add(openLogsFolderItem);
		contextMenu.Items.Add(openLogFileItem);
		contextMenu.Items.Add(new ToolStripSeparator());
		contextMenu.Items.Add(exitItem);

		_notifyIcon = new NotifyIcon
		{
			Icon = SystemIcons.Application,
			ContextMenuStrip = contextMenu,
			Text = NormalizeTooltip(toolTipText),
			Visible = true
		};
	}

	protected override void ExitThreadCore()
	{
		_notifyIcon.Visible = false;
		_notifyIcon.Dispose();
		base.ExitThreadCore();
	}

	private static string ResolveLogsFolder(string logFilePath)
	{
		try
		{
			var folder = Path.GetDirectoryName(Path.GetFullPath(logFilePath));
			if (!string.IsNullOrWhiteSpace(folder)) return folder;
		}
		catch
		{
			// ignored
		}

		return @"C:\ProgramData\PortFlowBackup\logs";
	}

	private static string NormalizeTooltip(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return "PortFlow";
		// NotifyIcon.Text is limited (Windows truncates; WinForms enforces 63 chars).
		return text.Length <= 63 ? text : text.Substring(0, 63);
	}
}
