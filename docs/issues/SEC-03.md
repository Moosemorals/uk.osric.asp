<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# SEC-03 — Page code has no execution budget and no process boundary

| | |
| --- | --- |
| Severity | Medium |
| Area | Security, runtime |
| Location | `src/uk.osric.asp.Runtime/CompiledTemplate.cs`, `src/uk.osric.asp.Web/Endpoints/MainEndpoint.cs` |

## Problem

The page API policy, the trimmed reference set and `PageLoadContext` keep page code away from the
site's own types, and the editor is meant to be behind authentication (SEC-01). What remains is the
fact that a page is still arbitrary C# running in the site's process, with the site's identity:

- No execution budget: `while (true) { }` pins a thread, and a timeout can only abandon the thread,
  because `Thread.Abort` does not exist on .NET Core (see ARCH-03).
- No memory limit: one page can allocate until the container is killed.
- `Environment.Exit` and `Environment.FailFast` stop the process.
- Shared state is mutable: a page can change `CultureInfo.DefaultThreadCurrentCulture`,
  `Console.Out`, `Environment.CurrentDirectory`, or start a background thread that outlives the
  request and survives `Unload`.
- Anything reachable through the base class library is reachable: the policy is a static check, and
  the load context only refuses the shell's assemblies.

None of this is a surprise: the threat model is "avoid footguns", and the mitigations above stop the
obvious ones. This issue exists so the residual risk is written down rather than assumed away.

## Fix outline

1. Give the page a budget: run it on a worker with a bounded wait, return 504 on expiry, and treat
   the abandoned thread as lost. This is the same work as ARCH-03 and should be done once.
2. Cap the container instead of the page: `MemoryMax`, `CPUQuota`, `TasksMax` on the app quadlet, so
   a page can degrade the site but not the host. Cheap, and worth doing regardless.
3. If page authors are ever anyone but the owner: move execution to a worker process, started per
   request or pooled, with `--network=none`, a read-only root filesystem, its own user, and a hard
   `SIGKILL` on timeout. That is the only boundary that holds; everything in-process is a fence.
4. Do not attempt a .NET sandbox. `AppDomain` isolation and CAS were removed, and a collectible
   `AssemblyLoadContext` is a lifetime mechanism, not a permission one.
