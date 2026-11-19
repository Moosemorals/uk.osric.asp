<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The host steps are a script

| | |
| --- | --- |
| Status | Proposed 2026-11-19 — waits for the owner. Nothing below is built |
| Size | S — one new script, one target cut back to its gate and its call, a line in `deploy/README.md` and the record. No behaviour change, and there is no suite for a shell script: the proof is `bash -n`, `make check` and one real deploy |
| Spike | None. Every claim below was read off `trunk` on 2026-11-19: the `deploy` and `deploy-guard` targets, `ci-install`, `deploy/ci/install-host`, `deploy/ci/check-push`, and the registry and host variables the target passes |
| Decisions | At the merge: **deploy-target-is-a-script** is new — the gate and the call stay in the target and the host steps live in `deploy/deploy-host` |
| Backlog | [`makefile-holds-the-deploy-recipe`](../backlog/makefile-holds-the-deploy-recipe.md) is this step |

## The short version

`make deploy` is the release command and it stays the only one (`code-one-gate`). What it is not is
the place the host's installation is written down: 136 recipe lines of package checks, file installs,
restarts and waits, inside an 801-line task runner. They move to `deploy/deploy-host`, which the target
calls once — the shape `ci-install` already has with `deploy/ci/install-host`, and `pull-install` with
`deploy/pull-backups.sh`.

The target keeps what belongs to a task runner: the prerequisite chain, the control-socket directory
the first `ssh` needs, and the call. The script keeps what belongs to the host: the packages it
insists on, the files it installs, the units it restarts, the scrapes it proves and the health wait at
the end, with the reason each step exists beside the command that performs it.

Nothing about the host changes — not the order, not one command, not one bound on a wait — and
nothing about the gate changes either. This is a move with one interface added: nine values the
Makefile already owns are handed to the script by name.

The helper is not a second entry point. `make help` lists the same targets, `deploy` is still what an
operator runs, and a script that only the target calls is what `install-host` already is — the
distinction `code-one-gate` draws is between entry points, not between a recipe and the file it runs.

## What exists today

| | |
| --- | --- |
| The target | `deploy: deploy-guard check test-browser image \| $(SSH_CONTROL_DIR)`, then 136 tab-indented lines, under 16 comment lines of prose |
| Its gate | `deploy-guard` — `HOST` set, a commit, a clean tree, no `example.com` in the Caddyfile, the host reachable; then `check`, `test-browser` and `image`, all as prerequisites, so nothing is pushed until the whole gate is green |
| Its callers | an operator, and nothing else: no hook and no unit runs `make deploy` |
| The precedent beside it | `ci-install` is a few lines ending in one call to `deploy/ci/install-host`, and `pull-install` is the same shape around `deploy/pull-backups.sh` |
| What the recipe uses from the Makefile | `$(SSH)`, `$(HOST)`, `$(LOCAL_IMAGE)`, `$(IMAGE_REF)`, `$(IMAGE_SHA_REF)`, `$(AUTHFILE)`, `$(REMOTE_AUTHFILE)`, `$(CI_STATUS_DIR)`, `$(GIT_SHA)` |
| What it reads from the tree | `deploy/uk.osric.asp.network`, `deploy/uk.osric.asp.container`, `deploy/prometheus.yml`, `deploy/prometheus.container`, `deploy/node-exporter.container`, `deploy/backup-snapshot.sh`, the two backup units, `deploy/ci/status-never-run.json`, `deploy/Caddyfile`, `deploy/caddy.container`, `deploy/fail2ban/filter.d/uk.osric.asp.conf`, `deploy/fail2ban/jail.local`, `deploy/fail2ban-metrics.sh` and its two units |
| The control socket | `SSH ?= ssh -o ControlMaster=auto -o ControlPath=$(SSH_CONTROL_DIR)/%C -o ControlPersist=60s`, and `$(SSH_CONTROL_DIR)` is an order-only prerequisite that creates it, because `ssh` will not |
| Why it is one target | a deploy is a sequence, so the recipe may not run with `-j`; the comment above it says so, and the quadlets are all written before the single `daemon-reload` that reads them |

The blocks, in the order they run, because the claim that none of them changes has to be checkable:

