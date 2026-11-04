# Logitech Battery Indicator

Windows tray app showing the battery level of a Logitech PRO X2 SUPERSTRIKE mouse (C# / .NET 10, WinForms, HID++).

The tray icon shows the mouse battery, read over HID++ via the Lightspeed receiver or the USB cable (read every 60 s, and more often for a few minutes after the cable or receiver is plugged in or unplugged). Diagnostics go to `%LOCALAPPDATA%\LogitechBattery\log.txt`.

The app shows a Windows notification when the battery drops to 15 % ("low") and again at 5 % ("critical"), once per discharge, and when it finishes charging ("fully charged").

"Start with Windows" in the tray menu is on by default and can be unticked at any time. It is stored in the per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key, no admin rights needed.

`LogitechBattery.exe --mock` starts with a simulated battery and a Simulator menu instead of reading the mouse.

Third-party notices (battery icons, HidSharp): [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Build locally

Development is on macOS; the app builds there but only runs on Windows.

```sh
dotnet build
dotnet publish src/LogitechBattery.App -c Release   # self-contained single-file win-x64 exe
```

## CI

`.github/workflows/build.yml` runs on GitHub (ubuntu):

- Every push to `main`: builds a dev exe (`<version>-dev.<run>+<sha>`), uploaded as artifact `LogitechBattery-win-x64` (kept 1 day).
- Pushing a `vX.Y.Z` tag: builds that version and creates a GitHub Release with `LogitechBattery.exe` and `THIRD-PARTY-NOTICES.md`. Releases are created only this way.

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

## Listen (HID++ notifications)

`--listen` keeps the Logitech HID++ collections open and logs every report they deliver, with timestamps, until you click OK; it reopens them after USB changes. The only request it sends is a read of the receiver's notification flags. The report goes to `%LOCALAPPDATA%\LogitechBattery\listen.txt` (opened in Notepad). Leave the tray app running: its own requests and their answers show up in the log too.

```powershell
& "$env:LOCALAPPDATA\LogitechBattery\LogitechBattery.exe" --listen
```

## Cutting a release

Bump `VersionPrefix` in `Directory.Build.props` if needed, then:

```sh
git tag vX.Y.Z && git push origin vX.Y.Z
```
