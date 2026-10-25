# PreySense review (fork gfsaaser24/PreySense)

Date: 2026-10-25. Base: upstream `91a7424` (v2.3.5). Read-only review of the code, plus facts tested on our own PHN16S-71 (BIOS V1.28). Nothing was run against hardware, partitions, or firmware.

Goal: replace PredatorSense so we can stop the two Acer services that leak ~900 handles/min each (`AASSvc` = AcerCentralService.exe, `ASMSvc` = AcerSystemCentralService.exe). PreySense does not talk to those services, so the goal is reachable.

Authorship: one author; the 8 newest release commits (v2.0.5 to v2.3.5) are co-authored by the Cursor agent (model not recorded). No outside review. Each commit is a whole release.

## Facts tested on this machine

| Read | Result | Meaning |
|---|---|---|
| `GetGamingMiscSetting(0x0B)` | `0x200` | EC operating mode = **2** (not in 0/1/4/5/6) |
| `GetGamingMiscSetting(0x0A)` | `0x7300` | supported-modes mask `0x73` = Quiet, Balanced, Performance, Turbo, Eco; bit 2 **not** set |
| `GetGamingMiscSetting(0x02)` | `0x300` | GPU mux = 3 (code reads it as Automatic) |
| `GetBiosOptions` (empty 128-byte password) | rc 0, 2048 bytes | read works without a password |
| PredatorSense + Windows "Ultimate Performance" plan | hides Performance/Turbo ("On Battery Power") | fixed by the "Acer" plan |
| Restarting AASSvc/ASMSvc | PredatorSense drops its TCP links and falls back to the battery mode list | why a 60 s restart guard breaks PredatorSense |
| Disk 0 partitions | EFI 512 MB, MSR 16 MB, C:, Recovery 1 GB | no hidden Acer partition |
| `HKLM\HARDWARE\ACPI` | DSDT, FADT, SSD1..SSDU, `SSDT\ACRSYS\ACRPRDCT\00001000`, `\00003000` | Acer power tables come from the BIOS firmware |

## Why OpenCore at all

The CPU/GPU power limits are constants inside Acer's ACPI tables (the `ACRSYS ACRPRDCT` SSDTs) and the EC. Those tables live in the BIOS flash chip, not on the disk. The firmware hands them to Windows at every boot, and Windows cannot change them (a registry ACPI override only works with test signing). OpenCore is a boot loader that runs before Windows and patches those tables in memory, then starts Windows. That is the only way PreySense can change the watt limits without flashing the BIOS.

## Got right (keep)

- Mode write `0x0B | mode<<8`, read bits 8..15; status byte `(out & 0xFF) == 0` (`WmiController.cs:537,572,155`).
- Fan behavior payloads `0x410009` Auto, `0x820009` Max, `0xC30009` Custom; unknown falls back to Auto (`WmiController.cs:583-589`).
- Sensor reads `0x01 | id<<8` (`WmiController.cs:522-525`).
- No TCP client to the Acer services (port constants in `AcerWmi.cs:43-44` are dead).
- One lock around all `root\WMI` calls.
- Keyboard RGB only over the ENEK5130 HID path, never the shared EC channel.
- No driver install; the old WinRing0 `R0PreySense` service is removed (`WinRing0Cleanup.cs`).
- Power overlays use correct GUIDs via `PowerSetActiveOverlayScheme`; the active plan is never switched (`AcerWmi.cs:89-91`).
- Update check only notifies and opens the browser; nothing is downloaded or run.
- Elevated autostart via a logon scheduled task (no UAC prompt).
- Releases are built by GitHub Actions from the tag, not uploaded by hand.
- OpenCore installer picks the `%SystemDrive%` disk, never touches the Windows ESP, never runs `bcdedit`/`mountvol`, deletes only a fully matched `PREYSENSE` partition; an uninstall script exists; `config.plist` is minimal (no SMBIOS spoof, no kexts, `WriteFlash` false).
- CPU power-limit units (mW/125) and Tau encoding are correct; GPU trip point clamped 70 to 89 C.
- No `SetBiosOptions` path is reachable from the UI.

## Got wrong (by severity)

### Safety
1. **No fan fail-safe.** Exit, crash, `SessionEnding`, and `Dispose` never write Auto (`0x410009`). Custom duty can be 0% (`EcFanPolicy.cs:12`). A crash in Custom can leave fans low or off. (`App.xaml.cs:51-68,137`, `WmiController.cs:963`)
2. **Fan cache desync.** Auto/Max bypass `_lastSetFanMode` (`ProductionHardwareService.cs:113-116`); Custom, then Auto, then Custom with the same percents writes nothing (`WmiController.cs:649-654`). UI says Custom, fans are in Auto/Max.
3. **Fan write race.** Slider writes are fire-and-forget `Task.Run` (`MainViewModel.Fans.cs:90`); `_lastSet*` has no lock.
4. **Fan writes unchecked.** Status not checked, no `GetGamingFanSpeed` read-back; state stored as if it worked (`WmiController.cs:612,634,674-677`).
5. **ACPI Override toggle does not gate mode-switch writes.** PL1/PL2/Tau, GPU watts, and trip point are written on every mode switch regardless (`MainViewModel.PowerModes.cs:196-199`, `Session.cs:361-364`).
6. **No upper limit on watts.** CPU PL1/PL2 up to 999 W (`CpuPowerLimitConfiguration.cs:116`), GPU UI max 999; `GpuPowerLimitConfiguration.Validate` has no upper bound.
7. **GPU byte overflow.** The SSDT writes watt deltas into 8-bit EC fields; above ~340 W the value wraps and writes garbage to EC shared memory at `0xFE800400` (`SSDT-PREYDEV.dsl:239-259`).
8. **dGPU restart** can black the screen (disables the NVIDIA display device, `DeviceHelper.cs:170-176`) and kills every process on the dGPU without asking (`NvidiaGpuControl.cs:202-231`). A GPU-mode change uses `shutdown /r /t 0 /f`.

