# GPU display switching

Two independent mux paths. The main window uses NVIDIA Dynamic Display Switching. Acer firmware mux is only for BIOS dGPU-only (reboot). Acer WMI does not switch the live panel.

---

## Main-window modes

| UI | Code | Who drives the panel | Reboot |
| :--- | :---: | :--- | :---: |
| Optimus | 0 | Intel iGPU; dGPU may RTD3 | No |
| Auto | 1 | Driver switches on 3D load | No |
| Advanced Optimus | 2 | NVIDIA dGPU via DDS | No |
| dGPU Only | BIOS | Firmware mux; iGPU absent after POST | Yes |

## NVIDIA DDS (`nvcpl.dll`)

PreySense calls `NvCplApiInit` / `GetInfo` / `GetSetting` / `SetSetting` / `Execute` from the NVIDIA Control Panel library.

| Id | Use |
| :--- | :--- |
| `0x17` | DDS capability / status bitmask |
| `0x332` | Display mode: 0 Optimus, 1 Automatic, 2 dGPU |
| `0x333` | Apps blocking a switch (open swapchains) |
| `0x10000` | Commit staged settings |

Typical switch is a few seconds. Fullscreen exclusive apps can block it; PreySense can list NVML compute/graphics clients so those processes can be closed.

## Acer BIOS mux (dGPU Only)

Used only by **dGPU Only**:

```text
SetGamingMiscSetting( ((mode + 1) << 8) | 0x02 )
  → ACPI WMBH → SMI 0xD0 → NVRAM display-mode byte
  → shutdown /r /t 2
```

Firmware values 1 / 2 / 3 map back to UI 0 / 1 / 2 via `GetGamingMiscSetting(0x02)`. After reboot, PreySense treats BIOS dGPU-only as active when an Intel/AMD iGPU is missing and NVIDIA is present.

Insyde Setup offset for this byte on this chassis is `0x50` (see [BIOS offsets](bios_setup_var_offsets.md)).
