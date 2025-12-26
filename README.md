# PortFlow

PortFlow is a Windows-based file transfer and backup project focused on **reliable, low-footprint automation**.

This repository contains two related efforts:

- **PortFlowBackup** — a finished, shippable Windows utility (v1.0.0)
- **PortFlow (Core)** — ongoing development toward a larger, configurable file-flow engine

---

## ✅ Download: PortFlowBackup v1.0.0

**PortFlowBackup** is a lightweight Windows utility that automatically runs a `robocopy`-based backup when a specific USB or external drive is inserted.

**Download:**  
➡️ Go to the **[Releases](../../releases)** page and download `PortFlowBackup.zip`

**Note:** The compiled `.exe` files are **not included in this repository**. All executable binaries are published as GitHub Releases only.

### Quick Start
1. Extract `PortFlowBackup.zip`
2. Run `install.cmd` as **Administrator**
3. When prompted, edit `portflow.backup.json`
4. Copy `PORTFLOW_TARGET.txt` to the **root** of your backup USB drive

After setup, backups run automatically whenever the target USB drive is inserted.

### Notes
- Runs silently in the background (system tray)
- No UI, no cloud services, no telemetry
- Logs: `C:\ProgramData\PortFlowBackup\logs`
- Uninstall: run `uninstall.cmd` as Administrator

---

## What’s in this Repository?

### PortFlowBackup (Released Utility)
- Shipping scripts and documentation live under `/shipping/PortFlowBackup`
- Executable binaries are **not** stored in git
- Downloads are provided via **GitHub Releases**

### PortFlow Core (In Development)
- Core libraries and experimental components for a broader file-flow system
- Not yet considered stable or end-user ready
- Subject to change as design evolves

---

## Design Philosophy
- Reliability over features
- Event-driven, not polling
- Minimal system impact
- Safe failure modes
- “Set it and forget it” operation

---

## License
This project is licensed under the **MIT License**.  
See the `LICENSE` file for details.

---

## Disclaimer
This software is provided **as-is**, without warranty.  
Test with non-critical data before relying on it for important backups.
