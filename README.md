# monitor-adjust

**A native Windows GUI to adjust monitor brightness, contrast and input source over DDC/CI.**

**English** | [中文](README.zh-CN.md) | [日本語](README.ja.md)

A small Windows desktop tool that controls a monitor's **brightness, contrast and input source**
directly over the DDC/CI protocol. It talks to the monitor hardware itself, which is not the same
thing as the Windows brightness slider — so external monitors, and the contrast and input-source
controls the OS slider cannot reach, are all adjustable.

Native single-file C# WinForms program, with no script host involved (no bat / ps1 / PowerShell).

> This repository holds the **source code**. The compiled `MonitorAdjust.exe` is published under
> [**Releases**](https://github.com/catlike-soda/monitor-adjust/releases) - see [Download](#download).
> Running it also requires `winddcutil.exe` (see [Requirements](#requirements)).

## Download

Get the latest build from [**Releases**](https://github.com/catlike-soda/monitor-adjust/releases):

1. Download `MonitorAdjust.exe` from Releases
2. Download `winddcutil.exe` from [ubihazard/winddcutil](https://github.com/ubihazard/winddcutil)
3. Put both files in the same folder and run `MonitorAdjust.exe`

## Features

- UI **switches between Chinese and English at runtime** (see below)
- Auto-detects how many monitors are connected — **plug in N, it shows N**. The window lays itself
  out to fit the screen and splits into columns / scrolls when there are many monitors
- Per-monitor adjustment of brightness and contrast
- Switch the input source (HDMI / DP / DVI / VGA / Component, …) with a confirmation prompt
- **"Apply" only writes brightness and contrast** — it never switches the input source by accident
- "Restore" returns to the values from the moment the window was opened
- "Refresh" re-reads everything and makes the current values the new "was" values
- Brightness range 0-100 or 0-255: detected automatically, with a manual override

## Language

The UI switches between **Chinese and English at runtime**: pick it from the "Language" box at the
bottom of the window. The change is instant and your choice is remembered.

- **On first run** it follows the system language: Chinese systems get Chinese, everything else English
- The choice is stored in `monitor-adjust.ini` next to the exe. If that folder is not writable
  (e.g. installed under Program Files) it falls back to `%LOCALAPPDATA%\monitor-adjust\settings.ini`
- You can also force the language from the command line:

```powershell
.\MonitorAdjust.exe --lang en
```

## Requirements

You need [**winddcutil**](https://github.com/ubihazard/winddcutil) (a Windows port of ddcutil,
shipped as a PyInstaller single-file program), placed **in the same folder as `MonitorAdjust.exe`**.

The program looks for it in this order: same folder as the exe → a `winddcutil\` subfolder next to
the exe → the current directory → `PATH`.

System requirements: Windows 7 SP1 or later, with the bundled .NET Framework 4.x. No administrator
rights needed.

## Usage

1. Put `MonitorAdjust.exe` and `winddcutil.exe` in the same folder
2. Double-click `MonitorAdjust.exe`
3. Drag the sliders and click "Apply"

Notes:

- **Be careful when switching the input source**: switching to a port with nothing connected makes
  the display go black or show "no signal", and the software may not be able to switch it back —
  you would have to use the monitor's own physical buttons
- The monitor must have DDC/CI enabled in its own OSD menu (most monitors ship with it on)
- Built-in laptop panels usually do not support DDC/CI and will not appear in the list; that is normal
- The program writes a `gui-log.txt` run log next to itself; feel free to delete it

## Build from source

Use the .NET Framework compiler that ships with Windows — **Visual Studio is not required**:

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:winexe /codepage:65001 `
  /out:"MonitorAdjust.exe" `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  "MonitorAdjust.cs"
```

- `/target:winexe` — no console window
- `/codepage:65001` — the source is UTF-8 and **this flag is required**, otherwise the Chinese
  string literals come out as mojibake
- Keep the output file name `MonitorAdjust.exe`: the assembly name is taken from it

## Hidden flags (debugging)

```powershell
# Self-check: write the values it read into a text file, without showing a window
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--dump','dump.txt' -Wait

# Off-screen render: draw the window off-screen and save a PNG, to check the layout
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--render','preview.png' -Wait

# Fake data mode: never calls winddcutil, draws the UI from made-up data (1-16 monitors)
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--fake','6','--render','n6.png' -Wait

# Pretend the screen is small, to check the layout with many monitors
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--fake','8','--screen','1024x600','--render','n8.png' -Wait

# Force the UI language (zh / en), e.g. to capture both versions
Start-Process ".\MonitorAdjust.exe" -ArgumentList '--lang','en','--render','en.png' -Wait
```

`--fake` also blocks every write, so it **can never change a real monitor**.

## Known limitations

- **The range is guessed**: `winddcutil getvcp` returns the current value but not the maximum, and
  offers no command-line options at all. The program therefore assumes "value > 100 means 0-255";
  if it guesses wrong, set the range manually
- The input-source list is occasionally unreadable (DDC/CI itself is flaky), in which case it falls
  back to a list of common inputs
- Sliders always start at 0 (the minimum brightness/contrast on virtually every monitor is 0)

## License

The code may be used, modified and distributed freely.
`winddcutil` is an independent project by its own authors, under its own license; this repository
does not include it.
