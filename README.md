# Link OSR2/SR6 to KK Studio

Links Koikatu / CharaStudio sex scenes to OSR2/SR6 and other linear hardware. Supports the Handy and other linear devices over [Intiface Central](https://intiface.com/central/) (Buttplug), direct serial (TCode), and `.funscript` export.

Introduction: https://discuss.eroscripts.com/t/for-the-koikatu-charastudio-provides-link-osr2-sr6-plug-ins-as-well-as-script-playback-procedures/190793

## Components

- **BepInEx plugin** — runs inside CharaStudio, samples character motion during animations and streams 6-axis data over a TCP socket.
- **Qt desktop app** — receives the motion, lets you edit the per-axis curves, and drives the hardware. (`Link_osr2_sr6_to_kk_studio.pro`, Qt 6.)
- **WPF desktop app** (`wpf/`) — a .NET 8 port of the desktop app. Same socket protocol and `config.ini`.

The plugin (TCP client) connects to the desktop app (TCP server) on `127.0.0.1:8000` by default.

## Language

The WPF app has a runtime language switch under **Settings → language** (no restart). Currently bundled: English and 正體中文 (Traditional Chinese). To add a language, copy `wpf/KKOsr2Sr6Link.Wpf/Localization/Strings.en.xaml` to `Strings.<code>.xaml`, translate the values, and add the code to `Loc.Languages` plus an entry in the Settings selector.

## Build

- **WPF app:** `dotnet build wpf/KKOsr2Sr6Link.Wpf` (tests: `dotnet test wpf/KKOsr2Sr6Link.Tests`).
- **Qt app:** open `Link_osr2_sr6_to_kk_studio.pro` in Qt Creator, or `qmake && make`.
- **Plugin:** build `kk_osr2_sr6_link.csproj` with MSBuild (.NET Framework 3.5; references resolve into a local Koikatu/BepInEx install — adjust the paths for your machine).