| Block | What it does |
| --- | --- |
| The host's packages | the aardvark-dns, rsync and fail2ban/nftables/`/usr/sbin/nft` checks, each naming the `apt-get` line that fixes it |
| The image | two local tags, both pushed, and the commit-named one pulled as `deploy` with the persistent auth file |
| The app's paths | `assistant.env` seeded `0600` once, `appsettings.Production.json` seeded `{}` once, and `.config/prometheus` created |
| The rootless units | the network, the app quadlet with its `Image=` line rewritten, Prometheus, node-exporter, and the snapshot script with its service and timer — then one `daemon-reload`, one restart of the three services and the timer enabled |
| The CI status seed | `/var/lib/uk.osric.asp-ci` created, `status.json` written from `deploy/ci/status-never-run.json` only when absent, chowned to `ci` when that account exists |
| The rootful units | `/etc/caddy` and the Caddyfile, the caddy quadlet, a reload and a restart; the fail2ban filter and `jail.local`, enabled and reloaded; the metrics collector and its units installed, started and enabled |
| The three waits | Prometheus reaching `app:8080/health` (10 × 2 s), the fail2ban counters readable through node-exporter (15 × 2 s), and the site's own `/health` (30 × 2 s) |
| The note | `deployed <sha>`, then how far the upstream is behind — a note, not a gate, because the push has already happened |

## The design

### 1. The target keeps the gate, the socket and the call

The prerequisites are the gate and they stay where the gate is defined: `deploy-guard`, then `check`,
then `test-browser`, then `image`, with `$(SSH_CONTROL_DIR)` order-only because the first `ssh` in the
script needs the directory its control socket lives in. The script is called after all of them, so a
gate that fails never starts the host work — the ordering `deploy-a-release-is-gated` records is kept
by the dependency line, not by the script.

`deploy-guard` does not move. Its checks are about the *destination of a release* rather than about a
host's state, they run before the gate, and two of them (a dirty tree, a commit to tag) are about this
checkout. Leaving it a target of its own keeps its usage message, its refusal order and its early
placement.

### 2. The script takes nine values, by name

The release needs values the Makefile owns, so they are passed as named environment variables on the
one call rather than as positional arguments: nine values, several of them opaque paths and refs, is
well past the count where a positional interface gets a value silently swapped and still runs.

| Value | What the script does with it | Why the script cannot derive it |
| --- | --- | --- |
| `HOST` | every `ssh` | the operator's, and the reason `deploy` takes an argument at all |
| `SSH` | every remote command | the connection that keeps a dozen commands to one host on one socket; overridable in the Makefile, and its options are not the script's to invent |
| `LOCAL_IMAGE` | the tag the local image was built as | the Makefile's registry block is where that name is decided |
| `IMAGE_REF` | the tag that follows `latest` | ditto |
| `IMAGE_SHA_REF` | the commit-named tag: pushed, and the one the host pulls | ditto, and it has to be the abbreviation the image was tagged with |
| `AUTHFILE` | the two local pushes | where this machine keeps its registry login |
| `REMOTE_AUTHFILE` | the pull as `deploy` | an absolute path on the host, and the reason it is not the runtime directory |
| `CI_STATUS_DIR` | the status seed | Caddy's directory, shared with `ci-install`, so it is named once in the Makefile |
| `GIT_SHA` | the last line and the upstream note | the abbreviation the image was tagged with, so the two agree by construction |

Each is read with `${VAR:?}` naming `make deploy`, so a direct run that forgot one stops at once with
the line to type instead of half-installing a host. There is no fallback for any of them: a default in
the script would be a second place the value is decided, which is what
`deploy-quadlets-are-the-source-of-truth` exists to prevent.

`SSH` is word-split deliberately when used — it is a command and its options, exactly as the Makefile
uses it unquoted today — and the one place that would read as an accident says why.

### 3. The script reads the tree from its own path

`deploy/deploy-host` resolves its own directory, so every file it installs is read from
`$deploy_dir/...` and the upstream note runs `git` in the repository root above it. That is
`install-host`'s `directory=$(cd "$(dirname "$0")" && pwd)` applied to the other side of the
connection, and it is what lets `bash -n` and a local run happen from anywhere.

The working directory still matters for one thing: the upstream comparison is about *this* checkout,
so the script changes to the repository root before it asks, rather than trusting the caller's `cwd`.

### 4. `$$` becomes `$`, and one block is interpolated

