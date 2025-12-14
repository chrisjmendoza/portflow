using System;
using System.IO;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PortFlow.App.Usb;
using PortFlow.Core.Pipeline;
using PortFlow.Core.Pipeline.Steps;

#nullable enable

namespace PortFlow.App;

public partial class MainWindow : Window
{
    private readonly UsbDriveWatcher _usbWatcher = new();
    private string? _currentUsbRoot;
    private bool _watcherInitialized;
    private bool _isRunning;
    private DispatcherTimer? _pollTimer;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
        => await RunPipelineAsync();

    private void OnLoaded(object sender, RoutedEventArgs e)
{
    if (_watcherInitialized) return;

    _usbWatcher.DriveArrived += UsbWatcherOnDriveArrived;
    _usbWatcher.DriveRemoved += UsbWatcherOnDriveRemoved;

    // Start WMI watcher OFF the UI thread so we never hang the window
    _ = Task.Run(() =>
    {
        try
        {
            _usbWatcher.Start();
            Dispatcher.Invoke(() => AppendLine("USB watcher started (WMI)"));
        }
        catch (Exception ex)
        {
       AppendLine("USB watcher failed to start (WMI). Falling back to polling.");
    AppendLine(ex.ToString());
    StartPollingFallback();
}
    });

    // Start polling fallback anyway (cheap + makes this resilient)
    StartPollingFallback();

    _watcherInitialized = true;
}


    private void OnClosed(object? sender, EventArgs e)
    {
        if (_watcherInitialized)
        {
            _usbWatcher.DriveArrived -= UsbWatcherOnDriveArrived;
            _usbWatcher.DriveRemoved -= UsbWatcherOnDriveRemoved;
        }

        _pollTimer?.Stop();
        _pollTimer = null;

        _usbWatcher.Dispose();
    }

    private void StartPollingFallback()
{
    if (_pollTimer != null) return;

    _pollTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    _pollTimer.Tick += (_, __) =>
    {
        var root = ResolveBestRemovableDriveRoot();

        // If we find a removable drive and we didn't already have one, treat as arrival
        if (!string.IsNullOrWhiteSpace(root) && !string.Equals(root, _currentUsbRoot, StringComparison.OrdinalIgnoreCase))
        {
            UsbWatcherOnDriveArrived(root);
        }

        // If none found but we previously had one, treat as removal
        if (string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(_currentUsbRoot))
        {
            UsbWatcherOnDriveRemoved(_currentUsbRoot!);
        }
    };

    _pollTimer.Start();
    AppendLine("USB polling fallback started");
}

private static string? ResolveBestRemovableDriveRoot()
{
    foreach (var d in DriveInfo.GetDrives())
    {
        try
        {
            if (!d.IsReady) continue;
            if (d.DriveType != DriveType.Removable) continue;
            return d.RootDirectory.FullName; // "E:\"
        }
        catch { }
    }
    return null;
}

    private void UsbWatcherOnDriveArrived(string driveRoot)
    {
        Dispatcher.Invoke(async () =>
        {
            var displayRoot = string.IsNullOrWhiteSpace(driveRoot) ? "(unknown)" : driveRoot;
            _currentUsbRoot = string.IsNullOrWhiteSpace(driveRoot) ? null : driveRoot;
            UsbStatusText.Text = $"USB: {displayRoot}";
            AppendLine($"USB ARRIVED: {displayRoot}");

            if (AutoRunCheck.IsChecked == true)
            {
                await RunPipelineAsync();
            }
        });
    }

    private void UsbWatcherOnDriveRemoved(string driveRoot)
    {
        Dispatcher.Invoke(() =>
        {
            _currentUsbRoot = null;
            UsbStatusText.Text = "USB: (none)";
            var message = string.IsNullOrWhiteSpace(driveRoot)
                ? "USB REMOVED"
                : $"USB REMOVED: {driveRoot}";
            AppendLine(message);
        });
    }

    private async Task RunPipelineAsync()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        RunButton.IsEnabled = false;
        OutputBox.Clear();

        try
        {
            var ctx = new PipelineContext
            {
                UsbRootPath = _currentUsbRoot
            };

            var runner = new PipelineRunner(new IPipelineStep[]
            {
                new HelloStep()
            });

            await runner.RunAsync(ctx, CancellationToken.None);

            OutputBox.Text = string.Join(Environment.NewLine, ctx.Log);
        }
        catch (Exception ex)
        {
            OutputBox.Text = ex.ToString();
        }
        finally
        {
            RunButton.IsEnabled = true;
            _isRunning = false;
        }
    }

    private void AppendLine(string text)
    {
        OutputBox.AppendText(text + Environment.NewLine);
        OutputBox.ScrollToEnd();
    }
}
