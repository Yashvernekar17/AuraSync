---
description: Implement a cross-cutting AuraSync feature or fix with focused investigation and validation.
mode: agent
---

Implement this AuraSync change:

${input:task:Describe the requested outcome, expected behavior, and any constraints}

Work end-to-end, but keep the investigation and diff scoped to the request:

1. Trace the relevant code path and existing tests/contracts before editing; search for reusable patterns instead of scanning unrelated areas.
2. Keep ownership intact: Angular owns UI and RGB sampling/processing; Electron owns privileged desktop lifecycle and capture IPC; the .NET 10 API/Application/Domain/Infrastructure layers own service behavior in their existing boundaries.
3. Preserve profile data at `%LOCALAPPDATA%\LedSync\profiles.json` and existing API/profile compatibility unless a migration is explicitly requested.
4. Implement related UI, service, contract, documentation, and tests together when required. Report capability limitations and distinguish automated results from hardware verification.
5. Run the smallest relevant type-check, test, or build. Do not run a full suite unless focused checks indicate it is needed.

Keep errors visible and actionable, avoid unrelated cleanup, and summarize the behavior changed, files touched, and exact checks run.
