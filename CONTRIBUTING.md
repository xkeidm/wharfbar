# Contributing

WharfBar is a Windows top bar and dock. Issues and pull requests are welcome.
The public docs are English. The only extra translation is [README.tr.md](README.tr.md).

## Before you start

- Read [AGENTS.md](AGENTS.md) for the repository's AI-agent entry point and project-specific instructions. It links to the canonical context in `.agent/` and `docs/`.
- Read [docs/00-overview.md](docs/00-overview.md) and [docs/04-gotchas.md](docs/04-gotchas.md).
- Behavior and boundaries live in [docs/](docs/) and [.agent/](.agent/). A change to either belongs in the same pull request.

## Planning and issues

The public roadmap is [GitHub Milestones](https://github.com/xkeidm/wharfbar/milestones).
New work should be an issue assigned to the right milestone (beta polish, post-beta candidates, or Stable 1.0).
Use the bug and feature issue templates when they fit.

## Versioning

WharfBar uses [Semantic Versioning](https://semver.org/). The current public line is **`1.0.0-beta`**.

| Stage | Version shape |
|-------|----------------|
| Current beta | `1.0.0-beta` |
| Later betas | `1.0.0-beta.N` |
| Optional release candidates | `1.0.0-rc.N` |
| First stable | `1.0.0` (drops the Beta label in the app) |
| After stable | PATCH / MINOR / MAJOR as usual |

Set `<Version>` / `<InformationalVersion>` in `src/WharfBar/WharfBar.csproj`. **About shows `InformationalVersion`.** `AssemblyVersion` stays a numeric four-part value (SDK default from the major.minor.patch of `Version`).

Record user-facing changes in [CHANGELOG.md](CHANGELOG.md). Report vulnerabilities per [SECURITY.md](SECURITY.md).

## Build

You need Windows and the .NET SDK 8 or 9. The project targets `net8.0-windows10.0.19041.0`.
There is no `global.json` on purpose. Pinning the SDK to 8.x breaks the build on a machine that only has the .NET 9 SDK.

```bash
dotnet build WharfBar.sln
dotnet test
dotnet run --project src/WharfBar
```

`run.bat` builds and starts `WharfBar.exe` detached, so closing that console does not kill the app.

## Quit safely

Running the app reserves a strip along the top of the screen and hides the Windows taskbar.

Quit from the tray (**Quit WharfBar**) or with Alt+F4. Both restore the taskbar and remove the AppBar.
A force-kill (`Stop-Process -Force`, detaching the debugger, closing a raw `dotnet run` window) skips `ABM_REMOVE`.
The taskbar watchdog restores the taskbar in about 0.5 seconds. The reserved strip can stick until `explorer.exe` is restarted.

A second instance exits because of a named mutex. `CloseMainWindow` does nothing here (`MainWindowHandle` is 0). Send `WM_CLOSE` to the window.

## Pull requests

- Keep the change focused. Say why in the description.
- Identifiers, API names, and config keys stay English.
- Do not combine Mica with `AllowsTransparency=true`.
- Do not read another process’s memory to mirror tray icons.
- On exit, AppBar removal and taskbar restore stay paired.
- Run `dotnet build WharfBar.sln`. Run `dotnet test` when you touch logic that already has tests.

## Conduct

This project uses the [Contributor Covenant](CODE_OF_CONDUCT.md).
