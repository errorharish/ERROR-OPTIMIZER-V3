# Optimization Tiers

## Normal (Phase 1)
The universal safety tier. Low risk, conservative reductions in Windows background tasks. Designed to reduce 15-30 background processes. Validated on Windows 10 and 11.

## Pro (Phase 2)
Balanced tier targeting latency and scheduling, enabling some hardware features securely.

## Ultimate (Phase 2)
Aggressive gaming and encoding performance optimizations, including memory management tuning, process affinities, and latency optimizations. Protected by mandatory System Restore points.

## Debloat (Phase 3)
Safe removal of unnecessary consumer/OEM software and background clutter (e.g. TikTok, Spotify, Candy Crush) while preserving core Windows functionality. Composable with other tiers.

## BIOS Safe (Phase 3)
Safely discover and modify only explicitly approved low-risk BIOS settings exposed by supported vendors (e.g., FastBoot, QuietBoot, WakeOnLan) using WMI/ACPI integrations. Boot-adjacent modifications automatically trigger BitLocker safety checks.

## Phase 4 Additions

### Tier 6: Maximum Workload Performance
**Goal**: Target specific workloads and dynamically prioritize them at the expense of non-essential Windows systems.
**Key Features**:
- **Workload Detectors**: Auto-detect Adobe After Effects, Emulators, Topaz AI, AAA Games.
- **Dynamic Resource Allocation**: Modifies CPU Affinity, Process Priority, Foreground Separation, and Power Plans while the workload runs.
- **Safety**: Fully reversible via `WorkloadSessionManager`, avoids RealTime priority to prevent system lockups.
- **Presets**: `AfterEffectsTurbo`, `AAAGaming`, `EmulatorMax`, `TopazAI`, `LowEndRescue`, `TotalMax`.

## Phase 5 Additions
Phase 5 introduces the comprehensive suite of Extended Optimization Engines designed to fuel the final GUI. These features behave as capability-driven optimizations mapped to categories rather than sequential tiers.
- **Cleaners**: RAM, Temp, Shader Caches, Deep Windows Updates, Large/Duplicate Files.
- **Network**: DNS, TCP Auto-Tuning, Congestion Control, Throttling overrides.
- **Performance**: Visual FX, GPU, CPU Parking, Mouse/Keyboard responsiveness, and RAM-aware Svchost splitting.
- **Utilities**: Diagnostics, Storage, God Mode, Service Debloater.
