# ARCHITECTURE FORENSIC ANALYSIS

**Project:** ERROR OPTIMIZER
**Date Generated:** 2026-08-20

## Overview of Existing Architecture
The Error Optimizer consists of a WPF GUI (`BiosOptimizer.GUI`) communicating with an elevated Windows Service (`BiosOptimizer.Service`) via Named Pipes (`BiosOptimizer.IPC`). The optimization logic is spread across `BiosOptimizer.Core`, `BiosOptimizer.Detection`, and `BiosOptimizer.Safety`.

## Current Architectural Coupling & Weaknesses

### 1. GUI-Backend Entanglement
- The ViewModels (e.g., `InputOptimizerViewModel`, `TierViewModel`) directly handle parsing raw JSON plans and maintaining deep state about individual optimization steps.
- Some ViewModels (e.g., `DashboardViewModel`) may be directly instantiating heavy operations or rapidly polling instead of relying on a centralized application state.
- **Risk:** If a ViewModel crashes or unloads improperly, it leaves orphaned tasks, lost IPC responses, or corrupted UI state.

### 2. IPC Protocol Limitations
- IPC uses `IpcClient.cs` on the GUI and `NamedPipeServer.cs` on the Service.
- Currently, IPC communication relies on string-based commands (`IpcMessageType`) and loosely-typed JSON payloads without explicit versioning.
- **Risk:** Renaming a property in a DTO on the service immediately breaks JSON parsing in the GUI, leading to silent failures or exceptions in ViewModels.

### 3. Service Lifecycle & Initialization Blocking
- The Windows Service likely does heavy discovery (WMI queries, registry scans) upon receiving connection requests rather than asynchronously pre-warming data.
- **Risk:** If a query hangs (common with WMI), the service blocks the IPC pipe, causing the GUI to stick in "Connecting to Service..." forever.

### 4. Fragmented Monitoring & Polling
- Multiple pages instantiate their own timers for monitoring or WMI queries.
- **Risk:** Memory leaks via un-disposed `DispatcherTimer` or `ManagementEventWatcher` instances. Creates high CPU usage overhead and can deadlock the UI thread.

### 5. ContentHost Safety & Unhandled Exceptions
- Standard WPF frame navigation pattern doesn't inherently protect against nested exceptions.
- **Risk:** A faulty `ResourceDictionary` lookup or null reference exception inside a `catch` block often results in `Content = null;` displaying a blank screen to the user.

## Architectural Boundaries to Establish
1. **WPF UI (Views & ViewModels):** Must handle rendering and data binding only. No direct system calls. Must gracefully display degraded states.
2. **Application Services (GUI):** A centralized `MetricsService`, `NavigationService`, and `IpcManager` layer.
3. **Optimization Engine (Service):** Agnostic to UI. Returns structured, versioned DTOs (`OptimizationPlan`, `VerificationResult`).
4. **Safety & Backup:** Strict checkpointing before applies.

This forensic analysis serves as the foundation for the upcoming hard architectural boundaries.
