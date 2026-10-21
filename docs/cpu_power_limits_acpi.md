# CPU power limits

How PreySense sets package PL1, PL2, and Tau on the Helios Neo 16S. Runtime writes go through OpenCore-injected `SSDT-PREYDEV`, not MSR drivers.

---

## What the UI is changing

Mode → Advanced, with OpenCore loaded and ACPI Override on:

| Control | Hardware target |
| :--- | :--- |
| Sustained long power (PL1) | RAPL package limit 1 |
| Short burst power (PL2) | RAPL package limit 2 |
| Burst window (Tau) | RAPL PL1 time window |

Factory values on this chassis (ERM4 AC table, also used by **Reset**):

| Mode | PL1 | PL2 | Tau |
| :--- | ---: | ---: | ---: |
| Quiet | 45 W | 140 W | 56 s |
| Balanced | 65 W | 140 W | 56 s |
| Performance | 75 W | 140 W | 56 s |
| Turbo | 85 W | 140 W | 56 s |

A mode switch writes that mode’s stored limits. Game profiles write their own PL1/PL2/Tau after switching mode. Without OpenCore, Acer’s profile still changes; these RAPL writes do not stick.

---

## RAPL MMIO (`CPWR`)

Acer maps MCHBAR + `0x5000` into ACPI SystemMemory:

```asl
OperationRegion (CPWR, SystemMemory, (\_SB.PC00.GMHB () + 0x5000), 0x1000)
Field (CPWR, ByteAcc, NoLock, Preserve)
{
    Offset (0x938), PWRU, 4,     // Power units
    Offset (0x9A0),
    PPL1, 15, PL1E, 1, CLP1, 1, PL1T, 7,
    Offset (0x9A4),
    PPL2, 15, PL2E, 1, CLP2, 1, PL2T, 7
}
```

Power fields are in 1/8 W (0.125 W). Watts × 8 = register value (`55 W` → `440`).

`SSDT-PREYDEV` method 3 writes `PPL1` / `PPL2` / enable bits, optionally `PL1T`, then calls OEM `HIDA()` so the EC/firmware observes the new package limits.

---

## The 85 W PCCC / IPF clamp

`PCCC()` in the OEM SSDT advertises a capability ceiling. On the 47 W SKU this chassis uses, that dword is **85,000 mW**. Intel IPF (`ipf_cpu.sys`, `ipfsvc`) treats it as the maximum sustained PL1 and pulls Dynamic PL1 back to 85 W even if RAPL was set higher.

OpenCore patches that dword to the **PL1 Platform Power Limit** from Settings (default 85, user-settable). That is an advertised ceiling, not the live PL1. Live PL1 is still method 3.

Disabling Intel DTT/IPF services is optional and is **not** required for PreySense. The PCCC patch plus PREYDEV writes are the supported path.

---

## Tau encoding

The UI value is seconds (or ∞). It is **not** written as a raw second count. PreySense encodes the nearest Intel RAPL 7-bit window:

```
seconds ≈ 2^Y × (1 + F/4) × (1/1024)
```

`Y` is 5 bits, `F` is 2 bits. Factory 56 s is `0x6F`. Slider value **449** encodes `0x7F` (treated as infinite). `_Q30` in OEM DSDT can still rewrite `PL1T` on a profile switch; PREYDEV writes Tau again after the mode change so the UI value wins while OpenCore is active.

---

## Runtime WMI (`PreySenseControl`)

Class `root\WMI\PreySenseControl`, GUID `{D73644F9-51A6-43DE-87BC-9D1B0BE8C931}`, method `WM01`:

| WmiMethodId | Name | Payload |
| :---: | :--- | :--- |
| 1 | GetVersion | Returns `0x0100` |
| 2 | Ping | Returns `0x50524559` (`PREY`) |
| 3 | SetCpuPowerLimits | PL1 mW, PL2 mW, optional Tau |
| 4 | GetCpuPowerLimits | Current PL1/PL2/Tau |

C# client: `PreySense.Core/Hardware/PreySenseWmiDeviceClient.cs`. Detection: ping plus OpenCore NVRAM / ACPI table scan (`OpenCoreSecurityValidator`).

---

## What PreySense does not use

- PawnIO, WinRing0, or MSR `0x610` writes for CPU power
- Replacing `_Q30` / `WMBH` (those OEM methods stay in place)
- A boot-time `_INI` that programs PL1
