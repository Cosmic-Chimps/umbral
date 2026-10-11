---
title: "Drop the configuration-file requirements from specs 002 and 003 before they are built"
kind: gap
status: pending
created: 2026-10-10
summary: "Spec 004 (clarified 2026-10-10) orders the work as 003 first without its file parts, then 004. 003's spec and plan and 002's spec still require extending the YAML document, so building 003 as planned would add file code that 004 deletes."
---

## Why
The clarification session of `specs/004-remove-config-document/spec.md` (FR-014 and Clarifications, Q4) settled that feature 003 is built before 004 without its configuration-file parts, and that 002 drops its own. Today:
- `specs/003-per-app-connectors/per-app-connectors.spec.md` (003 worktree, uncommitted) still has FR-016 to FR-018 and SC-005. Its `plan.md` lists `ConfigFile.fs` and `ConfigExport.fs` changes and says "The YAML document gets two optional keys". Its contracts also list the YAML keys.
- `specs/002-generic-oidc-connector/spec.md` still has User Story 3, FR-019, FR-020, FR-022 and SC-007, and its Input mentions "from the YAML configuration file (config parity, export round-trip)".

## What to do
- In 003, remove FR-016 to FR-018 and SC-005, the file tasks and paths in `plan.md`, and the YAML keys in `contracts/`. Note that spec 004 supersedes them, and update the plan's "Config parity" gate row.
- In 002, remove User Story 3, FR-019, FR-020, FR-022 and SC-007 (keep FR-021, which is about startup, not the file), and note that spec 004 supersedes them.

## Where
`specs/003-per-app-connectors/` (in the 003-per-app-connectors worktree), `specs/002-generic-oidc-connector/spec.md`.
