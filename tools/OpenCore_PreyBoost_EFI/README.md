# PreySense OpenCore EFI

Minimal OpenCore 1.0.8 package for runtime PreySense power control on the Acer Predator Helios Neo 16S (`PHN16S-71`).

It loads before Windows. It does not flash the BIOS, load unsigned Windows drivers, or add BCD boot entries.

## What it does

- Injects **one** SSDT: `SSDT-PREYDEV.aml` — isolated `PNP0C14` WMI device (`PreySenseControl`). No boot-time `_INI` power writes.
- Patches Intel **PCCC** so IPF’s advertised PL1 ceiling is 999 W.
- Patches SSDO **E3DT** `0x0E` → `0x0D` (GPU-to-CPU cross-throttle).
- Leaves OEM `_Q30`, `WMBH`, fans, and profile tables in place.
- Sets `SyncRuntimePermissions` so Windows does not bugcheck `0x7E` when Secure Boot is on and the BIOS memory attributes table marks `OpenRuntime.efi` non-executable.

Live PL1/PL2/Tau/GPU TGP/WMG3 are written later from Windows by PreySense.

## Layout

```text
tools/OpenCore_PreyBoost_EFI/
├── EFI/
│   ├── BOOT/BOOTX64.EFI
│   └── OC/
│       ├── ACPI/SSDT-PREYDEV.aml
│       ├── Drivers/OpenRuntime.efi
│       ├── OpenCore.efi
│       └── config.plist
├── Prepare-USB.ps1          USB copy (FAT32)
├── Install-To-ESP.ps1       Hidden PREYSENSE partition
└── Uninstall-From-ESP.ps1   Remove the dedicated PreySense partition
```

The same tree is embedded in PreySense.exe. Settings → ACPI Override runs these scripts from a staging folder under `%LocalAppData%\PreySense\OpenCoreStaging\`.

## USB test

```powershell
powershell -ExecutionPolicy Bypass -File Prepare-USB.ps1 -DriveLetter E:
```

Reboot, F12, pick the USB drive. Unplug to return to stock Windows.

## Permanent install

```powershell
powershell -ExecutionPolicy Bypass -File Install-To-ESP.ps1
```

Admin required.

1. Reuses a volume labeled **PREYSENSE** if it exists (mount, copy, dismount). Otherwise shrinks Windows by **16 MB** and creates a FAT EFI System partition with that label and **no Windows drive letter**.
2. Copies the EFI tree above. Does **not** write BCD.

Then in BIOS:

1. Security → supervisor password if asked.
2. **Select an UEFI file as trusted for executing**, three times:
   - `PREYSENSE` → `EFI` → `BOOT` → `BOOTX64.EFI`
   - `PREYSENSE` → `EFI` → `OC` → `OpenCore.efi`
   - `PREYSENSE` → `EFI` → `OC` → `Drivers` → `OpenRuntime.efi`
3. Boot order → PreySense first → Save.

Acer firmware puts Windows Boot Manager first on every reboot unless BIOS order is set. Do not hijack `bootmgfw.efi` on the Windows ESP.

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File Uninstall-From-ESP.ps1
```

Deletes the `PREYSENSE` partition and expands Windows. Set BIOS boot order back to Windows Boot Manager.

## After install

In PreySense Settings, ACPI status should show OpenCore loaded. Turn on **Enable ACPI Override** to unlock Mode and Games watt limits.

Full mechanism: [docs/opencore_acpi_patching.md](../../docs/opencore_acpi_patching.md).
