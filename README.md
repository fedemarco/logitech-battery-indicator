# Logitech Battery Indicator

Windows tray app showing the battery level of a Logitech PRO X2 SUPERSTRIKE mouse (C# / .NET 10, WinForms, HID++).

**Status:** early development. A minimal tray app exists; battery reading is not implemented yet. See [docs/PLAN.md](docs/PLAN.md).

Third-party notices (battery icons, HidSharp): [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Build locally

Development is on macOS; the app builds there but only runs on Windows.

```sh
dotnet build
dotnet publish src/LogitechBattery -c Release   # self-contained single-file win-x64 exe
```

## CI

`.github/workflows/build.yml` runs on GitHub (ubuntu):

- Every push to `main`: builds a dev exe (`0.1.0-dev.<run>+<sha>`), uploaded as artifact `LogitechBattery-win-x64` (kept 1 day).
- Pushing a `vX.Y.Z` tag: builds that version and creates a GitHub Release with `LogitechBattery.exe`. Releases are created only this way.

## Install / update on the Windows PC

One-time setup:

```powershell
winget install GitHub.cli
gh auth login
gh api repos/fedemarco/logitech-battery-indicator/contents/scripts/update.ps1 -H "Accept: application/vnd.github.raw" > update.ps1
```

Then, to install or update (stops the running app, installs to `%LOCALAPPDATA%\LogitechBattery`, starts it):

```powershell
powershell -ExecutionPolicy Bypass -File update.ps1            # latest dev build
powershell -ExecutionPolicy Bypass -File update.ps1 -Release   # latest release
```

## Probe (HID++ discovery)

`--probe` sends read-only HID++ queries to attached Logitech devices and writes `%LOCALAPPDATA%\LogitechBattery\probe.txt` (opened in Notepad). It runs even while the tray app is running. Run it once with the mouse on the Lightspeed receiver and once with the USB cable, then send back `probe.txt` (rename the second one, e.g. `probe-cable.txt`, before running again):

```powershell
& "$env:LOCALAPPDATA\LogitechBattery\LogitechBattery.exe" --probe
```

(cmd: `"%LOCALAPPDATA%\LogitechBattery\LogitechBattery.exe" --probe`). Wake the mouse (move it) first.

## Cutting a release

Bump `VersionPrefix` in the csproj if needed, then:

```sh
git tag vX.Y.Z && git push origin vX.Y.Z
```

## Version plan

| Version | After milestone |
|---------|-----------------|
| v0.1.0  | 4 (tray indicator) |
| v0.2.0  | 5-6 (HID++ reading, refresh, charging) |
| v0.3.0  | 7-8 (notifications, startup) |
| v1.0.0  | 9 (polish, packaging) |
