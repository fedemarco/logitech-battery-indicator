# Security policy

## Supported versions

Only the [latest release](https://github.com/fedemarco/logitech-battery-indicator/releases/latest) gets security fixes.

## Report a problem

Use **Security → Report a vulnerability** on the [repository page](https://github.com/fedemarco/logitech-battery-indicator/security/advisories/new). This opens a private report. Please do not open a public issue for a security problem.

This is a hobby project. I answer on a best-effort basis, usually within 14 days. A fix ships as a patch release. The release notes get a **Security** section, and the advisory is published with credit to the reporter, if you want it.

## What counts

- A crafted USB or HID device, or a HID++ report, that crashes the app or does more than that.
- A file written outside `%LOCALAPPDATA%\LogitechBattery`.
- A damaged history file or `settings.ini` that does harm.
- A crafted answer to the update check (a rogue server, a proxy or a captive portal) that crashes the app, or makes it open a URL outside the release pages of this repository.

## Facts that help a report

- The app needs no administrator rights.
- It reads only the Logitech vendor HID++ collections, not keyboard or mouse input.
- Its only network request is the update check to `api.github.com`. It runs only when the user turns on "New app versions" or clicks "Check for updates…". The app downloads nothing and updates nothing: it can only open a release page in the browser.
