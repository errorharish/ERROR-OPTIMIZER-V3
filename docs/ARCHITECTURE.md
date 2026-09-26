# Architecture

## Core Engine
The Core Engine acts as the central orchestrator, executing JSON-defined optimization tiers by abstracting away the low-level Windows APIs. It is fully disconnected from specific environments through interfaces:
- `ITierEngine`: Entry point for Previewing, Applying, and Restoring tiers.
- `IActionRegistry`: Allow-list of safe optimization actions (e.g., `SetServiceStartup`, `SetRegistryValue`).
- `IProfileRepository`: Loads and parses JSON profiles.

## Phase 5 Core GUI Integration
In Phase 5, the architecture was extended to support direct GUI data binding:
- `IOptimizationEngineRegistry`: Serves the complete catalog of backend capabilities to the UI.
- `IOptimizationScoreEngine`: Computes a live health score based on system states (Applied, Remaining, Skipped).

## Detection Layer
The Detection layer interrogates the machine for its state to satisfy environment and workload conditions.
- Uses WMI for hardware specs (`Win32_Processor`, `Win32_VideoController`, etc.).
- Identifies OS version and environmental factors (Laptop, Battery, Touchscreen).
- Detects the presence of key workloads to influence higher-level tiers safely.

## Safety Layer
Safety is the paramount priority. The Safety Layer includes:
- **Snapshots**: Takes pre-execution state logs.
- **Verification**: Asserts the value of an applied configuration.
- **Rollback**: Restores original state if verification fails.
- **Protected Targets**: Immutable list of unmodifiable core OS services.

## Windows Action Layer
Abstracted handlers implement safe modifications. Instead of raw registry calls scattered everywhere, the action layer translates discrete requests to `IRegistryManager`, `IServiceManager`, etc.
This includes safe implementations for Network (e.g. TCP Auto-Tuning), Cleaners (RAM, Temp, Shader Caches), Performance tuning (CPU Parking, Svchost Splitting, Visual FX), and advanced utility diagnostics (Storage, BitLocker, God Mode).
