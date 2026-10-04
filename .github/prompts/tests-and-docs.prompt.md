---
description: Add focused AuraSync tests and keep project and user documentation aligned with shipped behavior.
mode: agent
---

Add or update tests/documentation for this change:

${input:task:Describe the behavior, expected result, or documentation gap}

Read the affected implementation and existing docs first. Keep the change scoped to the behavior:

- `readme.md` is the project overview; `docs/user-guide.md` explains setup and operation; `docs/architecture/overview.md` describes ownership and data flows.
- Update `contracts/api/openapi.yaml` when a public API shape changes. Keep claims consistent across docs and mark limitations accurately.
- Test observable behavior and relevant edge cases. Prefer the smallest relevant tests/build; do not imply physical hardware was tested unless it was.
- For documented Nano/WS2812 wiring or firmware, keep LED count, protocol, pin, baud rate, and profile instructions consistent with the example.
- Keep screenshots genuine and current; do not fabricate UI or hardware evidence.

Run focused validation plus `git diff --check`, then report exactly what changed and what was verified.
