# Security policy

## Supported versions

Only the [latest release](https://github.com/fedemarco/logitech-battery-indicator/releases/latest) gets security fixes.

## Report a problem

Use **Security → Report a vulnerability** on the [repository page](https://github.com/fedemarco/logitech-battery-indicator/security/advisories/new). This opens a private report. Please do not open a public issue for a security problem.

This is a hobby project. I answer on a best-effort basis, usually within 14 days. A fix ships as a patch release. The release notes get a **Security** section, and the advisory is published with credit to the reporter, if you want it.

## What counts

- A crafted USB or HID device, or a HID++ report, that crashes the app or does more than that.
- A file written outside `%LOCALAPPDATA%\LogitechBattery`.
- A damaged `history.csv` that does harm.

## Facts that help a report

- The app needs no administrator rights.
- It reads only the Logitech vendor HID++ collections, not keyboard or mouse input.
- It has no network code. It never connects to the internet.
