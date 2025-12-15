# PortFlow

PortFlow is a Windows desktop utility that detects removable USB devices and executes configurable file-processing pipelines when media is inserted.

It is designed for reliability, traceability, and automation — turning “plug in a drive” into a deterministic workflow with clear logging and optional verification.

---

## Key Features

- Detects USB / removable media insertion and removal on Windows
- Automatically runs a processing pipeline on insert (optional)
- Manual run mode for testing and debugging
- Clear, readable run logs in the UI
- Resilient device detection (polling fallback when system events are unavailable)

---

## Current Status

**Early development / prototype stage**

PortFlow currently supports:
- USB detection
- Auto-run and manual pipeline execution
- Example pipeline step (`HelloStep`) for verification and wiring

The architecture is in place to support real workflows such as scanning, copying, and validating files.

---

## Architecture Overview

PortFlow is split into two main projects:

- **PortFlow.App**
  - WPF desktop application
  - USB detection, UI, and lifecycle management
- **PortFlow.Core**
  - Pipeline engine and step abstractions
  - Platform-agnostic logic (no UI or Windows dependencies)

Pipelines are composed of ordered steps implementing a shared interface, allowing workflows to evolve without rewriting the app.

---

## Example Workflow (Planned)

1. User inserts USB drive
2. PortFlow detects removable media
3. Pipeline runs automatically:
   - Scan files
   - Copy to destination
   - Verify results
   - Generate manifest
4. User reviews log output

---

## Why PortFlow Exists

USB-based workflows are common in:
- Manufacturing and CNC environments
- Media ingestion and archiving
- Field data collection
- 3D printing and machine control

PortFlow aims to make these workflows **repeatable, safe, and observable**, instead of manual and error-prone.

---

## Roadmap (High-Level)

- Replace demo step with real `ScanStep`
- Config-driven pipelines
- Safe copy + verification steps
- Manifest and structured logging
- Device allowlisting
- Packaging for non-developer machines

---

## License

License to be determined.