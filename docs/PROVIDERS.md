# BIOS Providers

Phase 3 introduces the BIOS Provider Framework, allowing safe, direct configuration of hardware-level BIOS capabilities without entering the BIOS utility manually.

## Vendor Support

We currently implement adapters for:
* **Dell**: Leverages the `root\dcim\sysman\biosattributes` WMI namespace. 
* **HP**: Leverages the `root\hp\instrumentedBIOS` WMI namespace.
* **Lenovo**: Uses the `root\wmi` namespace and manages staged save operations (`Lenovo_SetBiosSetting` followed by `Lenovo_SaveBiosSettings`).
* **ASUS**: Maps logical requests to explicitly allowed ACPI IOCTL methods.
* **Generic**: If the system manufacturer is unknown or the required management software is not installed, the optimizer gracefully falls back to a read-only Generic provider that exposes Secure Boot state, Manufacturer, and Version.

## Capabilities

The framework revolves around discovered capabilities. We only present a capability as writable if:
1. The machine actually exposes it to the provider.
2. It isn't locked by BIOS Admin passwords.
3. It passes our safety check against forbidden settings.

### BIOS Safe Settings
BIOS Safe is a dedicated low-risk tier focusing on convenience features rather than dangerous hardware tuning. Supported settings include `FastBoot`, `QuietBoot`, `WakeOnLan`, and `ErpDeepSleep`.

### Reboot Behavior
Most BIOS configurations require a reboot to be enacted. When applying the `BiosSafe` profile, the engine tracks these pending configurations and transitions the transaction state to `AwaitingReboot`. Verification is automatically deferred until the system has rebooted.

### BitLocker Warning
Any BIOS modification targeting boot-adjacent hardware will automatically trigger a BitLocker detection phase. If BitLocker is active on the host drive, the operation is forcefully blocked and demands manual user `--confirm` acknowledgment. This prevents unintended BitLocker Recovery loops.

### Forbidden Settings
The providers are hardcoded to reject any attempts to modify critical security or system stability settings, regardless of what is requested by the JSON manifest. Exclusions include:
* TPM state
* Secure Boot
* BIOS Passwords
* SataMode
* CSM
* BootOrder
* Firmware flashing

## Provider Limitations
Providers rely exclusively on the WMI or ACPI interfaces supplied by the OEM. If the OEM management layer (e.g., Lenovo Vantage Service or Dell Command) is uninstalled or corrupt, the provider will downgrade to "Unavailable" and fallback to Generic Read-Only mode. We do NOT bundle proprietary software to force capability enablement.
