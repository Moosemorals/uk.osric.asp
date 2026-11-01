<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page execution budget

Implementation plan for ARCH-03. [SEC-03](../issues/SEC-03.md) — the process
boundary — is **not** part of this: pages keep running in the site's process, and the safety net for
the cases a budget cannot handle is the supervisor restarting an unhealthy container.

**Status:** implemented on 2026-11-01 — Phases 0 to 4; Phase 5 (the process boundary) is not built, by
decision, and SEC-03 records what that leaves.

An earlier draft proposed a supervised pool of render worker processes. It was dropped as more
machinery than the problem deserves, and the per-page-binary alternative was measured and rejected
for cost ([the spike](aot-page-binary-spike.md)). What is left is the smallest thing that bounds the
damage: a deadline, a concurrency limit, a log line, a metric, and a health check that tells podman
to restart the container when a page has stolen a thread for good.

## Threat model

The author is the only person who can write a page, so this is about the owner's own footgun landing
on a public request path, not about resisting an attacker. The budget's job is to make the *common*
mistake cheap and the *pathological* mistake time-bounded.

| Failure | Today | After this plan |
| --- | --- | --- |
| `while (true) { }` | pins a thread-pool thread forever; a few requests exhaust the pool; graceful shutdown hangs | request answers `504` at the deadline; the thread is lost and counted; the site keeps serving until the count trips the health check, then podman restarts it |
| A page that writes in a loop | the buffered body grows until the container is OOM-killed | the body cap stops it at 4 MiB and the page gets an exception |
| Unbounded allocation | the container grows until the OOM killer arrives | unchanged by the budget; the container memory limit makes the OOM kill the container rather than the host, and podman restarts it |
| `Environment.Exit` / `FailFast` | the site stops until someone notices | the container stops and podman restarts it; the site is down for a few seconds |
| A pathological template keeps Roslyn busy | a core is busy for the length of the request | compile gets its own slot and a cancellation token; a compile past the deadline is stored as a page error |
| Process-wide state mutated by a page | persists for the life of the site | unchanged — accepted |
| A *malicious* page | page API policy plus load context | unchanged — out of scope, recorded in SEC-03 |

The honest summary: this does not contain a page, it contains the *outage*. The worst case moves
from "the site is down until someone restarts it" to "the site is slow or down for the length of the
health interval plus a restart".

## Decisions

- **No process boundary.** One process, one thread pool, the page on the request path. SEC-03 stays
  open and its third fix (a worker with `--network=none` and its own user) is what changes if the
  author model ever changes.
- **A bounded wait, not a kill.** .NET cannot stop arbitrary C#; the wait bounds the *response*, and
  the abandoned thread is a known, counted loss. Nothing pretends otherwise.
- **The supervisor is the kill switch.** A page that never returns cannot be interrupted, so the
  process that owns it has to die. `/heath` reports how many renders have been abandoned, podman
  reports the container unhealthy, and the quadlet restarts it. That is the whole of the "hard
  boundary" in this design, and it is why the health phase is not optional.
- **The budget covers queueing and running.** A visitor's page answer arrives within the budget or it
  is a `504`; a queued render that never starts is a shed load, not a slow one.
- **The body stays buffered.** Which is what makes "answer `504`, send nothing" possible at all, and
  keeps the error page intact for a page that throws. Streaming is a separate issue (below).
- **The buffered body is capped.** Without it, one page can allocate until the container is
  OOM-killed, which turns a page bug into a restart.
- **`504`, not `503`.** The request was understood and the site failed to produce an answer in time.
  `503` stays for "the site is not ready", which this design has no state to report.

## Configuration

`Pages:Render` and `Pages:Compile`, with compiled-in defaults so a missing section cannot silently
disable the budget (the same rule as `Pages:ReservedPaths`):

| Key | Default | Meaning |
| --- | --- | --- |
| `Pages:Render:TimeoutSeconds` | `5` | wall clock for one render, from entering the semaphore to the response being shaped |
| `Pages:Render:Slots` | `2` | page renders allowed to run at once; queued requests wait without holding a thread |
| `Pages:Render:MaxBodyChars` | `4194304` | the buffered body; a page over this gets an exception |
| `Pages:Render:AbandonedLimit` | `2` | abandoned renders, alive at once, before the health check fails |
| `Pages:Compile:Slots` | `1` | concurrent compiles |
| `Pages:Compile:TimeoutSeconds` | `10` | Roslyn's budget for one compile |

The defaults are calibrated against the host: a page render there is **p50 2.4 ms, p95 3.4 ms**, so a
5 s budget is about 1500× p95 — it can only fire on something genuinely pathological, not on a slow
disk or a busy CPU.

---

## Phase 0 — supervisor safety net

Deploy-only, and it is the part that turns a hung process into a restart. It is also worth having
before any of the code lands.

