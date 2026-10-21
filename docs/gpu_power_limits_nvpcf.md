# GPU power limits

How PreySense sets NVIDIA TGP and the Dynamic Boost thermal trip on this chassis. Live control is ACPI through `SSDT-PREYDEV`, not NVAPI clock offsets.

---

## Two layers

```text
Active TGP ≈ VBIOS base + ACPI TGP offset (MxCT) + Dynamic Boost (MxDB)
```

- **VBIOS** sets the hardware floor (typically 85 W) and a hard max (often 150 W on this SKU). NVML reports those for slider bounds. PreySense does not flash VBIOS.
- **ACPI NVPCF** (`nvpcf.sys` via `Device (NPCF)` in SSDO) adds milliwatt offsets. NVIDIA’s driver will not exceed what ACPI currently advertises, even if VBIOS max is higher.

Factory ACPI Turbo on this chassis is **130 W**, not the VBIOS 150 W max.

---

## What the UI is changing

| Control | ACPI objects |
| :--- | :--- |
| GPU Power Limit | `\_SB.NPCF.ACBT`, `ATPP`, ERM4 `MxCT` / `MxDB` / `MxCP` for the active profile |
| GPU Thermal Trip (WMG3) | `\_SB.NPCF.WMG3` |

Defaults PreySense stores per mode:

| Mode | GPU watts | WMG3 |
| :--- | ---: | ---: |
| Quiet | 85 | 79 °C |
| Balanced | 100 | 79 °C |
| Performance | 115 | 79 °C |
| Turbo | 130 | 79 °C |

After a write, PREYDEV issues `Notify(\_SB.NPCF, 0xC0)` so `nvpcf.sys` reloads.

NVML is **read-only** here (current / min / max watts). The app does not expose GPU core or memory clock offsets.

---

## ERM4 profile table (`0xFE800400`)

Eight-byte entries per thermal mode. CPU bytes occupy `+0x00`…`+0x03`; GPU bytes follow:

| Offset | Field | Meaning |
| :---: | :--- | :--- |
| `+0x04` | `MxDB` | Dynamic Boost headroom (W) |
| `+0x05` | `MxCT` | TGP offset added to the VBIOS floor (W) |
| `+0x06` | `MxCP` | Platform power cap offset (W) |

Factory GPU ACPI (floor 85 W): Quiet +0, Balanced +15, Performance +30, Turbo +45 → 85 / 100 / 115 / 130 W.

Dynamic Boost is withdrawn as GPU/CPU temperature crosses OEM trip points. **WMG3** is the GPU step that commonly drops boost; factory is **79 °C** (`0x4F`). Raising WMG3 in Mode or Games delays that step-down. Range in the UI is 70–89 °C.

---

## PREYDEV methods

| WmiMethodId | Name |
| :---: | :--- |
| 5 | SetGpuPowerLimit (target watts) |
| 6 | GetGpuPowerLimit |
| 7 | SetGpuThermalTripPoint (°C) |
| 8 | GetGpuThermalTripPoint |

Implementation: `acpi_staging/SSDT-PREYDEV.dsl`. The SSDT does not lock a 140 W continuous TGP at boot. The wattage you set in Mode or Games is what is written at runtime.

---

## Related OEM objects (not written by the UI except via PREYDEV)

| Object | Role |
| :--- | :--- |
| `ACBT` | Base TGP allowance (125 mW units) |
| `AMAT` | Max Dynamic Boost offset |
| `ATPP` | Target platform power |
| `DBAC` | Dynamic Boost on AC |
| `NVWM` | WhisperMode acoustic clamp |

Acer System Monitor (`ASMSvc`) can still register a Dynamic ROS / BSOC table with NVPCF on weak adapters. PreySense’s debloater can stop that service; it is not required for TGP writes.
