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

- `make format-check` was broken (BUG-10) and nobody noticed.
- Stale generated files (BUG-11) accumulated unnoticed.
- A commit can break the build, the tests or the licence headers without any signal.
- The Makefile is the only task runner, which is good, so CI can simply call it.

## Original proposal (superseded)

1. Add `.github/workflows/ci.yml` triggered on `push` and `pull_request` to `trunk`.
2. Steps:
   - `actions/setup-dotnet` with the version from `global.json`;
   - `make build`;
   - `make test` (core, runtime, web and integration);
   - `make format-check`;
   - `make license-check` (install `reuse`, e.g. `pipx install reuse`);
   - optionally `make image` to prove the container build, once it is fast enough.
3. Keep every step a Makefile target so local and CI runs are identical.
4. Add branch protection requiring the workflow once it is green.

## What the gate is now

There are no pull requests here (see `CONTRIBUTING.md`), so a hosted runner would gate nothing. A
pre-push hook was tried and removed (DEC-72): it only ever delayed a push. The pipeline is therefore
a set of Makefile targets, and nothing else — no workflow file, no account, no secret, no runner
image to keep in step. Whatever eventually runs it, locally or on a rented machine, runs one command.

| Target | What it proves |
| --- | --- |
| `make check` | `format-check`, `license-check`, `audit`, all four suites, and a Release `publish` |
| `make ci` | the same, from `make clean`, that the build wrote no tracked file (`drift-check`), and that two image builds agree (`image-check`) |
| `make audit` | no NuGet package is vulnerable or deprecated (needs the network) |

Supporting changes:

- **Warnings are errors.** `Directory.Build.props` sets `TreatWarningsAsErrors` (plus
  `ContinuousIntegrationBuild`), so the tree that builds cleanly locally is the tree that passes.
  Putting it in the project file rather than the gate keeps one build: the stamps in `artifacts/`
  can never record a laxer build than the check assumes.
- **Release is proven, not assumed.** `publish` is part of `check`, so a Release-only break does
  not wait for `make deploy` to find it.
- **Hangs fail.** Every suite runs with `--blame-hang --blame-hang-timeout 5m` (`TEST_HANG_TIMEOUT`
  to change it): the budget tests burn a wall clock on purpose, so a wedged run needs a ceiling.
- **Deploy is gated and traceable.** `make deploy` runs `deploy-guard` (the host answers, the tree is
  this commit and nothing else, the Caddyfile is not still the placeholder), then `check`, then
  `image`: it refuses an uncommitted tree (an image that cannot be rebuilt from the commit its tag
  names is a rollback waiting to fail; `ALLOW_DIRTY=1` is the deliberate exception), deploys by
  commit tag rather than `latest` so the host can say what it is running, and curls `/heath` after
  the restart instead of reporting success blindly.

Still open: the gate runs when someone runs it, so a commit pushed without `make check` is
unverified until a deploy or `make ci` takes it. A self-hosted runner or a pre-receive hook on the
remote would close that, and would run `make ci`; both need infrastructure this repository does not
want yet. The rest of this file is kept as the original proposal.