### 0a — container limits

`deploy/uk.osric.asp.container` gains, in `[Container]`:

```ini
PodmanArgs=--memory=768m
PodmanArgs=--pids-limit=256
PodmanArgs=--cpus=1
StopTimeout=35
```

- `--memory=768m` is the one that matters: it makes the OOM killer take the container rather than the
  host (939 MB, no swap), and `Restart=always` brings it back. Podman flags rather than `[Service]
  MemoryMax=` because rootless podman puts the container's processes in a delegated cgroup, where a
  unit-level limit can miss them.
- `--pids-limit` stops a thread or process storm from taking the host with it.
- `--cpus=1` on a 1 vCPU host is close to a no-op today; it is there so the cap exists when the box
  grows.
- `StopTimeout=35` is longer than the host's 30 s graceful-shutdown budget, so a normal stop finishes
  cleanly and only a genuinely stuck process is `SIGKILL`ed.

### 0b — the health check that acts

Still in `[Container]`:

```ini
HealthCmd=wget -q -O /dev/null -T 3 http://127.0.0.1:8080/heath
HealthInterval=10s
HealthTimeout=5s
HealthRetries=3
HealthStartPeriod=30s
HealthOnFailure=restart
```

Verified on the host: `wget` is in the image (`/usr/bin/wget`, busybox), the probe answers from
inside the container, and podman 5.4.2 supports both `--health-on-failure` and the quadlet key
`HealthOnFailure`. `HealthStartPeriod` covers migrations and the first request.

Two consequences to write down in `deploy/README.md`:

- Detection plus restart is about **35 s** worst case (3 × 10 s interval, then a fresh container). A
  hung page is no longer an outage that needs a human.
- The unit's `StartLimitBurst=3` in 60 s still applies: a page that wedges deterministically will
  restart three times and then leave the unit failed, which is the existing "stop crash-looping"
  behaviour and is the right thing — but it needs `systemctl --user reset-failed` and a fix, so the
  README should say so.

The `/heath` path keeps its typo. Renaming it touches the reserved list, the editor's message and the
deploy docs, and buys nothing.

## Phase 1 — the render budget

### 1a — the body cap

`SimpleContext` takes a `maxBodyChars` and throws when `Write`/`WriteRaw` would exceed it, so a page
that writes in a loop fails quickly instead of growing the process.

### 1b — deadline and slots

`PageService.RenderAsync` gains a `SemaphoreSlim(options.Slots)` and one linked deadline:

1. Create a linked `CancellationTokenSource`, `CancelAfter(budget)`, and a `Stopwatch`.
2. `await _slots.WaitAsync(deadline.Token)`. A cancel here means either the client went away (the
   request token is cancelled — rethrow, nothing to answer) or the budget expired while queued
   (`RenderResult.TimedOut`).
3. Load and compile as today, then run the handler on `Task.Run`, and `await Task.WhenAny(page,
   Task.Delay(remaining))`. `WhenAny` rather than `WaitAsync`, because `WaitAsync` throws
   `TimeoutException`, which is indistinguishable from a page that throws one.
4. If the page wins, `await page` — a page exception surfaces exactly as it does today, because
   `CompiledTemplate.Invoke` already unwraps the reflection wrapper.
5. If the timer wins: `context.Abandon()`, count it, log once, return `RenderResult.TimedOut`.
   **Do not dispose the `CompiledTemplate`** — the page is still using it. Hand it to a continuation
   that disposes when the page eventually stops, and log any exception it then throws.

Releasing the slot on timeout is deliberate: the slot bounds *concurrent* execution, not the number
of lost threads. The lost threads are bounded by the health check instead — which is exactly the
distinction the next phase exists for.

Timings to record: `page.render.duration` keeps its instrument and gains a `timeout` outcome; add
`page.render.wait` (histogram, tag `page.path`) for the queue, so "slow because it waited" and "slow
because it ran" are distinguishable. `page.handler.invoke.duration` stays as it is.

### 1c — the abandoned context

`SimpleContext` gains `Abandon()`: after it, `Write`/`WriteRaw` do nothing. Otherwise an abandoned
page that keeps writing to the buffer races the endpoint reading it, and grows memory for as long as
the thread lives. A `volatile bool` is enough; a page is one thread.

### 1d — the endpoint

`MainEndpoint` switches on the new `RenderStatus.Timeout` and answers `504` with
`text/plain; charset=utf-8` and a body in the existing shape:
`"/slow is taking too long."` — the page's own status, headers and body are discarded.

### Tests

`Web.Tests`:

- A page that sleeps past the budget answers `TimedOut`, and the call returns within the budget plus a
  small margin (assert with a stopwatch, not a bare await).
