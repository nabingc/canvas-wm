# Orchestrator

Start a session in this folder and say:
> Act as the orchestrator in `orchestration/ORCHESTRATOR.md`. Start.

---

You coordinate four subagents — `re-analyst`, `spec-writer`, `developer`,
`tester` — to build CanvasWM one feature at a time from
`orchestration/feature-queue.md`, with a human gate after every stage.

## Per-feature loop

1. **Analyze** → `re-analyst` with the slug, description, study sections from
   the queue, and output path `findings/<slug>.md`. Status `analyzing`.
   Gate: show the summary, confidence mix, top unknowns, file path.
2. **Spec** → `spec-writer` on `findings/<slug>.md` → `specs/<slug>.md`.
   Status `specced`. Gate: show the public API, acceptance criteria count
   ([mac] vs [windows]) and assumptions needing review.
3. **Develop** → `developer` on `specs/<slug>.md`. Status `building`.
   Gate: files changed, API, SPEC-ASSUMPTION sites, build result.
4. **Test** → `tester`. Status `testing`. Gate: the test report.
   - FAIL → recommend developer (bug) or spec/human (spec problem); on approval
     route back, then re-run `tester`. Repeat until PASS or the human stops.
   - PASS with no [windows] criteria → status `done`.
   - PASS with [windows] criteria → status `awaiting-windows`; point the human
     to `windows-verification/<slug>.md`. When they report the result, mark
     `done` or route failures back to the developer.

## Rules
- **Never skip a gate.** Stop and wait for "go" (or edits) after each stage.
- **One feature in flight.** Do not start the next until the current one is
  `done`, `awaiting-windows` (human may approve moving on), or parked.
- **Dependencies:** do not start a feature whose `Depends on` entries are not
  `done` or `awaiting-windows`.
- **Pass paths, not contents.** Tell each subagent which files to read/write.
- **Clean room:** never give `spec-writer`, `developer` or `tester` paths under
  `~/Documents/trelliswm-re/`, or paste decompiled text into their prompts.
- **Keep the queue current** after every stage. Statuses:
  `pending → analyzing → specced → building → testing → awaiting-windows → done`
  or `blocked: <reason>` / `parked`.
- **Surface problems**: contradictory specs, thin findings, licensing/DRM
  touchpoints, or a stage that failed — bring them to the human.
- **Summarise at gates**; link files instead of dumping transcripts.

## Starting
Read the queue, pick the first eligible `pending` feature, state it in one
line, and begin stage 1.
