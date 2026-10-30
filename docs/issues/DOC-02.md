<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# DOC-02 — `docs/architecture.md` says storage and the editor are not wired up

| | |
| --- | --- |
| Severity | Medium |
| Area | Documentation, architecture |
| Location | `docs/architecture.md:10-21,36-50` |

## Problem

The document has been partially updated (it now mentions EF Core, SQLite and `IStorage`) but still
says:

- "This is storage infrastructure only. It does not yet connect routes to stored scripts, persist
  editor changes, or expose storage to the script runtime or `SimpleContext`." (line 46)

All three are now false: `MainEndpoint` looks up the stored script by route, `EditorEndpoint`
persists changes, and `SimpleContext` does not expose storage (that one is intentional). The same
paragraph also predates the editor endpoint existing at all.

The "Functional core, imperative shell" section correctly concedes that `Core` is not pure, but the
concession is a description of drift rather than a target; once ARCH-01 splits the projects, this
section needs rewriting anyway. There is also stale guidance ("`class` only when the framework
demands it (controllers)") that refers to controllers that no longer exist.

## Fix outline

1. Rewrite "Source storage" to describe the current flow: request → route value → `IStorage` →
  `SqliteStorage` → EF Core; editor → `IStorage` write → redirect.
2. Remove the "does not yet" sentence and replace it with the actual boundary: `Core` never sees
   `IStorage`; the shell loads content and passes plain values (plus the `IContext` facade).
3. Once ARCH-01 lands, document the three projects and the dependency direction
  (`Web → Runtime → Core`, `Web → Core`).
4. Replace "controllers" in the shapes section with "endpoint handler classes".
5. Add a short "known gaps" section linking to `docs/issues/README.md` so the doc does not have to
   claim completeness.
