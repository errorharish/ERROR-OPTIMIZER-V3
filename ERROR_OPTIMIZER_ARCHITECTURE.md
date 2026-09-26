# Error Optimizer Architecture Analysis

## Current Architecture
- **GUI**: WPF using MVVM pattern (BiosOptimizer.GUI).
- **Service Layer**: Elevated .NET Worker Service (BiosOptimizer.Service).
- **IPC**: NamedPipe based communication.
- **Engines**: Basic Action-based engine (IOptimizationAction).
- **Theme**: Basic Dark/Red theme.

## Upgraded Architecture Needs (Phase 2-5)
- **MachineIntelligenceEngine**: Requires WMI, Registry, and Hardware querying to build a comprehensive MachineProfile.
- **Workload Profile**: Dynamic detection of gaming, creative, and streaming applications.
- **OptimizationPlan**: The static list must be replaced with a Machine-Aware rule engine that evaluates Risk, Dependencies, and OS Gates.
- **Safety Engine**: Must implement strict Read -> Dry Run -> Backup -> Apply -> Verify -> Rollback pipeline.
