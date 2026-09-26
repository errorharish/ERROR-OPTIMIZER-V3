# Safety

The system adheres to an extremely rigid safety model designed to preserve machine stability across unpredictable environments.

## The Snapshot System
Before any change is executed on the underlying system, the optimization engine generates a local backup (Snapshot) of the state.
Snapshots are persisted locally under `%ProgramData%\BiosOptimizer\transactions\`.
Each transaction bundles multiple state changes ensuring holistic rollback capability.

## Verification
A change is only considered "Successful" if the engine reads back the expected value after applying it. If verification fails, a Rollback is automatically triggered.

## Phase 5 Cleaners & Utils Safety
- **Cleaners**: Never auto-delete active user data, browser databases, game saves, or project files. Large/Duplicate scanners only identify paths; explicit user confirmation is strictly required. RAM cleanup triggers garbage collection instead of undocumented volatile hacks.
- **Service Debloater**: Implements a tier list. Essential services fall under the Protected policy, ensuring core OS functionality remains robust regardless of requested debloat intensity.

## Protected Targets Policy
This absolute immutable policy prevents modification of critical Windows services. The list is hardcoded in the safety layer and cannot be bypassed via CLI, JSON manifest, or GUI. Targets include:
- `WinDefend`
- `RpcSs`
- `DcomLaunch`
- `EventLog`
- `PlugPlay`
- etc.

Phase 3 extends this policy for **Debloat**: Core Windows AppX packages like `Microsoft.Windows.SecHealthUI`, `.NET.Native.Framework`, and `WindowsStore` are strictly forbidden from removal.

## BIOS Safety and BitLocker
BIOS modifications inherently carry a higher risk to system stability and boot capability.
* **Absolute Exclusions:** The BIOS Provider Framework contains hardcoded blocks against modifying TPM, Secure Boot, CSM, SataMode, Overclocking, Undervolting, and Firmware flashing.
* **BitLocker Protection:** Modifying boot-adjacent BIOS parameters can trip BitLocker into Recovery mode. The `TierEngine` explicitly checks for active BitLocker protection and forcefully upgrades the operation risk, requiring explicit user `--confirm` acknowledgment.

## Risk Model
Optimization targets define a `Risk` level (`Low`, `Medium`, `High`, `Critical`).
- `Low`: Auto-applied without warning during application.
- `Medium`: Explicit confirmation required.
- `High`: Explicit confirmation and warnings required.
- `Critical`: Blocked in Phase 1 execution.
