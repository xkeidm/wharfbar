# `.agent/` — canonical context for AI

This folder **is the source of truth for WharfBar agents.** Cursor `.cursor/rules` only points here and at `docs/`.

## How to use it

1. On every task, read [`project.md`](project.md) and [`boundaries.md`](boundaries.md) first.
2. For code/style, [`conventions.md`](conventions.md).
3. UI / user flows: [`screens/README.md`](screens/README.md) — a separate page
   per screen (top bar, dock, settings).
4. Stack / roadmap: [`context/stack.md`](context/stack.md), [`context/roadmap.md`](context/roadmap.md).
5. For technical detail, the root [`docs/`](../docs/) — especially Win32 and gotchas.
6. Root index: [`../AGENTS.md`](../AGENTS.md).

## What is not written here

- Long Win32 copies → `docs/02-win32-appbar-dwm.md`
- Human-facing marketing → `README.md`
- Thin IDE rules → `.cursor/rules/` (do not bloat them)

## Update rule

When behavior, a boundary, or the architecture changes, update the matching `.agent/` and `docs/` file **in the same PR/change**.
