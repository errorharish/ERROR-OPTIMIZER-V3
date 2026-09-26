# ERROR OPTIMIZER - Phase 5 Complete

## Product Goal
The long-term objective of this product is maximum real-world performance while preserving system stability. This is achieved through workload-aware optimization that reduces unnecessary background activity without crippling the core operating system or compromising security.

## Phase 5 Scope
Phase 5 is the final backend expansion before building the WPF GUI. It incorporates the complete suite of performance, cleaning, and networking engines along with the GUI capability catalog (`IOptimizationEngineRegistry`).
Phase 3 expands the foundational engine (built in Phase 1 & 2) by introducing two entirely new optimization domains, heavily protected by strict isolation policies and hardware guardrails:
- **Debloat Subsystem**: Safely removes unnecessary consumer apps, OEM tracking tasks, and cleans Windows recommendations.
- **BIOS Provider Framework**: Allows real, WMI/ACPI-driven configuration of low-risk BIOS features directly from Windows across Dell, HP, Lenovo, and ASUS systems.

### Phase 4 Complete
- Workload detection (Adobe, Topaz, Emulators, Games).
- Dynamic performance profiling & classification.
- Action handlers for Power Plans, CPU Affinity, Priority, HAGS, Pagefile.
- Performance measurement baselining.

### Phase 5 Complete
- Extended Optimization Engines: RAM, Temp, Shader caches, Deep cleanup, Large files, Duplicates.
- Network Engine: DNS flush, TCP auto-tuning, Congestion control, Network throttling.
- Performance Engine: Visual FX, GPU, CPU parking, Mouse/Keyboard, Managed registry health.
- Additional Utilities: Startup apps, Service Debloater, Storage info, Diagnostics, System Restore, God Mode.
- Core Registry: `IOptimizationEngineRegistry` and `IOptimizationScoreEngine` to serve future UI data binding.

The following advanced features are reserved for later phases:
- Advanced tiers (Maximum Workload Performance)
- Scenario Presets
- Production Windows Service
- Named Pipe IPC
- Final WPF GUI
- Kernel driver / Raw Hardware Access

### Phase 3 Now Supports:
- Real Windows detection
- Real service/registry/task/startup inspection/mutation
- Real snapshots, verification, rollback, and restore
- Normal, Pro, Ultimate tiers with profile inheritance
- Mandatory System Restore integrations
- Real AppX/UWP package debloat management
- Real BIOS modifications via OEM providers
- BitLocker pre-flight safety checks
- Extended CLI

## Normal Optimization
The "Normal" tier represents a safe baseline optimization for Windows 10 and Windows 11. It focuses on reversible changes such as:
- Safe Service optimization (e.g., DiagTrack, MapsBroker, RetailDemo)
- Safe Startup trimming (disabling non-critical background applications)
- Reversible Scheduled Task optimization
- Game DVR toggling

## Safety Mechanisms
The BIOS Optimizer uses a comprehensive safety architecture:
- **Snapshots:** Every mutation creates a transactional snapshot before application.
- **Verification:** Every write requires immediate readback verification.
- **Rollback:** Any failed verification triggers automatic rollback of the transaction.
- **Restore:** Users can easily restore any applied tier to its baseline state.
- **Protected Services:** Core Windows services are completely isolated from mutation via an immutable policy.
- **Dry-run:** Planners evaluate logic and mock output without executing mutations (`--dry-run`).
- **Risk Levels:** Each entry has an associated risk level enforcing distinct confirmation policies.

## Build Instructions
1. Run `dotnet restore`
2. Run `dotnet build`
3. Run `dotnet test`
4. Or use the automated build script: `.\build.ps1`

## CLI Commands
- `BiosOptimizer.exe detect` - Display system hardware and capabilities.
- `BiosOptimizer.exe environment` - Display detected environmental contexts.
- `BiosOptimizer.exe workloads` - Display installed gaming/rendering workloads.
- `BiosOptimizer.exe tiers` - List available optimization tiers.
- `BiosOptimizer.exe debloat list` - Enumerates installed, removable consumer/OEM apps.
- `BiosOptimizer.exe bios detect` - Detects firmware capabilities and active OEM provider.
- `BiosOptimizer.exe bios capabilities` - Lists specific writable BIOS settings supported by your machine.
- `BiosOptimizer.exe preview <TierId>` - Plan and summarize changes without mutation (e.g., `preview Debloat` or `preview BiosSafe`).
- `BiosOptimizer.exe apply <TierId> --dry-run` - Run the application logic with safety checks without persisting mutations.
- `BiosOptimizer.exe apply <TierId> --confirm` - Apply optimization and persist results.
- `BiosOptimizer.exe status` - Display last active optimization state.
- `BiosOptimizer.exe restore <TierId>` - Roll back the system to its pre-optimized snapshot.
- `BiosOptimizer.exe diagnostics` - Output raw telemetry for debugging.

## Limitations
- No kernel driver implementation (NullHardwareAccess remains active).
- No Windows Service or GUI.
- Advanced Tier (Maximum Workload Performance) is unsupported.
