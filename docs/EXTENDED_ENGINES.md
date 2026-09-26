# Phase 5: Extended Optimization Engines

Phase 5 introduces the full suite of optimization capabilities that will be exposed in the final UI.

## Engine Catalog

### 1. Cleaners
- **RAM Cleanup**: Safely reclaims memory by invoking GC algorithms. Does not use unsafe undocumented memory wiping that causes system micro-stutters.
- **Fast Temp Clean**: Clears user and system temp folders safely.
- **Shader Cache Cleanup**: Cleans DirectX, NVIDIA, and AMD shader caches.
- **Deep System Cleanup**: Targets Windows Update caching and prefetch data safely.
- **Large & Duplicate Scanners**: Finds candidates for removal. Never auto-deletes user data.

### 2. Network Engine
- **DNS Flush**: Clears local resolver caches.
- **TCP Auto-Tuning**: Configures the TCP sliding window scaling factor.
- **Congestion Control**: Tweaks provider to CTCP or CUBIC.
- **Network Throttling**: Disables the Multimedia Class Scheduler's network throttling index for better real-time networking (e.g. gaming).

### 3. Performance Engine
- **Visual FX**: Minimizes unnecessary UI animations via the registry.
- **GPU Performance**: Leverages HAGS and standard graphic preferencing without manipulating physical driver binaries.
- **CPU Parking**: Tweaks core parking dynamically for high-core count systems.
- **Mouse & Keyboard Optimization**: Tunes standard polling / repeat delays natively supported by Windows. No fake registry hacks.
- **Managed Registry Health**: Detects if managed state (like GameDVR or Telemetry) has drifted from optimized values and allows repair.
- **RAM-Aware Svchost Split Threshold**: Configures the `SvchostSplitThresholdInKB` value natively based on detected physical RAM according to the absolute authoritative mapping:
  - (default) → 0x3800000
  - 4 GB → 0x4000000
  - 6 GB → 0x600000
  - 8 GB → 0x800000
  - 12 GB → 0xC00000
  - 16 GB → 0x1000000
  - 24 GB → 0x1800000
  - 32 GB → 0x2000000
  - 64 GB → 0x4000000
  - Unsupported/Unlisted RAM capacities are inherently skipped/blocked (no extrapolation occurs).

### 4. Utilities
- **Diagnostics**: Enumerates CPU, RAM, GPU, and security states (Secure Boot, TPM).
- **Storage**: Lists logical drive configuration and available capacity.
- **Startup & Services**: Identifies autoruns and debloats non-critical Windows services using the standard Safety Pipeline.
- **God Mode**: Creates a native Windows configuration shortcut on the desktop.

## Safety Pipeline Integration
Every mutable engine here is wired into `ITierEngine` or uses the exact same `Snapshot -> Verify -> Rollback` architecture designed in Phases 1-4. 
