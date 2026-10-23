# DTS Custom Audio

PreySense changes DTS presets and Custom Audio settings through `dtscnt64.dll`. It does not process audio itself. `dtsapo64.dll` and `DtsApo4Service` stay as installed.

Preset names in the settings menu are display labels. The COM mode token is unchanged: `Game1`, `Game2`, `Game3`, `Custom Audio`, `Music`, `Movie`, `Voice`, `Off`. Auto is not one of those tokens.

## Auto

Auto is `IDTSOperatingMode.AutoContentModeEnable`. Turning it on leaves the current mode name in place. DTS then follows the Windows audio stream and uses Music, Movie, or Voice. Game, Custom, and Off stay manual. Choosing any other preset writes the flag back to `0`. If the device is Off, Auto selects Music first, then sets the flag. Auto does not add a second processor, so it has no CPU or latency of its own beyond the preset DTS picks. That cost has not been measured on this machine.

## Custom Audio controls

These properties were read from the public control enumerator on the endpoint's `Custom Audio` operating mode. `GetControlFromName` returns the control. `Value` is the integer PreySense reads and writes. Ranges below were read on this machine.

| Custom Audio control | COM name | Values |
| --- | --- | --- |
| Front | `Eagle-Stereo Mode` | `1` (`Eagle-Front`) |
| Wide | `Eagle-Stereo Mode` | `2` (`Eagle-Wide`) |
| Traditional | `Eagle-Stereo Mode` | `3` (`Eagle-Off`) |
| Treble Enhance | `MFX:Eagle-Treble Enable` | `0` off, `1` on |
| Treble level | `MFX:Eagle-Treble Level` | `0`–`100`, step `1` |
| Bass Boost | `MFX:Eagle-TBHDX Enable` | `0` off, `1` on |
| Bass level | `MFX:Eagle-TBHDX Bass Level` | `0`–`100`, step `1` |
| Dialog Clarity | `SFX:Eagle-DE Enable` | `0` off, `1` on |
| Dialog level | `SFX:Eagle-DE Level` | `0`–`100`, step `1` |
| Volume Smoothing | `SFX:Eagle-LC Enable` | `0` off, `1` on |
| Equalizer | `MFX:Eagle-GEQ Enable` | `0` off, `1` on |
| Equalizer bands | `MFX:Eagle-GEQ Band0 Gain` … `Band9 Gain` | COM `-150`…`150`, one step is `0.1` dB. The DTS app slider is `-10`…`+10` dB |

The equalizer bands, in order, are 32 Hz, 64 Hz, 125 Hz, 250 Hz, 500 Hz, 1 kHz, 2 kHz, 4 kHz, 8 kHz, and 16 kHz. Those labels and the ±10 dB slider ends are the DTS:X Ultra app strings. The control's own range is ±15.0 dB. Equalizer edits stay on that device's Custom Audio mode and are written by Apply or Save.

`Eagle-Stereo Mode` also reports `0` (`Eagle-Default`) and `4` (also `Eagle-Off`). The DTS app does not offer those as Custom Audio buttons, so PreySense does not show them.

Reading a mode's controls does not make that mode active. The Custom Audio entry is shown only for a device whose preset is Custom. Apply and Save write that device's settings. Factory Reset is one footer button. It calls `RestoreDefaults` on every operating mode for each DTS device, then selects Music on both.

There is one Factory Reset button in the sound settings footer. It uses `IDTSOperatingMode.RestoreDefaults` for every mode on every DTS device, then selects Music on each.

A one-time check on this machine wrote each stereo value, each enable, and each level through these properties, read them back, then restored the previous Custom profile. Switching the speakers from Custom to Music and back left the Custom values in place. The headphone endpoint's preset and Custom values did not change. Opening a second control object still read the restored values. That check did not include a reboot or a listening test.
