using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PortFlow.Runner;

/// <summary>
/// WinForms <see cref="ApplicationContext"/> that hosts the system tray icon and menu for the headless runner.
/// </summary>
/// <remarks>
/// The tray context is responsible for exposing basic UX for a background process:
/// open logs, open log file, and request graceful shutdown.
/// </remarks>
internal sealed class TrayIconContext : ApplicationContext
{
	private readonly Icon _icon;
	private readonly NotifyIcon _notifyIcon;
	private readonly Action _onExitRequested;
	private readonly Action<string> _log;
	private readonly string _logFilePath;
	private readonly string _logsFolderPath;

	/// <summary>
	/// Creates the tray icon and context menu.
	/// </summary>
	/// <param name="toolTipText">Tooltip text (will be truncated to Windows limits).</param>
	/// <param name="logFilePath">Path to the runner log file.</param>
	/// <param name="log">Log sink for tray-related actions.</param>
	/// <param name="onExitRequested">Callback invoked when the user selects Exit.</param>
	public TrayIconContext(string toolTipText, string logFilePath, Action<string> log, Action onExitRequested)
	{
		_onExitRequested = onExitRequested;
		_log = log;
		_logFilePath = logFilePath;
		_logsFolderPath = ResolveLogsFolder(logFilePath);

		try
		{
			var iconPath = Path.Combine(AppContext.BaseDirectory, "portflow.ico");
			_icon = new Icon(iconPath);
		}
		catch (Exception ex)
		{
			_log($"Failed to load tray icon (portflow.ico): {ex.Message}");
			throw;
		}

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
			Icon = _icon,
			ContextMenuStrip = contextMenu,
			Text = NormalizeTooltip("PortFlow Backup"),
			Visible = true
		};
	}

	protected override void ExitThreadCore()
	{
		_notifyIcon.Visible = false;
		_notifyIcon.Dispose();
		_icon.Dispose();
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