Moving a recipe into a script rewrites every escaped `$$` as a plain `$`, because make's escape is
gone and the shell is now the one that reads it. In this recipe that is the counters in the three
waits and the branch test in the upstream note, and a missed one is a remote command that compares
against the empty string and passes — the kind of defect no gate here would catch, which is why it is
the first thing the diff is read for.

One block names a Make variable inside a remote script that is otherwise single-quoted: the CI status
seed, where `CI_STATUS_DIR` appears four times and no remote `$` does. It becomes a concatenation —
single-quoted text, `"$ci_status_dir"`, single-quoted text — so the directory is the caller's value
and the remote text stays literal.

### 5. A failure says what failed

Today a failed line prints its own message and then `make: *** [Makefile:NNN: deploy] Error 1`, where
the line number points at the command. After the move that becomes the script's exit status and the
target's line, so each block keeps or gains the sentence it already prints: the three package checks
name the missing package and the `apt-get` line, the three waits name what could not be reached and
where to look, and the rest rely on ssh printing the remote command's own error.

`set -euo pipefail` at the top, as in every other script under `deploy/`: unset variables and a
failure anywhere in a pipe are failures, which is stricter than a make recipe and is what the waits
want — a `grep -q` that cannot read the file must not read as "not yet".

### 6. The target as it would be

```make
# The gate, the socket it needs and one call. The release itself is deploy/deploy-host, beside the
# files it installs, so the reason for a step sits with the step and `bash -n` reads it without a
# host. The whole gate runs before the image is built, and therefore long before anything is pushed:
# a tag that has been pulled by the server cannot be withdrawn. Do not run this target with -j; the
# order is the point.
deploy: deploy-guard check test-browser image | $(SSH_CONTROL_DIR)
	@HOST='$(HOST)' SSH='$(SSH)' \
		LOCAL_IMAGE='$(LOCAL_IMAGE)' IMAGE_REF='$(IMAGE_REF)' IMAGE_SHA_REF='$(IMAGE_SHA_REF)' \
		AUTHFILE='$(AUTHFILE)' REMOTE_AUTHFILE='$(REMOTE_AUTHFILE)' \
		CI_STATUS_DIR='$(CI_STATUS_DIR)' GIT_SHA='$(GIT_SHA)' \
		deploy/deploy-host
```

The comment above the target keeps only what is about running the target — the gate order and the
`-j` warning. The prose about what each host step is for travels with the step.

`deploy/deploy-host` opens like every other script under `deploy/` — shebang, the two SPDX lines, a
paragraph saying what it is and how it is run, `set -euo pipefail`, its comments as `#` — and is
committed `0755`, as `install-host`, `check-push`, `backup-snapshot.sh` and `pull-backups.sh` already
are.

## What it touches

| Path | Change |
| --- | --- |
| `deploy/deploy-host` | new: the host steps, their comments and the three waits |
| `Makefile` | `deploy` becomes the target above; the comment above it is cut to the gate order and the `-j` warning |
| `deploy/README.md` | the file tree gains `deploy/deploy-host`, and the paragraph naming the source of truth says `make deploy` calls it |
| `docs/backlog/makefile-holds-the-deploy-recipe.md` | `Status` becomes `planned`, `Plan` names this file |
| `docs/backlog/README.md` | the row's status becomes `[planned](../plans/deploy-host-script.md)` |
| `.github/copilot-instructions.md` | checked, not changed: "The `Makefile` is the only entry point" still holds, because the helper is called by it as `install-host` is |

## What proves it

| Proof | How |
| --- | --- |
| The script parses | `bash -n deploy/deploy-host` |
| The interface refuses | running it with no `HOST` prints the usage line naming `make deploy` and exits non-zero, before any `ssh` |
| The target is unchanged in what it means | `make -n deploy HOST=deploy@example` prints the gate's prerequisites in the same order and one recipe line, and `make help` is byte-for-byte the same |
| Nothing else moved | `git diff --stat` touches no other target; `pull-install`, `ci-install` and the `remote-*` targets are untouched |
| The tree is still the source of truth | every file the script installs is still read from `deploy/`, and no file content changes |
| The gate is green | `make check` |
| A release still works | one real `make deploy HOST=deploy@<server>` from a clean trunk, and `/health` answers afterwards |

