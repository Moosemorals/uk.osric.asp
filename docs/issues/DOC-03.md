<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# DOC-03 — `deploy/README.md` repeats the stale wiring claim and the MVC trimming rationale

| | |
| --- | --- |
| Severity | Medium |
| Area | Documentation, deployment |
| Location | `deploy/README.md:69,139` |

## Problem

"Storage and migrations" says:

> This is source storage infrastructure only: route-to-script lookup, editor persistence and runtime
> storage access are not wired up yet. (line 69)

Route lookup and editor persistence *are* wired up. Runtime storage access is intentionally not
exposed, but the sentence conflates the three.

The image section says:

> Trimming would reach ~29 MB, but ASP.NET MVC does not support it: the trimmed image dies at startup
> with `TypeLoadException`. (line 139)

MVC is no longer used. Trimming is still a bad idea (the Roslyn compiler, reflection and
`AssemblyLoadContext` are all trim-hostile), but the stated reason is wrong, and a future reader may
conclude it is now safe.

## Fix outline

1. Replace the stale sentence with what storage is: the app looks up stored scripts by route and the
   editor writes them; pages cannot access storage through `IContext` (by design).
2. Rewrite the trimming paragraph: state that runtime compilation, reflection and dynamic loading
   are incompatible with trimming and that the framework-dependent Alpine image is the chosen
   trade-off.
3. Add the health endpoint (OPS-02) and the auth requirement (SEC-01) to the deployment notes when
   they exist.
4. Add a note that data-protection keys become persistent if authentication is added.
