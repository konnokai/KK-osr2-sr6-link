# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Links Koikatu/CharaStudio sex scenes to OSR2/SR6 (and other linear) hardware. Two **independent** programs that talk over a TCP socket:

1. **C# BepInEx plugin** (`plugin/Osr2_sr6_link.cs`) — runs *inside* CharaStudio. Samples character bone positions during animations and streams 6-axis motion to the desktop app.
2. **Qt C++ desktop app** (`qt/mainwindow.cpp` + widgets) — receives the motion, lets the user edit it, and drives hardware over serial / Intiface (Buttplug) / funscript export.

There is no shared code between the plugin and the app; the socket is the only contract. They have separate project files (`.csproj`/`.sln` for C#, `.pro` for Qt) built with different toolchains.

A third component, the **WPF/.NET 8 desktop app** (`wpf/`), is a port of the Qt app being brought to parity with it (it speaks the same socket protocol and reads the same `config.ini`). See "WPF app" below.

## Repository layout

One folder per component (file paths below are written relative to these):

- `qt/` — Qt C++ desktop app (`.pro`, `main.cpp`, `mainwindow.*`, the custom widgets, `mainwindow.qrc` + `icons/`).
- `plugin/` — C# BepInEx plugin (`Osr2_sr6_link.cs`, `kk_osr2_sr6_link.csproj`/`.sln`, `Properties/`).
- `wpf/` — WPF/.NET 8 app (`KKOsr2Sr6Link.Wpf/` app + `KKOsr2Sr6Link.Tests/`).

## Build

**Qt app** (qmake project `qt/Link_osr2_sr6_to_kk_studio.pro`):
- Open in Qt Creator, or: `qmake && make` (Qt 6, `core gui network serialport websockets widgets`, C++17).
- The `.pro` is the source of truth for which `.cpp`/`.ui`/`.qrc` files compile — `.sln`/`.csproj` are *not* for the Qt code.

**C# plugin** (`plugin/kk_osr2_sr6_link.csproj`, .NET Framework **3.5**, output type Library):
- Build with MSBuild / Visual Studio. Debug config writes the DLL straight into a local game install at `..\..\..\galgame\kk\Koikatu\BepInEx\plugins\` — that path is hardcoded and will only work on the original author's machine; adjust `<OutputPath>` if building elsewhere.
- References (`Assembly-CSharp`, `BepInEx`, `KKAPI`, `Timeline`, `UnityEngine`, `0Harmony`) all resolve via `HintPath` into that same game install. No NuGet.

**WPF app** (`wpf/KKOsr2Sr6Link.Wpf/KKOsr2Sr6Link.Wpf.csproj`, .NET 8, `dotnet build` / `dotnet run`):
- xUnit tests in `wpf/KKOsr2Sr6Link.Tests/` — `dotnet test`. The plugin and Qt app have no tests; the WPF app does.

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

## WPF app

Port of the Qt app (`wpf/KKOsr2Sr6Link.Wpf/`). `MainWindow.xaml(.cs)` is the shell; `Engine/` holds the non-UI logic (`LinkServer`, `SerialOutput`, `ButtplugClient`, `PlaybackEngine`, `AppConfig` over `IniConfig`, scene parsing), `Controls/` the custom widgets (`RangeSlider`, `OverviewEdit`, `ScripterEdit`). Method comments cite the `mainwindow.cpp` lines they mirror — keep that cross-reference when porting more.

**Localization** (`Localization/`) — runtime UI language switch, no restart:
- Strings live in swappable `ResourceDictionary` files `Strings.<lang>.xaml` (`en`, `zh-Hant`). XAML binds them with `{DynamicResource L.*}` (static UI) / `{DynamicResource St.*}` (status messages); swapping the merged dictionary updates everything live.
- `Loc.SetLanguage(lang)` swaps the dictionary; `Loc.T(key)` / `Loc.T(key, args)` resolve a string (or format one) from code. `MainWindow` aliases these as `L(...)`.
- Persisted in `config.ini` `[App]/language`; applied in the `MainWindow` ctor **before** `InitializeComponent` so `DynamicResource` resolves. The selector lives on PageSettings (`Language_Changed`).
- Adding a string: add the key to **both** dictionaries (a missing key falls back to the key text). Do **not** localize strings used as logic keys — lovemaking-mode values (`normal`/`blowjob`/…) and axis names (`L0`–`R2`) are matched/saved by their literal text.

## Conventions / gotchas

- Filenames keep their original spelling: `range_silder` ("silder", not "slider"). Don't "fix" it — it's referenced everywhere.
- `MainWindow` is frameless with custom drag handling (`mousePressEvent`/`mouseMoveEvent` + `m_drag`), so window-chrome behavior is manual.
- The plugin pins very old API versions (KKAPI 1.38, Timeline 1.1, .NET 3.5) because Koikatu/BepInEx require them — do not upgrade target framework or references casually.
- `Collect_data` samples by **programmatically seeking** the Timeline (`Timeline.Seek` one interval per `Update` frame), not by live user playback. The six axes are geometry between bone world positions, which only exist after the pose is evaluated — you can't derive them from Timeline keyframes alone.
- Per-scan, bone `Transform`s are looked up **once** in `bone_cache` (keyed by the pair's `charas_name`, filled by `BuildBoneSet` in the `resampled` block). Don't reintroduce per-sample `GameObject.Find` in the sampling loop — it scans the whole scene 20×/sample/pair.
- The `ReceiveClient` socket listener runs on a **background** thread, sleeps when disconnected, and treats `Receive` returning 0 as a close. Keep it from busy-waiting (no tight loop without `Thread.Sleep`).
- `scanning_mode` only ever takes the value 0; the "bisexual" buttons are unfinished and behave identically to "normal" (no `scanning_mode == 1` branch exists).
