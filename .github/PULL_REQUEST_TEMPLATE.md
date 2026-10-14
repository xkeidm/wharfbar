## What

<!-- One or two sentences on the behavior change. -->

## Why

<!-- The user-facing or maintenance reason. -->

## Checks

- [ ] `dotnet build WharfBar.sln`
- [ ] `dotnet test` when the change has logic worth testing
- [ ] Docs in `docs/` and `.agent/` updated if behavior or architecture changed
- [ ] Quit was the tray item or Alt+F4, not a force-kill, if the app was run
