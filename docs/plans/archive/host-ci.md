<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The host runs the gate

| | |
| --- | --- |
| Status | Proposed, built and merged 2026-11-19. The names it proposed are in [the record](../../decisions.md); the host steps in §Tests, `ci-install` and the first pushed trunk are the post-merge install this checkout could not run |
| Size | M/L — one host account, a bare repository, the two hooks, the check and mirror scripts and their installer, two user units and a slice, one Makefile target, one static route served by Caddy from a host directory, the Caddy mount and header, and the docs; no page, endpoint or storage behaviour changes |
| Area | Deploy (host); the shell's one table of routes |
| Spike | None. The Makefile targets, the static-file pipeline, the shell catalogue, the Caddyfile and the host's existing shape were read off `trunk` on 2026-11-19. The host itself was not reachable from that checkout, so step 1 of the build starts by proving the assumptions it makes about the account, packages and podman |
| Decisions | Recorded: [deploy-ci-is-its-own-account](../../decisions.md#deploy-ci-is-its-own-account), [deploy-trunk-is-checked-after-push](../../decisions.md#deploy-trunk-is-checked-after-push) and [deploy-github-mirrors-the-host](../../decisions.md#deploy-github-mirrors-the-host) are new; `code-one-gate`, `deploy-quadlets-are-the-source-of-truth` and `deploy-one-small-host` are edited in place. The first says the gate is the same target on a different machine; the second says a push is accepted before it is checked; the third says the host is the upstream and GitHub a pruned copy |
| Backlog | [`ops-no-ci`](../../backlog/closed.md) was this step. It was the item's "still open" paragraph — the gate runs when someone runs it. [`ship-the-tested-image`](../../backlog/ship-the-tested-image.md) is the release half and [`makefile-holds-the-deploy-recipe`](../../backlog/makefile-holds-the-deploy-recipe.md) is the deploy-target cleanup; both were blocked by this step; the merge unblocked them |

## The short version

The gate exists and is one command; what is missing is something that runs it without being asked. The
host that already runs the site is the machine that gets asked. A push to `trunk` on the host is
accepted, a `post-receive` hook starts `make ci` in a **separate `ci` account**, and the result is
published as `/ci/status.json` by Caddy from a host directory, with the full log in the journal.
GitHub becomes a one-way, pruned mirror of the host's branches and tags, so the public copy is no
longer the upstream.

The three constraints that shape everything below:

- **The push is not blocked.** The owner chose an asynchronous gate. A push returns as soon as the
  host has the objects; the check is a transient systemd unit in its own slice. A red trunk is fixed
  forward, not refused at the door.
- **The check is `make ci`, not a second definition of green.** It runs the target the repository
  already documents, from a clean worktree, and writes what the target exited with. Nothing about
  the gate is re-implemented in shell.
- **The app and the CI share a machine, not a user.** `ci` has no sudo, no access to `deploy`'s
  configuration or volumes, and a 2 vCPU / 4 GiB slice. The app keeps its own 3 vCPU / 2 GiB cgroup,
  which remains the hard boundary a runaway page meets.

## What the owner decided

| Question | Answer | What it settles here |
| --- | --- | --- |
| When a push to trunk arrives | Accept it, check asynchronously, publish a status file the site serves | A `post-receive` hook, not a blocking `pre-receive`; a transient unit and a status file; a failed trunk is fixed forward |
| What runs on every push | `make ci` | The clean-tree drift check and the two image builds are part of what a push proves, not a nightly extra |
| Which refs are gated | Only `refs/heads/trunk`; branches are accepted unchecked | A branch push starts only the mirror. A branch is a proposal, and a proposal is not trunk |
| What GitHub carries | All branches and tags, prune what the host no longer has | The host is the source of truth. The first mirror is a no-op because the bare repository is cloned from GitHub, so no existing branch is lost |
| How the host writes to GitHub | A dedicated deploy key with write access to this repository only | The key lives in `~ci/.ssh`; no other GitHub credential exists on the host |
| How the host is prepared | A `make ci-install` target, plus cloud-init for future hosts | The account, repository, hooks, units and slice are installed from `deploy/`, exactly as the quadlets are |
| Protecting the running site | A 2 vCPU / 4 GiB systemd slice, low CPU and I/O weight | The build is deprioritized against the app; the app's own cgroup is unchanged |
| Who serves the status file | Caddy, from a host directory | Caddy mounts the status directory read-only and answers `/ci/status.json`; the app has no route or mount, and the status answers while the app is down |

Two consequences follow and are design decisions rather than open questions. **The mirror runs on
receive, not after the check**, so a commit is off the host before the gate has an opinion about it;
the status file says whether it passed. **A check that finds trunk has moved does not publish a
result**, because the newer push's check is the one that matters; the status file always describes the
current tip.

## What exists today

| Piece | Where | What it does |
| --- | --- | --- |
| The gate | [Makefile](../../../Makefile) | `check` is `format-check`, `license-check`, `audit`, `decisions`, `backlog`, `check-pages`, all four suites and a Release `publish`; `ci` is `clean`, `drift-check`, `check` and `image-check`; `deploy` is `deploy-guard`, `check`, the browser suite and the image work |
| The host | [deploy/README.md](../../../deploy/README.md), [cloud-init.yaml](../../../deploy/cloud-init.yaml) | One Hetzner CAX21 (Debian 13, arm64, 4 vCPU, 8 GiB), rootless podman quadlets for the app and Prometheus, rootful Caddy, and a `deploy` user with passwordless sudo. There is no `ci` user, no `git`, no `reuse` and no SDK |
| The remotes | `git remote -v` | `origin` is `git@github.com:Moosemorals/uk.osric.asp.git`; `trunk` tracks `origin/trunk`; `origin/page-inbox` exists. There is no host remote |
| The shell's files | [Program.cs](../../../src/uk.osric.asp.Web/Program.cs), [wwwroot](../../../src/uk.osric.asp.Web/wwwroot), [ShellCatalogue.cs](../../../src/uk.osric.asp.Web/Shell/ShellCatalogue.cs) | `UseStaticFiles()` runs before routing; every web-root file is in the route table, and `ReservedPaths` derives from that table, so one list both serves and reserves a key |
| The edge | [Caddyfile](../../../deploy/Caddyfile) | Rootful Caddy owns 80/443, adds and removes headers, encodes, and reverse-proxies everything to `127.0.0.1:8080`. It has no static route today; this plan adds one for the status file |
| The host's install pattern | [pull-install](../../../Makefile), [backup-snapshot.sh](../../../deploy/backup-snapshot.sh) | Host scripts and user units live in `deploy/`, are installed by a Makefile target over `ssh`, and rely on lingering for timers that outlive a login |
| Not built | — | No host repository, no receive hook, no `ci` account, no mirror, no status file and no systemd slice |

## Goals

1. Every commit that reaches `trunk` is checked by `make ci` on the host, and the result is visible
   at `/ci/status.json` with the commit, the times and the failing target.
2. The host is the upstream. GitHub is a one-way, pruned copy of its branches and tags, and the local
   checkout knows which is which.
3. CI is a separate account with no sudo and no route to the app's configuration, keys or storage.
   Its CPU and memory are capped so the site keeps its own share.
4. Setup is one idempotent target plus cloud-init for a new host, and `deploy/` is the source of truth
   for every file it installs.
5. The full log stays on the host; the site serves a small, non-secret status value.
6. The local loop is unchanged: `make check` is still the gate, and `make deploy` still gates a
   release. The host adds a signal, not a second workflow.

## Non-goals

- **A blocking receive hook.** The owner chose asynchronous. A push that waits fifteen minutes for a
  test suite is a push nobody makes often, and a rejected push cannot be reviewed while it waits.
- **Gating branches.** Only `trunk` is checked. A branch is a proposal; the status file is about the
  integrated tree.
- **The browser suite per push.** It stays where `code-browser-suite-is-opt-in` put it: `make deploy`.
  `make ci` is the four unit/integration suites, the records and the image proof. Moving the browser
  suite downstream of the image, and promoting the image the tests drove, is
  [`ship-the-tested-image`](../../backlog/ship-the-tested-image.md), not this step.
- **Outbound notification.** A failed check is read from the status file or the journal. Pushing the
  result to the owner is [`ops-no-outage-notification`](../../backlog/ops-no-outage-notification.md).
- **A CI dashboard.** The status file is JSON. A panel on `/admin/status` is a later page or shell
  change, not this one.
- **A second VM or a container-in-container build.** The host has the capacity with a slice, and the
  image targets already run under rootless podman. A clean-room runner is a different decision.
- **An automatic revert of a red trunk.** A revert is a commit with its own reasons, not a script's
  reaction to an exit code.
- **Branch protection on GitHub.** The mirror is a copy; the gate is on the host. Protecting the copy
  would make the mirror's forced update fail and give the copy authority it does not have.

## The design

### 1. The `ci` account and the bare repository

`ci-install` creates a system account that exists only to receive pushes and run the gate:

- no sudo, a `0700` home, its own subuid/subgid ranges and lingering, so its user manager and timers
  survive a logout;
- `/usr/bin/git-shell` as its login shell, so the same keys that reach `deploy` can reach `ci` but
  SSH can only carry `git-receive-pack`, `git-upload-pack` and `git-upload-archive` — there is no
  shell to run;
- `/home/ci/repo.git`, a bare repository cloned **from the public GitHub URL**, so every branch and
  tag that exists today is in the host's copy before the first push and the first mirror has nothing
  to delete; and
- `/home/ci/bin`, `/home/ci/.config/systemd/user` and `/var/lib/uk.osric.asp-ci` for the scripts, the
  units and the status file.

The bare repository's `github` remote is the SSH URL, not the HTTPS clone URL. The clone needs no
credential because the repository is public; the mirror needs the deploy key because writing does.
If the repository is ever made private, the clone uses the same deploy key, and nothing else changes.

### 2. What a trunk push does

Two hooks, both installed from `deploy/ci`:

**`pre-receive`** checks one thing and nothing else: a push that would delete `refs/heads/trunk` is
refused. A gate that accepts before it checks would otherwise accept the one push that makes the
status file meaningless.

**`post-receive`** reads the `<old> <new> <ref>` lines and:

- for `refs/heads/trunk` whose `new` is not all zeroes, starts a transient unit
  `systemd-run --user --collect --no-block --slice=uk.osric.asp-ci.slice` running
  `check-push <new>`;
- starts the mirror service, whatever the ref was; and
- prints the status URL and the unit name to the pusher, so the push's own output says where to look.

The push returns as soon as `post-receive` does; the check is the transient unit's work. A second push
while a check runs does not start a second concurrent check: `check-push` holds a `flock`, and a run
that finds `trunk` has moved before it started exits without writing a result. The newest tip is the
one that is checked, which is what the status file promises.

### 3. The check and the status file

`check-push <sha>`:

1. takes `/home/ci/.check.lock` with `flock`, so one `make ci` runs at a time;
2. re-reads `refs/heads/trunk` and exits if it is no longer `<sha>`;
3. creates a detached worktree of the bare repository at `<sha>` under `/home/ci/work.<random>` and
   installs a trap that removes it;
4. writes `running` for `<sha>` with the start time;
5. runs `make ci` in the worktree with a `PATH` that includes `/home/ci/.dotnet` and the
   `DOTNET_*` quiet settings; and
6. writes `passed` with exit code 0, or `failed` with the exit code and the target `make` named in its
   own `make: *** [...: target] Error N` line, with the finish time — but only if `trunk` is still
   `<sha>`.

The status write is a `jq`-built document written to a temporary file in the same directory, `chmod
0644`, and `mv`-ed over the old one. The directory mount in the app follows the rename; a file mount
would not, which is why the quadlet mounts the directory.

The file is:

```json
{
  "commit": "e608233…",
  "ref": "refs/heads/trunk",
  "result": "passed",
  "startedAt": "2026-11-19T10:00:00Z",
  "finishedAt": "2026-11-19T10:14:21Z",
  "exitCode": 0,
  "summary": "make ci passed"
}
```

`result` is `never-run`, `running`, `passed` or `failed`. `exitCode` is `null` until a run finishes.
`summary` is one line: the failing target for a failure, and a plain sentence otherwise. There is no
log path, no host name and no environment in the file; the full output is the journal.

### 4. Caddy serves the status file

The file is written by `ci` to `/var/lib/uk.osric.asp-ci/status.json`. Caddy is the server: the
rootful edge container mounts that directory read-only and answers the path before the reverse proxy:

```ini
# deploy/caddy.container
# The host's CI status, written by the ci account. The directory is mounted rather than the file so
# the writer's atomic rename is visible inside the container.
Volume=/var/lib/uk.osric.asp-ci:/srv/ci-status:ro
```

```caddyfile
@ciStatus path /ci/status.json
handle @ciStatus {
    rewrite * /status.json
    root * /srv/ci-status
    file_server
    header Cache-Control no-store
}
handle {
    @metrics path /metrics
    respond @metrics 404
    reverse_proxy 127.0.0.1:8080 {
        flush_interval -1
    }
}
```

The two `handle` blocks are one mutually exclusive group, so the status path is answered by the
file server and everything else is the app. The existing `/metrics` 404 moves inside the catch-all
`handle`, because a top-level `respond` is ordered after `handle` and the proxy would otherwise
swallow the path. The app has no route, no mount and no knowledge of the file; that is the point of
putting it on Caddy. The status answers even when the app is down, which
is when an owner is most likely to be looking at it, and the edge can read a `0755` directory with a
`0644` file that `ci` owns. The JSON's content type comes from the `.json` extension; `no-store` is
there because a status file that a browser caches is a status file that lies.

The shell's route table is still the one place a key is reserved. One entry is added to
[`ShellCatalogue`](../../../src/uk.osric.asp.Web/Shell/ShellCatalogue.cs), with a description that
names Caddy:

```csharp
new ShellRoute("/ci/status.json", "GET", ShellAudience.Everyone,
    "The host's CI status: the commit the last trunk push checked, when it ran and whether it passed. Caddy serves it; the app does not answer this path."),
```

The entry is a reserved record, not an app route: it keeps a stored page from claiming a key the edge
owns, and it tells the assistant what the path is. `ReservedPaths` already derives from this table, so
the two lists cannot disagree.

`make deploy` creates `/var/lib/uk.osric.asp-ci` if it is absent and seeds a `never-run` file if the
status file is absent, so the Caddy mount is a directory and the first request answers JSON rather
than 404. `ci-install` takes ownership of both for `ci`.

### 5. The mirror

`mirror` is a short script, not `git push --mirror` on a working repository:

```sh
git --git-dir=/home/ci/repo.git push --prune github \
  '+refs/heads/*:refs/heads/*' '+refs/tags/*:refs/tags/*'
```

The explicit refspecs and the `+` are deliberate. `--mirror` on a checkout also carries
`refs/remotes`, which would put `refs/remotes/origin/trunk` in a bare repository and then on GitHub.
Only heads and tags are the mirror, and the `+` is the force the copy is meant to take: the host is
the authority. `--prune` is what the owner asked for: a branch deleted on the host is deleted on
GitHub. A failed push leaves the host's copy untouched and the timer retries.

It runs from the `post-receive` hook and from `uk.osric.asp-mirror.timer` every fifteen minutes with
`Persistent=true`. The hook is the prompt; the timer is the guarantee. Both hold
`/home/ci/.mirror.lock`, so a push during a mirror cannot overlap it.

The credential is a dedicated ed25519 key in `~ci/.ssh/id_ed25519_github`, added to GitHub as a deploy
key with **Allow write access**. `~ci/.ssh/config` pins it with `IdentitiesOnly yes`; `known_hosts`
carries GitHub's host keys from install time. The key can write this repository and nothing else. It
is the only GitHub credential on the host, and it is not in the repository, the image or a page.

### 6. The resource slice

`uk.osric.asp-ci.slice` is a user slice under `ci`'s user manager:

```ini
[Slice]
CPUQuota=200%
MemoryMax=4G
CPUWeight=50
IOWeight=50
```

The transient check and the mirror service set `Nice=10`. The app's quadlet already has `--cpus=3`
and `--memory=2g`; the slice is the build's ceiling, the app's cgroup is still the hard floor. Rootless
podman puts the build's containers under the check's cgroup on this host; the first run confirms that
with `systemd-cgtop` and `podman stats`, because a container that escaped the slice would make the
4 GiB cap advisory.

`make ci` starts with `clean`, and `check-push` removes its worktree, so the build's own output does
not accumulate. The persistent growth is the NuGet cache and the podman layers under `~ci`; both are
small for this tree, and `df -h /home` during the first runs is the measurement that says whether that
stays true.

### 7. Installation: `make ci-install` and cloud-init

`make ci-install HOST=deploy@moosemorals.com` is the one setup command. `HOST` is the existing
`deploy` connection; the `ci` connection is derived as `ci@` the same host. It is idempotent and fails
with the missing piece named, in the style of `deploy`:

- `ssh` and `sudo -n` reach the host;
- the `ci` user exists, with no sudo, `0700` home, `git-shell`, subuid/subgid and lingering;
- `git`, `reuse` and `jq` are installed, and the SDK version read from `global.json` is under
  `/home/ci/.dotnet` (`dotnet-install.sh`, not a distribution roll-forward);
- `/home/ci/repo.git` exists, cloned once from the public GitHub URL, with `HEAD` on `trunk` and a
  `github` remote on the SSH URL;
- `deploy/ci`'s hooks, scripts, slice, service and timer are installed, `daemon-reload` is run, and
  the mirror timer is enabled;
- `/var/lib/uk.osric.asp-ci` and its `never-run` status file exist and belong to `ci`;
- if the deploy key is absent it is generated once, and its public half is printed with the exact
  GitHub instructions; and
- the local remote commands that make the host `origin` and GitHub `github` are printed for the owner
  to run deliberately.

`cloud-init.yaml` grows the same `ci` user for a future host: no sudo, `git-shell`, the same two
keys, subuid/subgid, lingering, and the `git` and `reuse` packages. The rest is `ci-install`, because
the SDK version belongs to `global.json` and the repository clone belongs to a GitHub that exists.

### 8. The remote switch

The owner runs, once, in the main worktree:

```bash
git remote rename origin github
git remote add origin ci@moosemorals.com:repo.git
git fetch origin
git branch --set-upstream-to=origin/trunk trunk
```

From then on `git push` is a push to the host, and the host's `post-receive` starts the check and the
mirror. `git push github trunk` remains possible as the deliberate exception when the host is
unreachable; it is ungated, and the status file does not move. `make deploy`'s closing note compares
against `@{u}`, which is now `origin/trunk` on the host, so it still says when the commit it deployed
is not on the upstream.

The host's bare repository was cloned from GitHub, so `origin/page-inbox` and every other existing
ref are present before the first push. Run `ci-install` and make the switch in the same sitting: a
commit pushed to GitHub after the clone and before the switch would be rewritten by the first
mirror. Once the switch is made, the only refs the first pruned mirror removes are ones the host
deliberately deleted.

## Configuration and values

| Value | Where it lives | Why it is there |
| --- | --- | --- |
| CPU 200%, memory 4 GiB, CPU weight 50, I/O weight 50 | `deploy/ci/uk.osric.asp-ci.slice` | Deploy configuration is the source of truth for the host, like the quadlets |
| Mirror every 15 minutes | `deploy/ci/uk.osric.asp-mirror.timer` | The hook is the prompt; the timer is the retry |
| `/ci/status.json`, `/var/lib/uk.osric.asp-ci` and `/srv/ci-status` | `ShellCatalogue`, `deploy/caddy.container` and the Caddyfile | The key is in the shell's one table; the directory and its mount are deploy configuration |
| `ci`, `/home/ci`, `/var/lib/uk.osric.asp-ci` | `deploy/ci` scripts, `ci-install`, cloud-init | One account and one directory; a name in three places is still one invariant |
| SDK version | `global.json`, read by `ci-install` | The host builds the tree's SDK, not the distro's |
| Cache and worktree | `check-push` | `make ci` cleans; the worktree is removed; caches are the only growth |

## Failure handling

| What fails | What it looks like |
| --- | --- |
| `make ci` fails | `/ci/status.json` says `failed` with the commit, the times and the target; `journalctl` has the output; trunk stays as pushed and the next push checks the fix |
| A check unit dies | `systemd-run --collect` removes it; the journal has the failure; the status file stays `running` for that commit; the next trunk push starts a new run, and the owner can run `check-push <sha>` by hand |
| Two pushes arrive close together | The first check skips its result if the tip moved; the second waits on the lock and checks the newest tip |
| The mirror cannot authenticate | `post-receive` prints the warning; the timer retries every 15 minutes; the key is the only thing to fix, and the status file still describes the check |
| The host is unreachable | The push fails; the local clone still has the commits; `git push github trunk` is the deliberate, ungated exception |
| The bare repository is lost | GitHub has the last mirrored refs and the owner's clone has the rest; `ci-install` re-clones GitHub and the owner pushes the missing commits |
| The status file is missing | Caddy answers 404 until `ci-install` or `make deploy` seeds it; the check still runs and writes the file |
| The slice is too tight | `make ci` fails with an OOM or a timeout; raise the slice, not the app's cgroup, and record the measurement in the plan |
| A page or a build saturates the host | The app keeps its 3 vCPU / 2 GiB cgroup; the CI is at `CPUWeight=50` and `Nice=10`; the health check is unchanged |

## Security and isolation

- `ci` has no sudo, no login shell and no access to `deploy`'s `.config`, `.ssh`, volumes or the
  assistant's environment file. `ci-install` checks that `/home/deploy` is not readable by `ci`.
- The build runs code from a push. Only the owner can push, and the account that can push has the
  deploy key and no other secret. A compromised dependency in a pushed tree can reach this
  repository's GitHub write key; it cannot reach the app's storage, the data-protection keys or the
  assistant's provider key.
- The status file is public but carries no secret: a commit, two timestamps, a result and one line.
  The full log stays in the host's journal.
- Caddy mounts the status directory read-only, and the app has no route or mount for it. A page or
  the assistant cannot write the status file; `ci` can write only `/var/lib/uk.osric.asp-ci`.
- The mirror is a pruned, forced copy of heads and tags. There is no branch protection on GitHub,
  because protection on the copy would make the host's mirror fail and would give the copy an
  authority the decision gives the host.

## Steps

One branch, `plan/host-ci`, because the pieces are one account and one push path; each numbered commit
is a coherent checkpoint and leaves the gate green. The owner may ask for any of them as a separate
step.

1. **The status route.** Add the `ShellCatalogue` reserved entry, the read-only mount in the Caddy
   container, the Caddy route and its `no-store` header, the `make deploy` seed, and the checks that
   prove Caddy serves the file with the app stopped.
2. **The account and the repository.** Add `ci-install` with the user, packages, SDK, bare clone,
   directory and seed; add the `ci` user to cloud-init; install the scripts and units as they arrive.
3. **The check.** Add `pre-receive`, `post-receive`, `check-push` and the slice; install them; prove a
   manual check writes `running` and then `passed` or `failed`, and that a superseded run writes
   nothing.
4. **The mirror.** Add the deploy-key setup, the `mirror` script, the service and the timer; prove a
   scratch branch appears and then disappears on GitHub.
5. **The record and the remotes.** Switch the local remotes, update the docs and decisions, close
   `ops-no-ci` at the merge, and unblock `ship-the-tested-image` in the same commit.

## Tests

There is no test harness for shell in this repository and adding one would put podman in the gate, so
the scripts are checked where they run, as the backup scripts were:

- `bash -n` on every new script.
- A local harness: a throwaway bare repository with the two hooks and a stub `systemd-run` that records
  its arguments. A trunk push starts one check for the new SHA; a branch push starts only the mirror;
  a deletion of trunk is refused. The status writer is run with a stub `make` that exits 0 and one
  that exits 1, and the JSON is read back for both.
- On the host: `make ci-install` twice, with the second changing nothing; a good commit's
  `/ci/status.json` goes `running` → `passed`; a deliberately broken commit checked by hand goes
  `running` → `failed` with the failing target; `journalctl` has the full log.
- The mirror: a scratch branch pushed to the host appears on GitHub; deleting it on the host and
  running the mirror removes it on GitHub.
- Caddy: `caddy validate` on the Caddyfile; with the app stopped, `curl` through Caddy answers
  `/ci/status.json` with `application/json` and `Cache-Control: no-store`; a fresh deploy's
  `never-run` seed is the first body. The shell's catalogue tests keep the key reserved and described.
- `make check`, `make decisions`, `make backlog` and `make license-check` on the branch.

## Documentation

- [deploy/README.md](../../../deploy/README.md): a "CI on the host" section with `ci-install`, the status
  URL, the journal command, the deploy key, the mirror and the remote switch; "Day-to-day" gains the
  push → status → deploy order.
- [deploy/operations.md](../../../deploy/operations.md): the slice, the journal, the disk and what to do
  when a check is red.
- [CONTRIBUTING.md](../../../CONTRIBUTING.md): pushing `trunk` to the host is what runs the gate; GitHub
  is a mirror; the status file is where the answer is.
- [README.md](../../../README.md): `ci-install` in the task-runner table and the host's `git`/`reuse`
  dependency beside the existing row.
- [.github/copilot-instructions.md](../../../.github/copilot-instructions.md): the deployment paragraph
  says the host runs `make ci` after a trunk push and where the status lives.
- [docs/decisions.md](../../decisions.md): the three new entries and the three edits named above.
- [`ops-no-ci`](../../backlog/closed.md): planned here, closed at this merge; the manual `/help` does
  not change because no page author's surface moves.

## Files

| File | Change |
| --- | --- |
| `deploy/ci/pre-receive` | New: refuse a trunk deletion |
| `deploy/ci/post-receive` | New: start the check for trunk and the mirror for every ref |
| `deploy/ci/check-push` | New: lock, worktree, `make ci`, status write |
| `deploy/ci/mirror` | New: pruned heads and tags to GitHub |
| `deploy/ci/install-host` | New: the root-side account, SDK, repository, key and unit installation |
| `deploy/ci/status-never-run.json` | New: the `never-run` body `make deploy` and `ci-install` seed |
| `deploy/ci/uk.osric.asp-ci.slice` | New: the 2 vCPU / 4 GiB cap |
| `deploy/ci/uk.osric.asp-mirror.service`, `.timer` | New: the mirror's oneshot and its fifteen-minute clock |
| `deploy/cloud-init.yaml` | `ci` user, `git` and `reuse`, subuid/subgid and lingering for the new host |
| `deploy/caddy.container` | The read-only status directory mount |
| `deploy/Caddyfile` | The `/ci/status.json` route and its `Cache-Control: no-store` |
| `Makefile` | `ci-install`; the `ci` comment no longer says there is no service; the `make deploy` seed |
| `src/uk.osric.asp.Web/Shell/ShellCatalogue.cs` | The reserved `/ci/status.json` key, described as Caddy's |
| `REUSE.toml` | The JSON seed's annotation, because JSON cannot carry a comment |
| `docs/plans/host-ci.md` | This plan |
| `docs/backlog/ops-no-ci.md`, `docs/backlog/ship-the-tested-image.md`, `docs/backlog/README.md` | `ops-no-ci` planned now and closed at the merge; the release half recorded and blocked by it |
| `docs/decisions.md` | Three new entries and three edits |
| `deploy/README.md`, `deploy/operations.md`, `CONTRIBUTING.md`, `README.md`, `.github/copilot-instructions.md` | The setup, the push order, the status and the mirror |

## Risks

| Risk | Answer |
| --- | --- |
| A red trunk is found late because the check is asynchronous | The status file is on the site and the push prints its URL. The owner checks it after a push. Pushing the result to the owner is a separate item and is not pretended here |
| The build starves the app | The slice is 2 vCPU / 4 GiB with low weights; the app keeps its own 3 vCPU / 2 GiB cgroup. The first run measures `systemd-cgtop` and `podman stats`, and the slice is adjusted if the container escapes it |
| The host's only copy is lost before the mirror | The mirror runs on receive, not after the check; the owner's clone is a third copy; `ci-install` re-clones GitHub |
| A compromised host rewrites GitHub | The deploy key can write this repository and nothing else. A host that can serve a page can already serve a lie; the mirror is a copy, not a trust boundary |
| The first pruned mirror deletes a GitHub branch | The bare repository is cloned from GitHub before the first push, so every existing branch is a source ref. `ci-install` and the remote switch are made in one sitting, so no GitHub-only commit sits between the clone and the first mirror |
| `make ci` cannot finish in a reasonable time on 4 vCPU | The push does not wait; the status says `running`. The first run's duration is a measurement, and the slice and the timer are the two values that change if it is wrong |
| The status file is cached or stale | Caddy adds `no-store`, the write is an atomic rename, and the file carries the commit it describes |
| Caddy cannot read the status directory | The directory is `0755` and the file `0644`, both owned by `ci`, and the Caddy container mounts the directory read-only. `make deploy` seeds it and `ci-install` owns it, so a missing file is a 404, not a broken site |
| The status route shadows a page | The key is in the shell's route table as a reserved record, so no page can be stored there; Caddy's `handle` group answers the path and the proxy handles the rest |
| The account's keys are the deploy user's | SSH is forced to `git-shell`; the account has no sudo and no shell. A dedicated key can replace them without changing the design |
| The mirror is a force-push to a protected branch | There is no protection on the mirror. The gate and the authority are on the host; a protected copy would fail the mirror and change which copy is the source |

## Considered and rejected

- **GitHub Actions.** The original proposal. There are no pull requests here, a hosted runner would
  gate nothing, and the host is already the deployment target. The item records this as superseded.
- **A blocking `pre-receive`.** It would make "trunk is verified" true at the door, but every push
  would wait for the whole suite and the image proof, and a rejected push would have no branch to
  review. The owner chose asynchronous.
- **A second CI VM.** More infrastructure, another host to patch and another backup. The slice gives
  the app its memory and CPU back on the existing box.
- **A container-in-container build.** The SDK's container targets need podman; nesting it adds a
  socket, a privileged path or a second daemon for a build that already runs rootless.
- **`git push --mirror` from the local checkout.** It carries `refs/remotes`, so the host would end up
  with a remote-tracking namespace and GitHub would receive it. The bare clone plus explicit heads
  and tags is the mirror that was meant.
- **Serving the status from the app's web root.** It would put the status behind the app, so it would
  be unavailable when the app is down, and it would add an app mount and a fixture for a file the app
  does not need. Caddy answers it from a read-only mount; the key is still in the shell's one table as
  a reserved record, so a page cannot shadow it.
- **Writing the status file in place.** A reader can see a half-written JSON document. The temporary
  file and the rename make the file's content a value, not a race.
- **A mirror that waits for the check.** A failed check would leave the commit only on the host, which
  is the machine that might be the one to fail. The mirror copies on receive; the status says whether
  it passed.
- **Gating a branch push.** A branch is a proposal; the status file is about `trunk`. Gating branches
  would multiply the machine's work for an answer nobody merges.

## What this does not close

- [`ops-no-outage-notification`](../../backlog/ops-no-outage-notification.md) is still the item that
  pushes a failure to the owner. A status file is a place to look, not a notification.
- The release promotion — build once, run the browser suite against the image, promote on command,
  and check the status and the running image id — is
  [`ship-the-tested-image`](../../backlog/ship-the-tested-image.md), not this step.
- The `deploy` target's host installation is
  [`makefile-holds-the-deploy-recipe`](../../backlog/makefile-holds-the-deploy-recipe.md); it was
  blocked by this step and is now open, with the status seed this branch added in the target.
- The live provider tests and the mutation targets stay out of the push gate.
- A visible CI panel on `/admin/status` is a page or shell change for later; the JSON file is the
  contract this step ships.
- A dedicated key for `ci` distinct from `deploy` is a rotation the owner can make; the forced shell
  and the absent sudo are what this plan relies on.

## Delivered when

`make ci-install HOST=deploy@…` has prepared the host and a second run changes nothing; a push to
`trunk` on the host is accepted and `/ci/status.json` — served by Caddy, not the app — goes `running`
→ `passed` for a good commit and `running` → `failed` for a deliberately broken one, with the failing
target named and the full output in the journal; it still answers with the app stopped; a branch and a
tag appear on GitHub and a branch deleted on the host disappears on the next mirror; the local `origin`
is the host and `github` is GitHub; `make deploy` still gates a release; and `make check`,
`make decisions`, `make backlog` and `make license-check` are green.

## What changed while building

- **Caddy's handle group.** The status route is a `handle @ciStatus` before a catch-all `handle`; the
  existing `/metrics` 404 moved inside the catch-all, because a top-level `respond` is ordered after
  `handle` and the proxy would otherwise swallow it. `caddy validate` is green.
- **The shell entry is a reservation, not a route.** The app has no mount and no endpoint; the
  catalogue entry keeps `/ci/status.json` out of the page store and tells the assistant whose path it
  is. `make check` is green with it.
- **The account setup is a script.** `install-host` runs as root and owns the packages, the account,
  the SDK, the bare clone, the deploy key, the hooks, the units and the seed. `ci-install` copies
  `deploy/ci` up and runs it; the Makefile only orchestrates. The ci user's subuid range is
  `200000-265535`, disjoint from `deploy`'s `100000-165535`. Only `deploy`'s unrestricted keys are
  copied: a line with a forced `command=`, such as the backup host's read-only `rrsync` key, is
  filtered out, and `git-shell` is the restriction on the account.
- **The scripts are testable without the host.** `check-push` and `mirror` take `CI_HOME`, `CI_REPO`
  and `CI_STATUS_DIR` overrides; the local harness pushed a real commit through both hooks, proved
  pre-receive refuses a trunk deletion, post-receive starts one check for trunk and only the mirror
  for a branch, `check-push` writes `running` → `passed`/`failed` and leaves a superseded commit
  alone, and the mirror pushes and prunes a branch.
- **The local gate.** `make check` passed: `backlog` 24 items in 23 rows with 200 citations,
  `decisions` 198 entries in 8 areas with 1,505 citations, `license-check` 1,081/1,081 files,
  `check-pages`, Core 890, Runtime 187, Web 2,062 and Integration 260 tests, and a Release publish.
  `cloud-init.yaml` parses as YAML.
- **What the checkout could not prove.** `make ci-install`, the first `make ci` on the host, the
  host's `podman`/cgroup containment, the Caddy status with the app stopped and the GitHub mirror
  need the server. The plan's §Tests and §Delivered when are the checklist for that run; the first
  pushed trunk is the real proof.
- **The merge wrote the record.** The three entries are in [the record](../../decisions.md);
  `code-one-gate`, `deploy-quadlets-are-the-source-of-truth` and `deploy-one-small-host` were edited
  in place; `ops-no-ci` is in [closed](../../backlog/closed.md), and the two follow-ons are open.

## The names this plan proposed

- [deploy-ci-is-its-own-account](../../decisions.md#deploy-ci-is-its-own-account) — the gate runs as
  `ci`, not as `deploy`: no sudo, a bare repository, a 2 vCPU / 4 GiB slice and the only GitHub
  credential on the host.
- [deploy-trunk-is-checked-after-push](../../decisions.md#deploy-trunk-is-checked-after-push) —
  the host accepts a trunk push and runs `make ci` asynchronously; the status file is the result, the
  journal is the log, and a red trunk is fixed forward rather than refused at the door.
- [deploy-github-mirrors-the-host](../../decisions.md#deploy-github-mirrors-the-host) — the host is the
  upstream; GitHub is a one-way, pruned copy of its branches and tags, written with a deploy key that
  can do nothing else.