### Correctness
9. **Mode 2 shown as Balanced.** `IsKnownOperatingMode` rejects 2 and `GetPowerProfile` returns 1 (`WmiController.cs:186-189,553-559`); calibration then "restores" Balanced and overwrites the EC state.
10. **Supported-modes mask `0x0A` is never read;** `SetPowerMode` writes any value and ignores its own read-back (`WmiController.cs:535,546-550`).
11. **No AC/DC split** for power limits; calibration mixes AC and DC readings.
12. **Fake settings:** `ceilingWatts` ignored, always 999 W (`OpenCorePowerLimitManager.cs:360`); `UpdateConfigPlistPatches` ignores its arguments; `TryReadIntelPlatformLimit` always returns 85 (`:160`). The 999 W MCHBAR value may do nothing if MSR 0x610 is lower; nothing checks.
13. **No handling of the Ultimate Performance plan** (unlike PredatorSense).
14. `SetCpuBoost` writes `perfboostmode` into the current plan on every switch; uninstall never undoes it.

### Services and app
15. **Debloater is destructive** (`AcerServiceDebloater.cs`): it matches anything named or displayed "Acer"/"Predator" (camera, voice, Pixy, Care Center), writes `Services\<name>\Start=4` directly, kills any Acer-signed process tree, **permanently deletes** scheduled tasks matching Acer/Predator/CareCenter/QuickAccess/DeviceInfo, and re-enable forces every service to Auto (original start types are never saved). The UI trusts an HKCU flag, not the real state.
16. Crash logs go next to the exe (often not writable) and overwrite each time; exceptions are not handled.
17. Tests write real HKCU state (`AcpiAndHardwareTests.cs:1246-1275` leaves "debloated" = true).
18. The update checker hard-codes the upstream repo and version `2.3.5`, separate from the csproj.
19. Leaks: `InvalidateCache` nulls `ManagementObject`s without dispose; enumerated WMI objects are not disposed.
20. Docs do not match code (fan read method, DDS `0x332` struct, backlight timeout path); README says the OpenCore installer reuses the partition, but it deletes and recreates it.

### Tests and CI
21. Most tests assert that strings exist in source files (74 `ReadAllText` calls in 53 tests); one is `Assert.IsTrue(x || !x)`. 7 tests fail on upstream as-is (missing DSL/iASL, OpenCore not loaded, stale XAML text).
22. The CI release does not run `dotnet test`; the exe is unsigned.

## OpenCore: do not run yet

What it does: shrinks C: by 16 MB, creates a hidden FAT `PREYSENSE` partition with OpenCore 1.0.8, which loads at boot and patches Acer ACPI tables so PreySense can set CPU/GPU watt limits.

Why not yet:
- **BitLocker:** enrolling the EFI files as trusted changes PCR7, so BitLocker asks for the recovery key, possibly on every switch between OpenCore boot and direct boot.
- `OpenRuntime` with Secure Boot ON has caused a 0x7E BSOD before (`docs/opencore_acpi_patching.md:23`); recovery depends on F2/F12.
- The install has no confirmation and no rollback; a reinstall makes a new partition GUID and breaks the BIOS boot entry.
- The patches remove the combined CPU+GPU budget (999 W PCCC, E3DT, ATPP): adapter overload, battery drain on AC, sudden shutdowns. Brick risk is low (TjMax/PROCHOT and vBIOS caps remain).
- USB install deletes `X:\EFI` on whatever drive is picked.
- The unused `GenerateOpenCoreGpuOverride` turns off Intel DTT (`DPTF=0`, `SADE=0`); delete it.
- `docs/bios/setbiossetting.ps1` can write the wrong byte or reset all BIOS settings; `bioseditor.ps1` is broken (missing map file).

Read-only checks before any decision:
1. `manage-bde -status C:` and `manage-bde -protectors -get C:`; msinfo32 "PCR7 Configuration".
2. F12 boot menu state: `GetBiosOptions`, then `Data[0x12D]` and `Data[0x50]`.
3. Do the ACPI patch byte patterns and table lengths (20131, 26025) match BIOS V1.28 (`HKLM:\HARDWARE\ACPI\SSDT\ACRSYS\ACRPRDCT\*`)?
4. HWiNFO64: PL1/PL2 MSR vs MMIO.
5. SHA256 of the bundled EFI files vs the official OpenCore 1.0.8 release (https://github.com/acidanthera/OpenCorePkg/releases).

## Plan for our fork

1. Fan fail-safe: restore Auto on exit, crash, and session end; floor Custom duty; read back every fan write; fix the cache and the race.
2. Respect mode 2 and the `0x0A` supported mask; show the real mode; never auto-"restore".
3. Replace the debloater with an allowlist of exactly `AASSvc` + `ASMSvc`; save and restore original start types via the SCM; no process kills, no task deletion.
4. Gate every ACPI/power write behind the override toggle; hard caps and byte-range checks.
5. Point the updater at the fork; derive the version from the assembly; CI runs tests.
6. Remove Chinese localization: done on branch `remove-chinese`.
7. OpenCore: decide after the read-only checks above.