There is no unit test to write: the code under test is a shell script whose every command is a
remote side effect, and the repository has no harness for the scripts under `deploy/ci` either. The
proof is the syntax check, the refusal, the gate and one deploy — the same evidence `install-host`
and `check-push` stand on.

## Documentation

- `deploy/README.md`: `deploy/deploy-host` in the file tree; the source-of-truth paragraph gains the
  sentence that `make deploy` calls it, so a reader who wants to change what a deploy installs is sent
  to the file that installs it.
- `docs/decisions.md`: **deploy-target-is-a-script** at the merge, recorded in
  `deploy/deploy-host`, the `Makefile` and `deploy/README.md`.
- `/help` and the author manual: nothing — this is not something a page author can do.
- `docs/architecture.md`: nothing — it points at `deploy/README.md` for the deployment, which is
  still the right place.

## The record at the merge

- **deploy-target-is-a-script** is written into the Deploy area of `docs/decisions.md`: `make deploy`
  is the entry point and `deploy/deploy-host` performs the host installation, so the steps sit beside
  the files they install and the `Makefile` names the gate rather than the host.
- `deploy-quadlets-are-the-source-of-truth` is edited in place to name the script, because "the host
  is never edited by hand" now leans on the script as well as on the quadlets.
- `code-one-gate` is *not* edited: a helper its entry point calls is not a parallel script, and the
  decision already describes `install-host` the same way.
- The item closes at the merge, as every resolved item does: its row moves to
  [`closed.md`](../backlog/closed.md) keeping its slug, this plan's `Status` becomes merged and it
  moves to [the archive](archive/), and the links into it are retargeted in the same change. The row
  does not move up the order before then, because a plan is not urgency.

## Risks

| Risk | What answers it |
| --- | --- |
| A `$$` in the recipe is copied as `$$` and the remote command silently compares against an empty string | The diff is read for `$$` before anything else, and a deploy is judged on its output: the waits name what they could not reach, and the note prints the sha |
| A path resolves differently in the script than it did in the recipe | Every read is from `$deploy_dir`, which is the `deploy/` the file lives in; `bash -n` and a `make -n` diff show the args, and one real deploy installs from the same files |
| A failure loses the make line number that identified it | Each block keeps the sentence it already prints; where a block had none the message is the remote command's own |
| `SSH` word-splitting surprises a caller who quotes it | It is the same value the Makefile splits today, and the script's one comment says the split is deliberate |
| The extraction is the moment a step is dropped or reordered | The blocks table above is the checklist: 8 blocks, in order, and the diff is read against it line by line |

## Considered and rejected

- **A `Makefile` `include` of `deploy/deploy.mk`** — shortens the top-level file without reducing
  anything and adds a second place to look for a target. The host script is the extraction with a
  reason.
- **Positional arguments** (`deploy-host "$HOST" "$IMAGE_SHA_REF" ...`) — nine values, several of them
  opaque, and a swap between `CI_STATUS_DIR` and `IMAGE_SHA_REF` fails on the host minutes later
  rather than at the call. `install-host` takes two positional arguments because it has two; this
  one has nine and they are named.
- **Keeping the tag, push and pull in the Makefile** — the target would then either run the host's
  package checks after the push, so a host missing `apt-get install -y aardvark-dns` is discovered
  once an image is already in the registry where it cannot be withdrawn, or need a second target to
  split the checks from the install. The five image lines go with the rest, in the order they run
  today.
- **`set -x` to restore which command failed** — it prints every command and its arguments on every
  deploy, and the blocks that matter already say what they were doing.
- **Building the `ssh` command inside the script from `SSH_CONTROL_DIR`** — a second copy of the
  control-socket options, which would drift from the `SSH` the `remote-*` targets use.
- **Moving `deploy-guard` into the script** — the guard runs before the gate and refuses a checkout,
  not a host; in the script it would run after the image was built.
- **`bash -n` for every script under `deploy/` in `make check`** — worth having, but it is a gate
  change that touches files this step does not move, so it is not this step's.

## Not in scope

- `pull-install` and `deploy/pull-backups.sh` — the backup host's half, a different host and already
  the extracted shape.
- `ci-install` and `deploy/ci/` — the CI account, unchanged.
- Reading `/ci/status.json`, or making the running image the one the browser suite drove — that is
  [`ship-the-tested-image`](../backlog/ship-the-tested-image.md).
- Any change to what the host installs, the order it installs, or a wait's bound.
