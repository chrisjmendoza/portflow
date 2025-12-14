using System;
using System.IO;
using System.Management;

namespace PortFlow.App.Usb;

public sealed class UsbDriveWatcher : IDisposable
{
    private ManagementEventWatcher? _createWatcher;
    private ManagementEventWatcher? _deleteWatcher;

    public event Action<string>? DriveArrived; // "E:\"
    public event Action<string>? DriveRemoved; // "E:\" (best effort)

    public void Start()
    {
        Stop(); // idempotent

        // Creation of a logical disk (WITHIN is required for intrinsic events)
        _createWatcher = new ManagementEventWatcher(new WqlEventQuery(@"
SELECT * FROM __InstanceCreationEvent WITHIN 1
WHERE TargetInstance ISA 'Win32_LogicalDisk' AND TargetInstance.DriveType = 2
"));
        _createWatcher.EventArrived += (_, e) =>
        {
            var root = TryGetRootFromEvent(e) ?? ResolveBestRemovableDriveRoot();
            if (root != null) DriveArrived?.Invoke(root);
        };
        _createWatcher.Start();

        // Deletion of a logical disk
        _deleteWatcher = new ManagementEventWatcher(new WqlEventQuery(@"
SELECT * FROM __InstanceDeletionEvent WITHIN 1
WHERE TargetInstance ISA 'Win32_LogicalDisk' AND TargetInstance.DriveType = 2
"));
        _deleteWatcher.EventArrived += (_, e) =>
        {
            var root = TryGetRootFromEvent(e) ?? string.Empty;
            DriveRemoved?.Invoke(root);
        };
        _deleteWatcher.Start();
    }

    public void Stop()
    {
        if (_createWatcher != null)
        {
            try { _createWatcher.Stop(); } catch { }
            _createWatcher.Dispose();
            _createWatcher = null;
        }

        if (_deleteWatcher != null)
        {
            try { _deleteWatcher.Stop(); } catch { }
            _deleteWatcher.Dispose();
            _deleteWatcher = null;
        }
    }

    private static string? TryGetRootFromEvent(EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent?["TargetInstance"] is ManagementBaseObject inst)
            {
                var deviceId = inst["DeviceID"]?.ToString(); // like "E:"
                if (!string.IsNullOrWhiteSpace(deviceId))
                    return deviceId.EndsWith(@"\") ? deviceId : deviceId + @"\";
            }
        }
        catch { }
        return null;
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

    public void Dispose() => Stop();
}
