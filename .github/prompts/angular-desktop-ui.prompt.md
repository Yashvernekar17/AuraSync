---
description: Implement or fix AuraSync Angular UI behavior using existing component and API patterns.
mode: agent
---

Implement this Angular UI change:

${input:task:Describe the expected UI behavior or bug}

Work in `apps/desktop/angular`. Angular owns presentation, profile/display UI state, layout geometry, screen-edge sampling, RGB processing, and API-client behavior.

- Read the relevant template, component, API model/service, styles, and tests before editing; avoid unrelated workspace-wide scans.
- Follow existing standalone-component, `FormsModule`, service, and stylesheet patterns. Keep desktop capture in Electron IPC; do not add renderer Node access.
- Preserve profile/API shapes unless coordinating a service change. Handle loading, duplicate actions, request races, and errors explicitly where relevant.
- Update `docs/user-guide.md` or `readme.md` only when the user workflow or documented behavior changes.
- Run `npm run typecheck --workspace @aurasync/angular` for type-affecting changes and the Angular build or focused UI checks when the change warrants it.

Summarize the user-visible change, touched files, and checks run.
