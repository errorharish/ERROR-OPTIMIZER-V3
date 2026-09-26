# Error Optimizer Current Architecture

This document describes the active implementation under `src`. The `src_backup`, `scratch`, and `ErrorOptimizer_Ref` trees are not production project inputs.

## Projects

- GUI: `src/BiosOptimizer.GUI/BiosOptimizer.GUI.csproj` (`net8.0-windows10.0.19041`, WPF, assembly `ErrorOptimizer`).
- Backend service: `src/BiosOptimizer.Service/BiosOptimizer.Service.csproj` (Worker service and named-pipe host).
- Core engine: `src/BiosOptimizer.Core/BiosOptimizer.Core.csproj` (profiles, tier execution, action handlers, scoring, cleaners, storage, and optimization engines).
- Detection: `src/BiosOptimizer.Detection/BiosOptimizer.Detection.csproj` (`EnvironmentDetector` and `ProcessSnapshot`).
- Safety: `src/BiosOptimizer.Safety/BiosOptimizer.Safety.csproj` (protected targets, verification, and backup/rollback support).
- IPC: `src/BiosOptimizer.IPC/BiosOptimizer.IPC.csproj` (named-pipe message types and DTO contracts).
- CLI/adapters: `src/BiosOptimizer.CLI/BiosOptimizer.CLI.csproj` (Windows action and provider adapters used by the service).

The solution entry point is `BiosOptimizer.sln`.

## Profiles

Profile JSON files live in `profiles/*.json`, including `Normal.json`, `Pro.json`, `Ultimate.json`, `Debloat.json`, `BiosSafe.json`, and `MaximumPerformance.json`. The service project copies them to its output under `profiles`.

## Navigation

`src/BiosOptimizer.GUI/App.xaml` registers data templates mapping view models to views. `src/BiosOptimizer.GUI/MainWindow.xaml` hosts the shell and navigation commands. `MainWindow.xaml.cs` and its view-model construction path select a view model, which WPF resolves through the application data templates.

The six profile pages share `src/BiosOptimizer.GUI/Views/TierView.xaml` and `src/BiosOptimizer.GUI/ViewModels/TierViewModel.cs`, except Debloat, BIOS Safe, and Maximum Performance, which have specialized view models and views. Registry Tweaks uses `RegistryTweakView.xaml` and `RegistryTweakViewModel.cs`.

## Engine and IPC

The existing Core engine is `src/BiosOptimizer.Core/Implementations/TierEngine.cs`, exposed through `ITierEngine`. It already provides `PreviewAsync` and `ApplyAsync`, evaluates profile conditions against detected machine state, uses the registered action handlers, creates backups before apply, and reports per-action progress/results.

`src/BiosOptimizer.Service/NamedPipeServer.cs` routes IPC messages. Relevant routes include `GetDashboard`, `GetSystemInfo`, `PreviewTier`, `ApplyTier`, `RestoreTier`, `PlanRegistryTweak`, `ApplyRegistryTweak`, `RestoreRegistryTweak`, `GetMachineProfile`, and `RestoreBackup`. The GUI client is `src/BiosOptimizer.GUI/Services/IpcClient.cs`.

## Build and publish

- Standard production script: `build.ps1`.
- Strict publish script: `build_publish.ps1`.
- Intended published GUI artifact: `dist/win-x64/ErrorOptimizer.exe`.
- Intended published backend artifact: `dist/win-x64/BiosOptimizer.Service.exe`.

`build_publish.ps1` stops stale processes, creates a timestamp build ID, removes `dist/win-x64` and source `bin/obj` folders, restores and builds `BiosOptimizer.sln` in Release, publishes the service and GUI for `win-x64`, then checks both executable paths and writes `dist/win-x64/BUILD_MANIFEST.txt`.

The scripts prefer the repository-local SDK at `.dotnet/dotnet.exe`; the current shell does not have a system `dotnet` SDK on PATH.
