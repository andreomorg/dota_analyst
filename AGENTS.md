# DotaAnalyst

## Working together with Claude Code

Two agents (Codex and Claude Code) work in this same checkout at the same time. Before any change, read and follow the shared plan `work/agents/plan.md`: claim a task there first (`Dono: codex`), edit only the files in the task's scope, take the shared locks it lists (`[colunas]`, `[docker]`, `[dotnet]`), never commit/push/checkout/reset/stash without the owner's explicit request, and record the result in the task when done. Explanations to the owner are in Portuguese.

Read `CLAUDE.md` for architecture and domain contracts before changing application code. Preserve the existing React API, Steam authentication, replay states, minute-based statistics, baselines and minimap geometry.

## UI source of truth

The user requires professional portfolio quality and faithful implementation of Stitch project `13241258925802792561`: https://stitch.google.com/projects/13241258925802792561.

Current user priority: finish desktop first. New mobile designs and implementation are deferred until the user requests them. Preserve existing mobile work; this priority overrides the desktop/mobile acceptance scope below for the current phase.

- Export and inspect the selected screen's HTML, assets and screenshot before implementing it. Preserve its composition, typography, spacing, colors, imagery and responsive structure.
- Do not redesign or simplify a selected screen while translating it to React. New functional controls or production adjustments must first have an explicit Stitch reference; preserve the original screens.
- Connect the existing real data and interactions without inventing production statistics, service status, latency, profile badges or navigation destinations.
- Every map reference must use the application's exact `dotaanalyst.client/src/assets/minimap.webp`, including heatmaps, deaths and wards. Never replace it with generated map artwork. Preserve `GameMap` and `mapGeometry` when implementing overlays in React.
- Validate each page visually at the reference CSS viewport on desktop and mobile with deterministic test data. Lint/build passing does not establish visual fidelity.
- Keep loading, empty, error, unavailable replay and authentication states complete and accessible. Preserve keyboard navigation, visible focus and chart alternatives.
- The local execution plan, task board and evidence live in `work/sprint-20-stitch-ui/`. Update the board truthfully: do not mark a task complete before its acceptance checks pass.
- `work/`, credentials and `.mcp.json` remain local according to `.gitignore`; never commit secrets or local MCP headers.
