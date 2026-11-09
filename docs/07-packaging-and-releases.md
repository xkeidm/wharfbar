# 07 — Packaging and Releases

> Canonical reference for how WharfBar is packaged and released: downloadable assets, the per-user
> install model, the `--quit` shutdown channel, and the local + CI release pipeline. Behavioural
> traps: [`04-gotchas.md`](04-gotchas.md); contributor rules: [`CONTRIBUTING.md`](../CONTRIBUTING.md);
> service map: [`01-architecture.md`](01-architecture.md).

## Artifacts

Every GitHub Release is built from one tag `v<semver>` (e.g. `v1.0.0-beta.2`) and carries three
files:

| Asset | Use | Notes |
|-------|-----|-------|
| `WharfBar-Setup-<version>-x64.exe` | Installer (Inno Setup 6) | Per-user, no admin/UAC; Start-menu entry, optional desktop shortcut, uninstaller in Settings → Apps |
| `WharfBar-<version>-win-x64-portable.zip` | Extract and run | `WharfBar-<version>-win-x64-portable\WharfBar.exe` + `Wallpapers\` + `LICENSE`; nothing is installed |
| `SHA256SUMS.txt` | Checksums | `sha256sum` format: `<lowercase hash>  <file name>`; verify before running |

Both assets come from the same self-contained single-file win-x64 publish (no .NET runtime install
needed): the installer installs that payload (`WharfBar.exe` + `Wallpapers\` + `LICENSE`), and the
portable zip packages the same files for extract-and-run. `LICENSE` is included in both, as MIT
requires.

**Requirements:** Windows 10 version 2004 (build 19041) or newer, or Windows 11; **x64 only**. The
app only touches per-user shell state (`%APPDATA%\WharfBar`, the Startup shortcut, HKCU), so the
installer never asks for elevation.

## Install model

| Item | Location / behaviour |
|------|----------------------|
| Program files | `%LOCALAPPDATA%\Programs\WharfBar` (default; `PrivilegesRequired=lowest`, no UAC) |
| Installed files | `WharfBar.exe`, `Wallpapers\`, `LICENSE`, `unins000.exe` / `unins000.dat` |
| Start-menu entry | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\WharfBar` (app + uninstaller) |
| Desktop shortcut | Optional task, **unchecked** by default |
| Settings | `%APPDATA%\WharfBar\config.json` — **not** the install dir; survives upgrades and uninstall |
| Autostart | Owned by the app: Settings → General → Start with Windows keeps `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\WharfBar.lnk`. The installer does not create it; uninstall deletes it |
| Uninstall entry | Settings → Apps (HKCU uninstall key, no elevation) |
| Installer languages | English + Turkish wizard UI |

Uninstall removes the app files, the Start-menu group, the desktop shortcut and the app-created
Startup shortcut; it deliberately leaves `%APPDATA%\WharfBar` (settings, island store, wallpapers,
plugin manifests) in place — see troubleshooting. **Upgrading:** run the newer setup over the old
install. The Inno `AppId` is stable, so an in-place upgrade is recognised instead of a second app or
a duplicate uninstall entry, and settings are kept because they live outside the install dir. Setup
closes the running app first (see `--quit`) and does not restart it; the finish page offers to
launch WharfBar. There is no auto-updater — users download the next release themselves.

## The `--quit` channel

`WharfBar.exe --quit` is the supported way for an installer, uninstaller, or script to stop a
running instance. It signals the running instance's named quit event; that instance shuts down
through its normal exit path (taskbar restore, `ABM_REMOVE`, tray removal) via `QuitChannel` +
`App.OnExit`, then releases the single-instance mutex **last** — so exit code `0` means the taskbar,
work area and every AppBar are already restored. The wait is bounded (10 s); Setup runs it from
`PrepareToInstall` before replacing files, uninstall from `[UninstallRun]` before deleting them.
`--quit` is not a second instance: `Program.Main` handles it before WPF starts and the process only
requests the quit and waits; the single-instance mutex still guarantees only one WharfBar runs.

| Exit code | Meaning |
|-----------|---------|
| `0` | No instance was running, or the instance quit cleanly |
| `1` | An instance is still running: an older build without `--quit` (cannot be asked), or the quit timed out |
| `2` | Unexpected error |

Setup **aborts** when WharfBar is still running after the bounded wait, with "WharfBar is still
running… quit it from the tray and run Setup again". Setup does not trust the `--quit` exit code
alone: it requests the quit and then waits (up to 15 s) for the single-instance mutex to disappear
before replacing any file, because a build older than `--quit` merely starts a doomed second
instance that exits `0` while the real instance keeps running. That is deliberate: replacing the
binary under a live instance would leave the taskbar and the AppBar un-restored.

**Never force-kill WharfBar to install, upgrade or uninstall it.** A hard kill skips `ABM_REMOVE`
and leaves the reserved top strip on screen until Explorer restarts; the watchdog only restores the
taskbar ([`04-gotchas.md`](04-gotchas.md)). A build older than this channel cannot be asked to quit
— quit it from the tray (**Quit WharfBar**) and run Setup again.

Normal quit paths: tray icon → **Quit WharfBar**, Alt+F4 on the bar, or the top-bar logo menu.
Closing the top-bar window is **not** an exit path; the process keeps living
(see [`04-gotchas.md`](04-gotchas.md)).

## Local release build

Requirements: Windows, the .NET SDK 8 or 9, and Inno Setup 6.3 or newer (`ISCC.exe` in its default
install directory or on `PATH`; `-InnoSetupCompiler <path>` overrides the lookup).

