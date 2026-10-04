---
description: Implement or fix AuraSync Electron lifecycle, capture IPC, or packaging behavior.
mode: agent
---

Implement this Electron desktop change:

${input:task:Describe the requested desktop behavior or defect}

Work in `apps/desktop/electron`. Electron is the privileged host for windows/splash, trusted desktop-capture IPC, local .NET service lifecycle, and Windows packaging. Angular owns UI and sync state; the service owns configuration and serial output.

- Inspect only the relevant `main.cjs`, `preload.cjs`, Angular bridge declarations, scripts, and tests before editing.
- Preserve `contextIsolation: true`, `sandbox: true`, `nodeIntegration: false`, trusted-origin IPC validation, and denial of untrusted navigation/windows.
- Report launch, health, capture, and shutdown failures explicitly; ensure spawned service processes and packaged runtime assets are handled correctly.
- Run `node --check` on changed CommonJS files and the smallest relevant UI/package validation.

Summarize behavior, changed files, checks, and any Windows/hardware limitations.
