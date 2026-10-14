@AGENTS.md

# Claude Code notes

- Canonical AI context is `AGENTS.md` + `.agent/` + `docs/` (imported above). Do not duplicate it here.
- Before coding, read `.agent/project.md`, `.agent/boundaries.md`, `.agent/conventions.md`, and the relevant `docs/` file.
- Update `.agent/` and `docs/` in the same change when behavior or architecture shifts.
- Never run the app and force-kill it (AppBar stays reserved); close it normally.
- Do not add `global.json`.
