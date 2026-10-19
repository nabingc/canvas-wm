# CanvasWM (working name)

An independent Windows tiling window manager on a zoomable canvas, built in
C# / .NET 10 by a gated multi-agent Claude Code pipeline.

## Layout
```
CLAUDE.md                     rules every agent follows (clean room, architecture)
.mcp.json                     REA MCP server (local checkout, Hopper provider)
.claude/settings.json         permissions for the pipeline
.claude/agents/               re-analyst, spec-writer, developer, tester
orchestration/ORCHESTRATOR.md the orchestrator prompt
orchestration/feature-queue.md features, dependencies, status
research/trelliswm-study.md   the behaviour study (copied from ~/Documents/trelliswm-re)
findings/ specs/              per-feature hand-off files
src/ tests/                   the .NET solution (created by feature 0)
windows-verification/         checklists you run on the Windows PC
```

## Run
1. Open this folder in VS Code and start Claude Code (or `claude` in a terminal).
2. Approve the `rea` MCP server when asked (first time only).
3. Say: *Act as the orchestrator in `orchestration/ORCHESTRATOR.md`. Start.*
4. Review at each gate and reply **go** or give corrections.

## Windows verification
When a feature reaches `awaiting-windows`, on the Windows PC:
1. Install the .NET 10 SDK; clone/copy this repo.
2. Follow `windows-verification/<slug>.md` and report the results back to the
   orchestrator (paste the output).

## Clean room
Only `re-analyst` reads decompiled evidence (kept outside this repo in
`~/Documents/trelliswm-re/`). Everyone else works from findings and specs.
