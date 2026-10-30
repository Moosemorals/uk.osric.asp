<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# OPS-03 — There is no CI pipeline

| | |
| --- | --- |
| Severity | Medium |
| Area | Operations, quality |
| Location | `.github/` (no `workflows/`), `Makefile` |

## Problem

The Makefile has `format-check` and `license-check` targets, and `docs/architecture.md` says schema
changes are tested with `make test-web`, but nothing runs automatically. There are no GitHub Actions
workflows at all. As a result:

- `make format-check` is currently broken (BUG-10) and nobody noticed.
- The stale generated files (BUG-11) accumulated unnoticed.
- A commit can break the build, the tests or the licence headers without any signal.
- The Makefile is the only task runner, which is good, so CI can simply call it.

## Fix outline

1. Add `.github/workflows/ci.yml` triggered on `push` and `pull_request` to `trunk`.
2. Steps:
   - `actions/setup-dotnet` with the version from `global.json`;
   - `make build`;
   - `make test` (unit, specs, web);
   - `make format-check`;
   - `make license-check` (install `reuse`, e.g. `pipx install reuse`);
   - optionally `make image` to prove the container build, once it is fast enough.
3. Keep every step a Makefile target so local and CI runs are identical.
4. Add branch protection requiring the workflow once it is green.
