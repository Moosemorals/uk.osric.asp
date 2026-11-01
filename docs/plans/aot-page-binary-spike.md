<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spike: per-page NativeAOT binaries

Measured on 2026-11-01 on the dev box (4 cores) and on the live host (`moosemorals.com`: 1 vCPU AMD
EPYC-Genoa, 939 MB RAM, no swap, Debian 13, podman 5.4.2 rootless).

The idea: instead of rendering in the site's process — the design recorded in
[page execution budget](page-execution-budget.md) — compile each page at save time into its own
NativeAOT executable on disk, named by a hash of the content and the runtime id, that reads one JSON
request on stdin and writes one JSON reply on stdout. The page is linked into the program, so nothing
is loaded at runtime, the shell's assemblies are not in the binary at all, and every render is a
process.

**Verdict: the render half is better than expected; the build half fails by 7.5×, and the per-render
sandbox is blocked by the deployed hardening.** With the process boundary deprioritised anyway
(SEC-03), there is nothing here to justify the build pipeline. This record exists so the idea is not
re-litigated from estimates.

## Hypotheses

| # | Hypothesis | Threshold | Result |
| --- | --- | --- | --- |
| H1 | AOT render fast enough to spawn per request | p95 ≤ 25 ms | **pass** — 3.5 ms median, 5.1 ms at 10-way concurrency |
| H2 | AOT binary small enough per version | ≤ 10 MB | **pass** — 2.96 MB (static musl) |
| H3 | AOT render small in memory | peak RSS ≤ 16 MB | **pass** — 3.4 MB |
| H4 | `bwrap` affordable per render | added ≤ 10 ms | **pass on cost (+5–7 ms), fail on availability** |
| H5 | Publishing on save affordable | wall ≤ 2 s | **fail by 7.5×** — 15.0 s per edited page |
| H6 | Publishing fits beside Kestrel | 400 MB peak, headroom left | **marginal** — 250 MB used, host dropped to 52 MB available, no swap |
| H7 | No blocking runtime surprises | zero | **fail** — bwrap needs capabilities the quadlet drops |

## Method

A throwaway workspace on each machine, since deleted:

- `Core/` — a copy of `src/uk.osric.asp.Core/*.cs` with a local csproj, so the closure is the real
  one and the repo is untouched.
- `driver/` — an exe referencing `Core`: captures `Console.OpenStandardOutput()` first, then
  `Console.SetOut(TextWriter.Null)`; takes one `PageRequest` from stdin; runs the page; writes one
  reply to stdout; prints peak RSS from `/proc/self/status` to stderr. `SpikeContext : IContext`
  buffers the body and records headers and logs, with `Aborted` as `CancellationToken.None`.
  `HelloPage : IHandler` writes ~2 KB of HTML with a LINQ loop and one log line. `SpikeJson` is
  `JsonSerializerContext` source generation, camelCase, so the closure is what the real design would
  link.
- `Mini/` + `MiniDriver/` — the same driver against only the render-side types (`IContext`,
  `PageRequest`, `PageResponse`, `IHandler`, `UserFacingException`), to test whether a smaller
  closure builds faster. It does not.

Two mistakes worth recording, because each cost a run: **never measure latency while a publish is
running** (the first IL numbers read 125/160 ms because of exactly that), and **busybox `date` has no
`%N`**, so host timing has to be done with GNU date outside the container, subtracting the
container's measured start cost.

## Dev box (4 cores)

| Variant | median | p95 | RSS | binary |
| --- | --- | --- | --- | --- |
| IL apphost | 137 ms | 151 ms | 34.9 MB | ~200 KB + framework |
| IL via muxer (`dotnet <dll>`) | 137 ms | 152 ms | 34.8 MB | — |
| IL ReadyToRun | 128 ms | 151 ms | — | — |
| IL + `DOTNET_EnableDiagnostics=0` | 139 ms | 188 ms | — | — |
| IL + no tiered compilation | 210 ms | 227 ms | — | — |
| **NativeAOT** | **5.9 ms** | 9.1 ms | **5.4 MB** | 2.9 MB |
| AOT + bwrap (`--unshare-net --unshare-pid`) | 11.1 ms | 14.1 ms | — | — |
| AOT + bwrap (`--unshare-all`, ro-binds, tmpfs) | 12.8 ms | 16.3 ms | — | — |

The earlier estimate of 30–45 ms for an IL apphost was 3× optimistic, and every IL knob did nothing
or hurt. AOT is not an optimisation of the CGI shape; it is the only way it exists.

## Host (1 vCPU, 939 MB)

### Render

Measured inside one container, so the CPU is the same and container start is not in the loop.

| | sequential | 10 concurrent | RSS |
| --- | --- | --- | --- |
| IL apphost | 102 ms/render | 115 ms/render | 34.6 MB |
| **NativeAOT** | **3.54 ms/render** | **5.09 ms/render** | **3.4 MB** |

The live app answers `/` on the loopback in **p50 2.4 ms, p95 3.4 ms**. Per-render AOT therefore
costs about 1.5× today's render at 1/75th of the memory, with a process boundary. On render
properties alone this architecture wins, and it would remove the load context, the assembly cache and
the shared-state window with it.

### The sandbox is blocked by the deployed hardening

