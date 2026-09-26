# BiosOptimizer Workload and Performance Optimization

This document outlines the Phase 4 **Maximum Workload Performance** and detection capabilities of BiosOptimizer.

## Overview
BiosOptimizer can detect heavy workloads installed on a system and dynamically apply targeted presets that maximize system resources for that specific workload.

## Supported Workloads
1. **Adobe After Effects**
   - Applies the `AfterEffectsTurbo` preset.
   - Sets process priority to High.
   - Enforces Ultimate Performance Power Plan.

2. **Topaz Video AI / Gigapixel**
   - Applies the `TopazAI` preset.
   - Raises process priority.
   - Enables HAGS (Hardware Accelerated GPU Scheduling).

3. **Android Emulators (BlueStacks, MSI App Player)**
   - Applies the `EmulatorMax` preset.
   - Enables HAGS.
   - Raises emulator process priority.

4. **AAA Games**
   - Applies the `AAAGaming` preset.
   - Maximizes Foreground Application Priority (Win32PrioritySeparation).
   - Enables HAGS and Ultimate Performance Power Plan.

## Hardware Classifications
The environment detector classifies a machine's performance capability:
- **Low-End**: RAM < 8GB OR no SSD OR low core count. Uses `LowEndRescue` preset to enforce pagefile usage and reduce background bloat.
- **Mid-Range**: Standard Windows optimization.
- **High-End**: RAM >= 16GB, NVMe, 8+ cores. Supports aggressive memory and scheduling settings.

## Temporary Session Tracking
Future UI layers can integrate with the `IWorkloadSessionManager` to temporarily apply optimizations while a workload is running, and restore standard behavior when the application is closed.
