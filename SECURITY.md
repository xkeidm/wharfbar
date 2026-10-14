# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| `1.0.0-beta` (current) | Yes |
| Pre-beta / untagged trees | No formal support |

Security fixes land on the default branch and ship in the next beta (`1.0.0-beta.N`) or stable release as appropriate. See [CHANGELOG.md](CHANGELOG.md) and [CONTRIBUTING.md](CONTRIBUTING.md) for versioning.

## Reporting a vulnerability

**Prefer a private report.** Do not open a public issue for anything that could be exploited or that reveals a sensitive shell failure mode.

1. **GitHub Security Advisories (preferred):**  
   [Report a vulnerability](https://github.com/xkeidm/wharfbar/security/advisories/new)
2. If advisory filing is unavailable to you, use a **private** channel the maintainer publishes (for example a security contact on the GitHub repository). This project does not currently list a dedicated security email in-repo.
3. **Public Issues** are fine only for **non-sensitive** concerns (docs typos, unclear quit instructions, general hardening questions with no exploit detail).

Please include: WharfBar version (`InformationalVersion` from About), Windows build, steps to reproduce, and impact. Omit proof-of-concept payloads that weaponize the issue; a clear description is enough.

We will acknowledge private reports when we can and coordinate disclosure after a fix is available or agreed.

## Scope notes (WharfBar)

WharfBar is a Windows shell companion: it registers an **AppBar**, can **hide and restore the taskbar**, and talks to other shell surfaces (tray overflow, clipboard island, and similar). Misuse or crashes can leave the desktop in a bad state (stuck reserved strip, taskbar visibility) until Explorer is restarted or the watchdog restores the taskbar.

Please report failures in those paths privately when they could be abused or leave users without a taskbar. Do not ask for or publish step-by-step exploit recipes against AppBar, taskbar hide/restore, tray peek, or related Win32 surfaces in public issues.

Out of scope for this policy: general Windows / Explorer bugs unrelated to WharfBar, and social-engineering reports without a product vulnerability.