```powershell
pwsh installer/build-release.ps1 -Version 1.0.0-beta.2
```

Windows PowerShell 5.1 works too (`powershell -NoProfile -ExecutionPolicy Bypass -File
installer/build-release.ps1 -Version 1.0.0-beta.2`); the script deliberately avoids PowerShell 7-only
syntax so a machine without `pwsh` can still build a release.

Output lands in `artifacts/release/` (git-ignored). The script validates/normalises the version (a
leading `v` is accepted and stripped; shape `MAJOR.MINOR.PATCH[-prerelease]`), publishes
self-contained single-file win-x64 with `-p:Version` / `-p:InformationalVersion` set to the semver
(so About matches the tag), stages the portable zip (publish output + `LICENSE`), compiles
`installer/WharfBar.iss` via `ISCC` (`/DAppVersion=<semver>`, `/DNumericVersion=<x.y.z.0>`,
`/DOutputBaseName=WharfBar-Setup-<version>-x64`), and writes `SHA256SUMS.txt` over exactly those two
files. No MSI, MSIX or ARM64; the runtime identifier is pinned to `win-x64`.

## Version and tag rules

- **The release tag is the source of truth**: tag `v<semver>`, e.g. `v1.0.0-beta.2`; the local
  script and CI strip the leading `v`.
- `<Version>` / `<InformationalVersion>` in `src/WharfBar/WharfBar.csproj` are the developer default
  only; the release build overrides both, and About shows `InformationalVersion`.
- Inno receives the semver (`-DAppVersion`, shown in the display name) and the numeric `x.y.z.0`
  (`-DNumericVersion`, used for the file version and the Settings → Apps version), because Windows
  version fields cannot carry `-prerelease`.
- A version containing `-` is published as a **prerelease**; `1.0.0` is a full release. While the
  app is `1.0.0-beta*`, every release is therefore a prerelease.

## CI release workflow

[`.github/workflows/release.yml`](../.github/workflows/release.yml) runs on `windows-latest`:

| Trigger | Behaviour |
|---------|-----------|
| Push tag `v*` | Builds the assets, uploads them as workflow artifacts, then `gh release create v<version>` with generated notes, title `WharfBar <version>` and `--verify-tag`; adds `--prerelease` when the version contains `-` |
| `workflow_dispatch` (manual, with a `version` input) | Builds and uploads the same workflow artifacts only — **no** GitHub Release. Use it as a packaging dry run before tagging |

Steps: checkout (`fetch-depth: 0`) → .NET 8 (`8.0.x`) → resolve/validate the version from the tag
(or input) → ensure Inno Setup 6 (the image has it; Chocolatey installs it otherwise) →
`./installer/build-release.ps1 -Version $env:VERSION` → upload the three assets (hard-fails when
empty) → publish the Release with `contents: write`.

## Release checklist (maintainer)

1. Move the `[Unreleased]` entries in [`CHANGELOG.md`](../CHANGELOG.md) under the new version, add
   its compare link, and commit on `main`; optionally bump the csproj dev default (the tag wins).
2. Push `main`, then tag: `git tag v1.0.0-beta.2` and `git push origin v1.0.0-beta.2`.
3. Watch the **Release** workflow on the Actions tab (a failed build publishes nothing — the Release
   is the last step), then confirm the release has exactly the setup exe, the portable zip and
   `SHA256SUMS.txt`, plus the right notes and prerelease flag.
4. Smoke-test: download all three, check the two binaries against `SHA256SUMS.txt`, install (or
   extract the zip), confirm About shows the tag version, then upgrade over the previous install and
   uninstall once. Expect the SmartScreen warning (unsigned build).

### Verifying a download (SHA-256)

`SHA256SUMS.txt` is `sha256sum` format, so any checker accepts it. On Windows PowerShell:

```powershell
Get-Content .\SHA256SUMS.txt   # 6f1c…  WharfBar-Setup-1.0.0-beta.2-x64.exe
Get-FileHash .\WharfBar-Setup-1.0.0-beta.2-x64.exe -Algorithm SHA256   # compare, case-insensitive
```

On Linux/macOS/WSL, run `sha256sum -c SHA256SUMS.txt` from the download folder (file names are
relative).

## Unsigned build (beta limitation)

Neither `WharfBar.exe` nor `WharfBar-Setup-*.exe` is code-signed (certificate cost/validation is
out of scope for the beta). SmartScreen may show "Windows protected your PC" on first run:
**More info** → **Run anyway**. Do not claim the download is signed in release notes; the SHA-256
sums are the only integrity check.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Setup says WharfBar is still running / did not quit | Quit from the tray (**Quit WharfBar**) and run Setup again. A build older than `--quit` cannot be asked to quit — by design. Never `Stop-Process -Force` |
| SmartScreen blocks the setup or exe | **More info** → **Run anyway** (unsigned beta; verify the SHA-256 first) |
| A reserved strip remains along the top of the screen | The app was force-killed or crashed; the watchdog restores the taskbar but not the AppBar. Restart `explorer.exe` (Task Manager → Windows Explorer → Restart) |
| Uninstall left settings/plugin files behind | Expected: `%APPDATA%\WharfBar` is kept on purpose. Delete it manually for a clean slate |
| Uninstall cannot proceed while the app runs | The uninstaller runs `--quit` before deleting files; a build older than `--quit` must be quit from the tray first (never force-kill) |

## Out of scope (v1)

ARM64 / `win-arm64` builds, MSI or MSIX packaging, auto-update (users re-download), code signing /
SmartScreen reputation, per-machine install or elevation, Windows Store publishing.
