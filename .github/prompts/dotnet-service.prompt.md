---
description: Implement or fix an AuraSync .NET 10 API, application, domain, or infrastructure behavior.
mode: agent
---

Implement this .NET service change:

${input:task:Describe the expected endpoint, domain behavior, or defect}

Work in `apps/led-sync-service`. Keep responsibilities clear: API maps/validates transport contracts; Application owns use cases and ports; Domain owns core models/invariants; Infrastructure implements persistence and platform/output adapters.

- Trace only the relevant contract, route, use case, adapter, and tests; reuse local patterns.
- Preserve `%LOCALAPPDATA%\LedSync\profiles.json` and compatible profile/API behavior unless migration is part of the task.
- Keep the API loopback-only and retain origin protections. Validate requests and frame lengths; use explicit, actionable errors rather than silent defaults.
- Update `contracts/api/openapi.yaml` for public request/response changes and add focused tests for behavior changes.
- Run the relevant .NET test project and build when needed; avoid rerunning broad checks without a reason.

Summarize the behavior, files touched, and exact validation performed.
