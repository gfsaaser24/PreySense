# BIOS Setup NVRAM offsets

Verified InsydeH2O **Setup** variable offsets on PHN16 / PHN16S-class Predator firmware. PreySense does not write these at runtime except indirectly (GPU mux SMI for dGPU-only). Use `setup_var` / modGRUBShell only if you know the firmware build matches.

---

## Offsets used or shown by PreySense

| Offset | Setting | Notes |
| :---: | :--- | :--- |
| `0x0050` | GPU display mode | `1` Auto, `2` Optimus, `3` dGPU. Written by firmware on **dGPU Only**. |
| `0x00F9` in VarStore `0x0003` | Intel UVP / overclocking lock | Known command: `setup_var 0x00F9 0x00 -s 0x0003`. PreySense does not apply it. |

---

## Other verified chassis offsets

| Offset | Setting | Values |
| :---: | :--- | :--- |
| `0x000E` | Intel VT-x | `0` off, `1` on |
| `0x000F` | Intel VT-d | `0` off, `1` on |
| `0x00A1` | Network boot | `0` / `1` |
| `0x00A2` | Wake on LAN | `0` / `1` |
| `0x00AA` | Wake on USB (lid closed) | `0` / `1` |
| `0x012D` | F12 boot menu | `0` / `1` |
| `0x012E` | Function key default | `0` media, `1` function |
| `0x0131` | Keyboard backlight timeout | `0` off, `1` 30 s |
| `0x0133` | Fast boot | `0` / `1` |
| `0x0148` | POST animation | `0` / `1` |
| `0x0149` | POST chime | `0` mute, `1` unmute |
| `0x078E` | Secure Boot | `0` / `1` |

CFG Lock and Overclocking Lock **vary by BIOS version**. Do not assume a single dword from older notes. Unlocking undervolt is outside PreySense’s supported control path.