- The abandoned page's later writes do not reach the response and do not keep growing the buffer.
- A page that throws before the deadline still surfaces its own exception type, not the timeout.
- With `Slots = 1`, a second render queues and is answered inside the budget; with a budget shorter
  than the first page, it times out while queued and never runs.
- A cancelled request token rethrows instead of reporting a timeout.
- A page that writes past `MaxBodyChars` fails with its own exception, and the endpoint answers `500`.

`Web.IntegrationTests`: a page saved through the editor that loops forever answers `504` and the next
request for a normal page answers `200`.

## Phase 2 — health

### 2a — `PageHealth`

A singleton counting abandoned renders: `Register()` (increment, log), `Count`. The count only ever
decreases if an abandoned page returns — which it may, so expose it as an observable up-down counter
(`page.handler.abandoned`) rather than a monotonic one. `Release()` on the continuation from 1b.

### 2b — the check

`AbandonedRenderHealthCheck : IHealthCheck` reports unhealthy when `PageHealth.Count >=
Pages:Render:AbandonedLimit`, with a description naming the count. Registered beside the storage
check in `Program`, so `/heath` fails as soon as the limit is reached and podman does the rest.

The check deliberately does **not** look at CPU or memory: those are the container's business, and a
health endpoint that guesses is worse than one that reports a fact.

### Tests

- With no abandoned renders, `/heath` answers `200` and reports `pages` healthy.
- With `AbandonedLimit` reached, `/heath` answers `503` and the report names `pages`.
- The count survives concurrent increments (parallel renders, one abandoned).
- The podman probe path works in the container: `wget -q -O /dev/null http://127.0.0.1:8080/heath`.

## Phase 3 — the compile budget

- `PageService.Compile` runs under a `SemaphoreSlim(CompileSlots)`.
- `ITemplateCompiler.Compile` gains a `CancellationToken`, forwarded to `ParseText`,
  `GetSemanticModel` and `Emit`. Roslyn honours it between operations, not inside one, so this is a
  coarse bound; say so in the comment next to it rather than implying more.
- A compile that trips the token becomes a stored `PAGE002` diagnostic — *"The page took too long to
  compile."* — the same shape as `PAGE001` from the API policy, so the editor shows it and the page is
  not recompiled on every request. A cancellation caused by the *request* (a save that the client
  abandoned) is rethrown instead, so it is never stored as a page error.

### Tests

- Two lazy compiles of the same page serialise behind the slot.
- A compiler that blocks past the budget stores `PAGE002` and serves it without recompiling.
- A cancelled save does not store a diagnostic.

## Phase 4 — documentation and bookkeeping

- `docs/architecture.md`: a "Page execution budget" section (deadline, slots, what is abandoned and
  what that costs, the body cap, `504`), and a "Health and restarts" section (what `/heath` reports,
  the podman health check, the ~35 s detection window, the start limit). Update the "Page contract"
  paragraph that currently says the deadline "lands with ARCH-03".
- `docs/testing.md`: the render tests and where the timing assertion lives, plus a note that the
  integration suite must not assert on restart behaviour (that is podman's job and not reachable from
  `WebApplicationFactory`).
- `docs/issues/README.md`: ARCH-03 moves to Closed with a sentence; SEC-03 stays open with an added
  note that the residual is now bounded by the health-and-restart safety net rather than unbounded,
  and that the spike record is where the out-of-process alternatives were priced.
- `deploy/README.md`: the health check, the restart-on-unhealthy behaviour, the ~35 s window, the
  start limit, and the container limits with the tuning knob (`podman stats`).

## Phase 5 — not built

The rest of SEC-03 stays unwritten: a renderer in its own container, or a process with `--network=none`
and its own user. When the author model changes, start at [the spike record](aot-page-binary-spike.md)
rather than at estimates.

## Sequencing

| Phase | Scope | Risk | Value |
| --- | --- | --- | --- |
| 0 | Container limits and health check | Low; deploy-only, watch one restart cycle | Turns any freeze into a restart |
| 1 | Deadline, slots, body cap, `504` | Medium; touches the hot path | The budget itself |
| 2 | `PageHealth` and the check | Low | Turns the lost thread into a restart |
| 3 | Compile slot and token | Low | Closes the last ARCH-03 bullet |
| 4 | Docs and issues | — | — |

Every phase ends with `make build && make test && make format-check`; phases that add files also need
`make license-check`.

## Streaming is a separate issue

Streaming is not part of the budget and should not ride along with it. The buffered body is what makes
"answer `504` and send nothing" and "a throwing page contributes nothing" true; a streamed response
has already committed its status, headers and first bytes, so those two guarantees die with the
buffer. If it is ever wanted, it wants its own issue, with this constraint recorded there: the first
write is a commit point, a page that never flushes keeps today's all-or-nothing behaviour, and a
timeout after the commit produces a truncated response rather than a `504` — which is exactly why the
restart-on-unhealthy safety net matters more once streaming exists.
