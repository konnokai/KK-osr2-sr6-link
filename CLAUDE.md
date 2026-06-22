# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Links Koikatu/CharaStudio sex scenes to OSR2/SR6 (and other linear) hardware. Two **independent** programs that talk over a TCP socket:

1. **C# BepInEx plugin** (`Osr2_sr6_link.cs`) — runs *inside* CharaStudio. Samples character bone positions during animations and streams 6-axis motion to the Qt app.
2. **Qt C++ desktop app** (`mainwindow.cpp` + widgets) — receives the motion, lets the user edit it, and drives hardware over serial / Intiface (Buttplug) / funscript export.

There is no shared code between them; the socket is the only contract. The two also have separate project files (`.csproj`/`.sln` for C#, `.pro` for Qt) — they are built with different toolchains.

## Build

**Qt app** (qmake project `Link_osr2_sr6_to_kk_studio.pro`):
- Open in Qt Creator, or: `qmake && make` (Qt 6, `core gui network serialport websockets widgets`, C++17).
- The `.pro` is the source of truth for which `.cpp`/`.ui`/`.qrc` files compile — `.sln`/`.csproj` are *not* for the Qt code.

**C# plugin** (`kk_osr2_sr6_link.csproj`, .NET Framework **3.5**, output type Library):
- Build with MSBuild / Visual Studio. Debug config writes the DLL straight into a local game install at `..\..\..\galgame\kk\Koikatu\BepInEx\plugins\` — that path is hardcoded and will only work on the original author's machine; adjust `<OutputPath>` if building elsewhere.
- References (`Assembly-CSharp`, `BepInEx`, `KKAPI`, `Timeline`, `UnityEngine`, `0Harmony`) all resolve via `HintPath` into that same game install. No NuGet.

No test suite exists for either component.

## Communication contract

- Plugin is the TCP **client**, Qt app is the TCP **server**. Default `127.0.0.1:8000` (configurable both sides; port clamped 0–9999).
- 6 axes follow the OSR2/SR6 / TCode convention used throughout the codebase:
  - `L0` insert (stroke), `L1` surge, `L2` sway
  - `R0` twist, `R1` roll, `R2` pitch
  - These names appear everywhere as parallel sets of variables/lists (`L0s`, `silderL0`, `scripter3L0`, `config_L0`, …). Changes to one axis almost always need to be mirrored across all six.
- Per "lovemaking mode" the plugin also tracks separate sample sets: default, `blowjob_*`, `breastsex_*`, `handjobL_*`, `handjobR_*` (see `Lovemaking_data` in `mainwindow.h`).

## Qt app structure

`mainwindow.cpp` is large (~100k) and holds almost all logic: TCP server, serial port, Intiface websocket client, config (`QSettings`), scene-part editing, and funscript I/O. Custom widgets it composes:

- `Range_Silder` (`range_silder.*`) — per-axis min/max range control.
- `Overview_edit` (`overview_edit.*`) — whole-scene timeline split into parts; emits part add/del/select.
- `Scripter_edit3` (`scripter_edit3.*`) — the per-axis curve editor actually used by `MainWindow` (one instance per axis). It is the current version.
- `Scripter_edit` (`scripter_edit.*`) — older editor still in the `.pro` but **not** included by `mainwindow.h`; treat as legacy unless you confirm a live use.

Hardware outputs:
- **Serial** (`QSerialPort`) — TCode to OSR2/SR6.
- **Intiface Central / Buttplug** (`QWebSocket`, default `ws://localhost:12345`) — Handy and other linear devices; device list managed via the `Device` struct.
- **Funscript export** — `convertsr6sToFunscript()` writes per-axis `.funscript` files.

## Conventions / gotchas

- Filenames keep their original spelling: `range_silder` ("silder", not "slider"). Don't "fix" it — it's referenced everywhere.
- `MainWindow` is frameless with custom drag handling (`mousePressEvent`/`mouseMoveEvent` + `m_drag`), so window-chrome behavior is manual.
- The plugin pins very old API versions (KKAPI 1.38, Timeline 1.1, .NET 3.5) because Koikatu/BepInEx require them — do not upgrade target framework or references casually.
