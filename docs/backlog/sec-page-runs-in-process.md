<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page code has no process boundary

| | |
| --- | --- |
| Difficulty | cornered-porcupine |
| Utility | high |
| Severity | Medium |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Security, runtime |
| Location | `src/uk.osric.asp.Web/Pages/PageRenderer.cs`, `src/uk.osric.asp.Runtime/LoadedPage.cs`, `src/uk.osric.asp.Web/Pages/PageCapabilities.cs` |
| Spike | [Per-page NativeAOT binaries](../spikes/aot-page-binary-spike.md) |

## Problem

The page API policy, the trimmed reference set and `PageLoadContext` keep page code away from the
site's own types, and the editor is behind authentication. What remains is the
fact that a page is still arbitrary C# running in the site's process, with the site's identity:

- No way to reclaim a thread. The execution budget bounds the response, but `while (true) { }` pins a
  thread until the supervisor restarts the process, and that restart is a whole-site outage.
- No memory limit of its own. The container limit protects the host, not the site: one page can still
  allocate until the container is killed and take every other request with it.
- `Environment.Exit` and `Environment.FailFast` stop the site's process, and the restart is the
  remedy.
- Shared state is mutable: a page can change `CultureInfo.DefaultThreadCurrentCulture`,
  `Console.Out`, `Environment.CurrentDirectory`, or start a background thread that outlives the
  request and survives `Unload`.
- Anything reachable through the base class library is reachable: the policy is a static check, and
  the load context only refuses the shell's assemblies.

None of this is a surprise: the threat model is "avoid footguns", and the mitigations above stop the
obvious ones. Each bullet is the same fact — a page is code with the site's authority — and the only
fix that is not a fence is a different process. This issue exists so the residual risk is written down
rather than assumed away.

## The shape it would take

The unit worth isolating is **one author's code** — not one page, one render or one visitor. A page
that embeds another page, or a layout that wraps one, is the same author's code on both sides: putting
both in one unit is no different from putting two methods of one class in one process, and the author
already controls both ends. The boundary is between an author and the site, and between two authors.

Keying the unit to the author fixes its lifetime, and the lifetime is what makes the work affordable. A
unit per *render* has to start fast, which is what demanded NativeAOT and its 11.2 s build. A unit per
*author* outlives the request and starts once, so an image is still compiled at save and loaded into an
`AssemblyLoadContext` exactly as it is today. The AOT pipeline is not a prerequisite for this item;
[the spike's](../spikes/aot-page-binary-spike.md) numbers stand for the shape they measured — a process
per render — and not for this one.

The protocol is the SDK. `IContext` and its capabilities are already plain values, `Task`-returning,
free of ASP.NET types and cancellation tokens, with `IPageCapabilities` as a per-render grant, so
moving a page out of the process means implementing those interfaces once more over a socket:

- one persistent, length-prefixed connection per unit, with renders multiplexed by id, because one
  unit serves concurrent requests;
- host to unit: one `render` frame — `PageRequest`, path, user, grant set and deadline;
- unit to host: capability calls by id, body and response mutations, one-way logs, the final status;
- host to unit: `cancel`, which completes `ctx.Aborted`; killing the unit is the escalation when a
  render ignores it.

The host binds path, session and user to the render, so no call names them, and the dispatcher refuses
a call to a capability that render was not offered — the rule `PageCapabilities` applies by absence
today. The budget keeps its edges: the host counts the body against `MaxBodyChars` and starts the
`Pages:Stream` clocks at a `commit` frame. The store never leaves the shell: the unit has no network
device, no filesystem holding the database and no connection string, and reaches only its page's own
values through `IPageStorage`, `IPageSession`, `IPageCatalog` and `IPageWrites`.

Framing belongs behind `Stream`, so the same protocol runs over a UNIX socket or `AF_VSOCK` unchanged
and the isolation mechanism stays a separate choice.

What it buys:

- `while (true) { }` is reclaimed by killing the unit — the one thing the execution budget cannot do.
- `Environment.Exit`, unbounded allocation and CPU become the unit's cgroup, so the site survives them.
- A wedged render restarts a unit instead of the site: `pages-health-and-restarts` keeps its supervisor
  and its probe, and the blast radius falls from the whole site to one author's unit.
- Fetch credentials, the connection string and the shell's types are not in the page's address space,
  so reflection cannot reach them either.

What it does not do:

- It is not a request boundary. Statics and process-wide state — `Console.Out`, `CultureInfo`, the
  current directory — live for the unit's lifetime, so one page can still reach another page of the same
  author. That is not a trust violation, because the author can already write that code into either
  page, but it is not a per-render reset either.
- It does not split composition. An `EmbedAsync` or a `Wrap` costs another render but no second unit
  while both sides are one author; a cross-author composition, most likely a layout, is when a second
  unit appears.

## Not in scope

There is no author on a page. [`PageHead`](../../src/uk.osric.asp.Web/Pages/PageHead.cs) is a path, a
revision, a tombstone and two hashes, and the only identities in the system are the editor's local
account and the visitor. `IPageWrites` says "any page the visitor may edit", which is a per-visitor
rule, not a per-author one. One unit per author therefore needs a page to have an owner, the editor's
authorization to key off it and `IPageWrites` to mean what that author owns — a data-model change larger
than the isolation work and not part of it. Until there is a second author, the unit has one member.

## Considered and rejected

- **A microVM as the unit.** Firecracker's attraction was snapshot restore: a clean memory image per
  render, which a container cannot reset. Keyed to an author and outliving the request, this item wants
  no per-render image, and the branch is closed anyway — `/dev/kvm` is not exposed on the CAX21, and
  gVisor would be a dependency this project does not want.
- **A process per render.** Affordable only with NativeAOT, and an edited page builds in 11.2 s against
  H5's 2 s bar. Withdrawn because the lifetime above no longer needs it, not because the measurement
  moved.
- **A .NET sandbox.** `AppDomain` isolation and CAS were removed, and a collectible `AssemblyLoadContext`
  is a lifetime mechanism, not a permission one. Nothing is attempted here.

## Status

The execution budget and the container limits are done by the
[execution budget](../plans/archive/page-execution-budget.md): a render has a deadline, a slot count, a
body limit and a compile budget, and the container carries `--memory`, `--pids-limit` and `--cpus`. What a
budget cannot do is reclaim a thread, so the safety net is the supervisor: `/health` reports abandoned
renders, podman probes it, and `HealthOnFailure=restart` turns a wedged process into a ~35 s restart
instead of a permanent outage.

What remains is the boundary itself — a long-lived unit per author, reached over a socket — and it is
not built, because page authors are still only the owner. There is no plan; the numbers to start from
are in [the spike record](../spikes/aot-page-binary-spike.md).
