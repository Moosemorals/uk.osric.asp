<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The Makefile holds the host deploy recipe

| | |
| --- | --- |
| Difficulty | caffeinated-squirrel |
| Utility | high |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Deploy, Makefile |
| Location | `Makefile`, `deploy/` |
| Spike | — |

## Idea

`make deploy` is 143 lines of the 801-line Makefile: the host prerequisite checks, the environment
seeds, the network and quadlets, Prometheus, the backup units, Caddy, fail2ban, the metrics scrape,
the health wait and the status seed. That is a host installation script inside the task runner, and
it is the one place the Makefile does more than name a command.

The `ci-install` target shows the shape: 9 Makefile lines, and `deploy/ci/install-host` does the
account and repository work. The deploy target should be the same — `deploy: deploy-guard check
test-browser image | $(SSH_CONTROL_DIR)` plus one call to `deploy/deploy-host`, which holds the host
steps and reads the files in `deploy/`. `make deploy` stays the only entry point (`code-one-gate`);
the helper is called by it, exactly as `install-host` and the pull scripts are.

The extraction waited for `ops-no-ci` because that branch added the status seed to the deploy target;
that branch has landed, so the target is the one to extract.

## What this is not

- **Not a Makefile `include mk/*.mk` split.** That shortens the top-level without reducing the total
  and adds a second place to look; the host script is the high-value extraction.
- **Not a second entry point.** `make help` still lists `deploy`, and `deploy/deploy-host` is a
  helper called by it, like `deploy/ci/install-host`.
- **Not a behaviour change.** The host steps, their order, the health wait and the status seed are
  the same; the script is `bash -n`-testable and the Makefile is readable.
- **Not the CI status checks.** Reading `/ci/status.json` and proving the running image is
  [`ship-the-tested-image`](ship-the-tested-image.md); this only moves the host steps.

## What would prove it

`make deploy` still deploys; the Makefile's `deploy` target is a few lines plus the script call; the
host steps live in `deploy/deploy-host` and every `deploy/` file is still the source of truth; the
script passes `bash -n`; `make help` shows the same targets; `make check` and a real deploy are
green.

## The name a plan would propose

- **deploy-target-is-a-script** — the host installation `make deploy` performs lives in
  `deploy/deploy-host`, called by the target, not in the Makefile.