| Setup | Result |
| --- | --- |
| `bwrap --unshare-all --ro-bind /box /box --dev /dev` in a default rootless podman container | **OK** — renders with only its own directory visible, no `/proc`, no `/usr` |
| Network inside that sandbox | `ip addr` shows `lo` only; `nc` to `1.1.1.1:53` has no route |
| `--proc /proc` inside bwrap | fails: `Can't mount proc: Operation not permitted` |
| `--unshare-net` without `--unshare-user` | fails: `Creating new namespace failed` |
| bwrap under `--cap-drop=all` (what the quadlet does) | fails: `setting up uid map: Operation not permitted` |
| … re-adding `SETUID`, `SETGID`, `CHOWN`, with and without `no-new-privileges` | still fails |

The tidy per-render sandbox — a 2.96 MB static binary in a namespace with no network and almost no
filesystem — works in principle and does not work inside the container as deployed. Getting it back
means either relaxing `DropCapability` on the app or giving the renderer its own container with its
own settings (the socket-and-second-unit shape from SEC-03's third fix). Per-render `podman run` is
not an option: container start measured **164 ms**, 46× the render.

### Build

| Step (musl, `linux-musl-x64`, static) | wall | note |
| --- | --- | --- |
| Cold publish (NuGet restore + first ILC) | 19.9 s | once per builder image |
| **Page edited, one string** | **15.8 s and 15.0 s** | the save-path number |
| Nothing changed (incremental) | 2.7 s | ILC's floor |
| Render-side-only closure (`Mini`) | 16.7 s cold, 15.0 s edited | **no better** — the cost is the runtime, not our code |
| Builder image (`sdk:10.0-alpine` + clang/build-base/zlib-dev) | built in 27 s | **1.38 GB** against the app's 148 MB |

- Minimum `MemAvailable` during the builds: **52 MB**, with no swap. The app was not restarted
  (`NRestarts=0`) and its `MemoryPeak` stayed at 253 MB, but the margin is about 50 MB: a builder has
  to get its own cgroup with its own limit or it will take Kestrel with it.
- Site latency while a build runs (loopback, 60 samples): p50 **2.4 → 5.4 ms**, p95 **3.4 → 13.1 ms**,
  max 16.8 ms. The site stays up; it is slower for the ~15 s.
- Every `Core` mvid or policy change invalidates every page: N pages × 15 s. Ten pages is 2.5 minutes
  of rebuild after each deploy, whether at boot or lazily.

### Fit with the deployment

- The app image is **Alpine**, and the static musl page binary runs there as uid 1654 straight from
  the `/data` volume: `podman exec uk.osric.asp /data/page-spike-test < req.json` answered `200`.
  No extra packages and no runtime dependency on the host.
- The `.dbg` file (5.4 MB) is produced even with `DebugType=none` and `IlcGenerateDebugInfo=false`;
  deleting it after publish leaves 2.96 MB per version.
- bwrap is not in the app image. Adding it means a derived base image (`FROM <app>; RUN apk add
  bubblewrap`), because the SDK container publish has no Dockerfile step.

## What this means

- **AOT plus CGI renders is viable, and better than the plan assumed.** 3.5 ms and 3.4 MB, a process
  boundary per request, and no pool to own. If the build were free this would be the design.
- **Saving a page is the cost.** 15 s here against tens of milliseconds today, plus a 15 s window
  where the site is about twice as slow. It only works as an asynchronous build with the previous
  binary still serving, which means per-page state (Ready, Building, Failed, Stale) and a rebuild at
  boot.
- **The box, not the design, is the blocker.** One vCPU and 939 MB with no swap leaves 52 MB of
  headroom for a 250 MB compiler while Kestrel runs.

## Re-check triggers

1. The host grows to 2+ vCPU and about 2 GB (a build near 8 s with comfortable headroom) — then AOT
   per page becomes the better architecture, with a builder quadlet and per-page build state.
2. A decision to build off-box: export the pages from the database and publish them in CI with the
   site, so the server never compiles and only ships 2.96 MB artifacts.
3. A .NET release that makes ILC materially faster, or a supported way to link only the page into a
   prebuilt native host, which would remove the per-page build entirely.
4. Pages becoming slow or numerous enough that in-process execution and its abandoned threads matter
   — AOT already solves that today.

## Reproducing

The workspace is disposable; rebuild it from this description. The commands that produced the numbers:

```bash
# Render: framework-dependent vs AOT, same container, loop timed from outside with GNU date.
dotnet publish -c Release -r linux-musl-x64 -p:PublishAot=true -p:StaticExecutable=true \
  -p:DebugType=none -o out-aot
podman run --rm -v "$PWD":/w -w /w/driver pagebuilder dotnet publish -c Release -r linux-musl-x64 ...
# IL baseline inside the same image (its build output is a musl apphost, so it runs there):
podman run --rm -v "$PWD":/w -w /w/driver pagebuilder sh -c 'dotnet build -c Release && for i in $(seq 20); do ./bin/Release/net10.0/PageSpike < req.json >/dev/null; done'

# The builder image used above (1.38 GB):
printf 'FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine\nRUN apk add --no-cache clang build-base zlib-dev\n' \
  | podman build -t pagebuilder -

# Sandbox, with and without the quadlet's hardening:
podman run --rm -v "$PWD/out-aot":/s:ro -i alpine sh -c \
  'apk add --no-cache bubblewrap; bwrap --unshare-all --ro-bind /s /box --dev /dev /box/PageSpike < /s/req.json'
podman run --rm --cap-drop=all ...            # fails: setting up uid map

# Site latency during a build, on the host:
for i in $(seq 60); do curl -s -o /dev/null -w "%{time_total}\n" http://127.0.0.1:8080/; done
```
