# OpenCore ACPI override

OpenCore 1.0.8 loads before Windows, injects one SSDT, and applies two binary patches. It does not flash BIOS, load Windows kernel drivers, or create BCD boot entries.

---

## What is loaded

| Item | Role |
| :--- | :--- |
| `SSDT-PREYDEV.aml` | Isolated `PNP0C14` device `\_SB.PRYW` (UID `PREY`) registering `PreySenseControl` in `root\WMI` |
| PCCC patch | Raises Intel IPF advertised PL1 ceiling from 85 W to 999 W |
| SSDO `E3DT` patch | `E3DT(0x0E)` → `E3DT(0x0D)` — GPU-to-CPU cross-throttle suppression |

OEM `_Q30` and `WMBH` are not renamed. The physical Turbo key still goes through Acer firmware; PreySense only listens.

`SSDT-PREYDEV` has **no** `_INI` power writes. Limits change when PreySense calls WMI after Windows is up.

`SSDT-PREYCPU` and `SSDT-PREYBOOST` are not part of the shipped `config.plist`.

OpenCore security for this package: `Vault=Optional`, `SecureBootModel=Disabled`, `ScanPolicy=0`, Apple SMBIOS spoof off, driver list = `OpenRuntime.efi` only.

---

## `PreySenseControl` methods

See [CPU](cpu_power_limits_acpi.md) and [GPU](gpu_power_limits_nvpcf.md). Summary: 1 version, 2 ping (`PREY`), 3–4 CPU PL1/PL2/Tau, 5–6 GPU watts, 7–8 WMG3.

UI gate:

```text
IsAcpiTuningAllowed = IsOpenCoreLoaded && IsAcpiOverrideEnabled
```

Detection (`OpenCoreSecurityValidator`): NVRAM `opencore-version` (`{4D1FDA02-38C7-4A6A-9CC6-4BCCA8B30102}`), ACPI OEM/table ids containing `PREY`, firmware table bytes, or the OpenCore firmware-environment registry key.

---

## Permanent install (hidden `PREYSENSE` partition)

From Settings → **Install to Boot Drive**, or:

```powershell
powershell -ExecutionPolicy Bypass -File tools\OpenCore_PreyBoost_EFI\Install-To-ESP.ps1
```

Admin required. Behavior:

1. If a volume labeled `PREYSENSE` already exists (or a leftover 8–32 MB EFI/legacy PreySense GPT partition), delete it and return that space to Windows.
2. Shrink the **Windows** partition by **16 MB**, create a new GPT **EFI System** partition, format **FAT**, label `PREYSENSE`, set **no default drive letter**.
3. Copy `EFI\BOOT\BOOTX64.EFI`, `EFI\OC\OpenCore.efi`, `EFI\OC\Drivers\OpenRuntime.efi`, `EFI\OC\ACPI\SSDT-PREYDEV.aml`, `config.plist`.

No `bcdedit /create`. Acer’s `A01ModifyBootInfoDxe` resets Windows Boot Manager to first unless **BIOS boot order** is set.

### BIOS enrollment (required)

1. BIOS → Security → supervisor password if prompted, reboot.
2. Security → **Select an UEFI file as trusted for executing**, three files:
   - `PREYSENSE\EFI\BOOT\BOOTX64.EFI`
   - `PREYSENSE\EFI\OC\OpenCore.efi`
   - `PREYSENSE\EFI\OC\Drivers\OpenRuntime.efi`
3. Boot → PreySense first → Save.

Do not replace `EFI\Microsoft\Boot\bootmgfw.efi` on the Windows ESP. That path is unsupported.

---

## USB test

Settings → Install to USB, or `Prepare-USB.ps1`. FAT32 stick, F12 → USB. Unplug to boot stock Windows.

---

## Uninstall

Settings → Uninstall, or `Uninstall-From-ESP.ps1`. Deletes the dedicated `PREYSENSE` partition and expands Windows.

Then set BIOS boot order back to Windows Boot Manager. Re-enroll is unnecessary after the partition is gone.

---

## After a successful boot

Settings ACPI status shows OpenCore loaded. Enable **ACPI Override**. Mode sliders and Games unlock. PL1 Dynamic in a load test should follow the Mode values instead of snapping to 85 W (once PCCC is patched above 85).
