PortFlow USB Backup — Troubleshooting
====================================

BACKUP DID NOT RUN
------------------
• Make sure you are signed in to Windows (PortFlow starts after login and runs in the system tray)
• Make sure the USB drive is plugged in
• Make sure PORTFLOW_TARGET.txt is on the USB drive root (for example: E:\PORTFLOW_TARGET.txt)
• Try unplugging and re-plugging the USB drive
• Wait a few seconds — large backups can take time

CONFLICT: MULTIPLE TARGET DRIVES
-------------------------------
If two or more drives have PORTFLOW_TARGET.txt, PortFlow will skip the backup
to avoid copying to the wrong device.
Unplug extra target drives and plug in only the correct one.

CHECK THE LOGS
--------------
Log files are located at:
C:\ProgramData\PortFlowBackup\logs\

You can also right-click the PortFlow tray icon to open logs.

Open the most recent log file with Notepad to see details.

USB WILL NOT EJECT
------------------
If Windows says the device is in use:
• A backup may still be running
• Wait until copying finishes
• Try ejecting again

REINSTALLING
------------
If needed:
1. Run uninstall.cmd
2. Run install.cmd again

This does not delete any files on your USB drive.
