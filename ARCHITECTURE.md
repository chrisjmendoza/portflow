# PortFlow Architecture

This document describes the high-level structure and design intent of PortFlow.

---

## Design Goals

- Reliability over cleverness
- Deterministic behavior
- Clear separation of concerns
- Minimal UI coupling to business logic

---

## Project Structure

### PortFlow.App
- WPF desktop application
- Owns:
  - USB detection
  - Application lifecycle
  - User interface
- Coordinates pipeline execution but does not implement pipeline logic

### PortFlow.Core
- Platform-agnostic library
- Owns:
  - Pipeline runner
  - Pipeline step contracts
  - Execution context and logging
- Designed to be reusable and testable without UI

---

## Pipeline Model

A pipeline is an ordered list of steps:

```text
PipelineRunner
 ├─ Step 1
 ├─ Step 2
 └─ Step N
Each step:

Has a name

Receives a shared execution context

Runs asynchronously

Appends log entries

This allows workflows to evolve by adding or replacing steps without rewriting the runner.

USB Detection Strategy
PortFlow uses a resilient approach:

Attempts to subscribe to system USB events

Falls back to polling removable drives when events are unavailable or unreliable

This ensures the application continues functioning across different Windows configurations.

Future Extensions
Configuration-defined pipelines

Multiple pipeline profiles

Background / tray execution

Structured logging and manifests