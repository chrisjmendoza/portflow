# Security Policy

## Overview
PortFlow and PortFlowBackup are local-only Windows utilities.

- No network access
- No telemetry or analytics
- No background services
- No credential handling

All file operations occur locally on the machine using standard Windows APIs and `robocopy`.

## Data Safety
- Users are responsible for verifying backup destinations and source paths
- Always test with non-critical data before relying on automated backups
- Do not run with configuration files from untrusted sources

## Reporting Issues
If you discover a security-related issue:
- Open a GitHub issue with details, or
- Contact the repository owner directly

Please avoid publicly posting sensitive system information.

---

This project prioritizes simplicity, transparency, and predictable behavior.
