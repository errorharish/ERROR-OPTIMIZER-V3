# Development Guide

## Setup
The engine builds as a cross-platform .NET solution but explicitly targets `win-x64` due to its deep reliance on WMI, Registry, and Windows Services.

## Build Scripts
The `build.ps1` script handles restoring, building, and publishing the solution as a self-contained single-file executable for ease of deployment.

## Testing
- Tests are written in xUnit.
- Tests rely strictly on mocked components (`MockServiceManager`, `MockRegistryManager`) to prevent unit testing from mutating the developer's actual machine.
- Execute tests via `dotnet test`.

## Extending Actions
To introduce a new optimization capability:
1. Define the handler in `BiosOptimizer.Core`.
2. Map the action string to the handler in the `ActionRegistry`.
3. Add the corresponding OS-abstraction interface to `BiosOptimizer.Safety` or `BiosOptimizer.Detection`.
4. Implement the concrete OS class.
5. Create unit tests for both logic and validation.
