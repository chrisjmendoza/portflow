PortFlow USB Backup — Setup Instructions
=======================================

PortFlow automatically backs up a chosen folder on your computer to a specific
USB drive (USB stick, SD reader, SSD, or external hard drive).

After you sign in, PortFlow runs in the system tray (near the clock).
You can right-click the tray icon to open logs or exit.
You only need to complete this setup one time.


WHAT YOU NEED
-------------
• Windows 10 or Windows 11
• A USB drive or external hard drive for backups
• Administrator access (you may see a security prompt)


IMPORTANT CONCEPT (PLEASE READ)
-------------------------------
PortFlow does NOT rely on a drive letter (like E:\ or F:\).

Instead, it identifies the correct backup drive by the presence of a special
marker file. This ensures backups always go to the correct device, even if
Windows assigns a different drive letter later.


STEP 1 — PREPARE YOUR BACKUP USB
--------------------------------
1. Plug in the USB drive or external hard drive you want to use for backups.
2. Open the drive in File Explorer.
3. Copy the file named:

   PORTFLOW_TARGET.txt

   into the **root** of the drive (not inside any folder).

Example:
   E:\PORTFLOW_TARGET.txt
   G:\PORTFLOW_TARGET.txt

Only drives containing this file will be used for backups.

NOTES:
• You can have multiple USB drives plugged in — only the one with
  PORTFLOW_TARGET.txt will be used.
• If you ever format this drive or replace it with a new one, simply copy
  PORTFLOW_TARGET.txt onto the new drive.
• External hard drives work the same way as USB sticks.


STEP 2 — EDIT THE BACKUP SETTINGS
---------------------------------
1. In this folder, locate the file:

   portflow.backup.json

2. Right-click the file and choose:
   • “Open with”
   • Select **Notepad** (or any text editor)

   Do NOT open this file in Word or a web browser.

3. Edit the following values:

   "sourcePath"
     → The folder on your computer you want to back up

   "destinationFolderName"
     → The folder name that will be created on the USB drive

4. Click **File → Save**, then close Notepad.


OPTIONAL — BACKUP TO USB ROOT
-----------------------------
By default, PortFlow creates a folder on the USB drive for backups.

If you want files written directly to the root of the USB drive:
• Leave "destinationFolderName" empty, like this:

   "destinationFolderName": ""

In this case, files will be copied directly to the USB root.

This behavior is safe and supported.


STEP 3 — INSTALL (ONE CLICK)
----------------------------
1. Double-click:

   install.cmd

2. If Windows asks for permission, choose **Yes**.

PortFlow USB Backup — Setup Instructions
=======================================

PortFlow automatically backs up a chosen folder on your computer to a specific
USB drive (USB stick, SD reader, SSD, or external hard drive).

Once installed, it runs silently in the background.
You only need to complete this setup one time.


WHAT YOU NEED
-------------
• Windows 10 or Windows 11
• A USB drive or external hard drive for backups
• Administrator access (you may see a security prompt)


IMPORTANT CONCEPT (PLEASE READ)
-------------------------------
PortFlow does NOT rely on a drive letter (like E:\ or F:\).

Instead, it identifies the correct backup drive by the presence of a special
marker file. This ensures backups always go to the correct device, even if
Windows assigns a different drive letter later.


STEP 1 — PREPARE YOUR BACKUP USB
--------------------------------
1. Plug in the USB drive or external hard drive you want to use for backups.
2. Open the drive in File Explorer.
3. Copy the file named:

   PORTFLOW_TARGET.txt

   into the **root** of the drive (not inside any folder).

Example:
   E:\PORTFLOW_TARGET.txt
   G:\PORTFLOW_TARGET.txt

Only drives containing this file will be used for backups.

NOTES:
• You can have multiple USB drives plugged in — only the one with
  PORTFLOW_TARGET.txt will be used.
• If you ever format this drive or replace it with a new one, simply copy
  PORTFLOW_TARGET.txt onto the new drive.
• External hard drives work the same way as USB sticks.


STEP 2 — EDIT THE BACKUP SETTINGS
---------------------------------
1. In this folder, locate the file:

   portflow.backup.json

2. Right-click the file and choose:
   • “Open with”
   • Select **Notepad** (or any text editor)

   Do NOT open this file in Word or a web browser.

3. Edit the following values:

   "sourcePath"
     → The folder on your computer you want to back up

   "destinationFolderName"
     → The folder name that will be created on the USB drive

4. Click **File → Save**, then close Notepad.


OPTIONAL — BACKUP TO USB ROOT
-----------------------------
By default, PortFlow creates a folder on the USB drive for backups.

If you want files written directly to the root of the USB drive:
• Leave "destinationFolderName" empty, like this:

   "destinationFolderName": ""

In this case, files will be copied directly to the USB root.

This behavior is safe and supported.


STEP 3 — INSTALL (ONE CLICK)
----------------------------
1. Double-click:

   install.cmd

2. If Windows asks for permission, choose **Yes**.

After installation, PortFlow is copied to:

   C:\ProgramData\PortFlowBackup\

You may delete this setup folder after installation is complete.

This installs PortFlow to run automatically in the background.
No window will appear during normal operation.


HOW IT WORKS
------------
• After you sign in, PortFlow runs in the system tray (near the clock).
• After you sign in, it automatically runs a backup whenever you plug in the correct USB drive.
• If the USB is not plugged in, nothing happens.
• If the wrong USB is plugged in, it is ignored.
• Drive letters (E:\, F:\, etc.) do not matter — detection is automatic.


LOG FILES
---------
Logs are stored on your computer and can be opened with Notepad.

Location:
   C:\ProgramData\PortFlowBackup\logs\

The log file shows when backups start, finish, and if any errors occur.


UNINSTALLING
------------
To remove PortFlow from your system:
1. Double-click:

   uninstall.cmd

This removes the background task.
No files on your USB drive are deleted.


TROUBLESHOOTING
---------------
If backups do not run:
• Make sure PORTFLOW_TARGET.txt is present on the USB drive
• Make sure the USB drive is plugged in
• Check the log files listed above

That’s it — once installed, PortFlow runs automatically.


This installs PortFlow to run automatically in the background.
No window will appear during normal operation.


HOW IT WORKS
------------
• PortFlow runs automatically after you log in.
• When the correct USB drive is detected, a backup runs.
• If the USB is not plugged in, nothing happens.
• If the wrong USB is plugged in, it is ignored.
• Drive letters (E:\, F:\, etc.) do not matter — detection is automatic.


LOG FILES
---------
Logs are stored on your computer and can be opened with Notepad.

Location:
   C:\ProgramData\PortFlowBackup\logs\

The log file shows when backups start, finish, and if any errors occur.


UNINSTALLING
------------
To remove PortFlow from your system:
1. Double-click:

   uninstall.cmd

This removes the background task.
No files on your USB drive are deleted.


TROUBLESHOOTING
---------------
If backups do not run:
• Make sure PORTFLOW_TARGET.txt is present on the USB drive
• Make sure the USB drive is plugged in
• Check the log files listed above

That’s it — once installed, PortFlow runs automatically.
