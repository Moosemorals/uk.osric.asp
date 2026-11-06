<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-010 - Serve retained revisions as immutable assets and use them in PageLayout

| | |
| --- | --- |
| Size | L |
| Area | Web, HTTP caching |
| Location | `src/uk.osric.asp.Web/Pages`, `src/uk.osric.asp.Web/Html/PageLayout.cs` |

## Idea

The shared layout links directly to `/site.css` and `/rocket.svg`. Revalidation still sends
requests. Retained revision hashes will let an asset URL select saved code independently of the
current page at that path. The endpoint can look up that revision, execute it and allow suitable
responses to be cached immutably; PageLayout can then link to those URLs.

## Sketch

1. Depend on the retained rows, stable revision hashes and unique hash index in the
   [editor tools and retained revisions plan](../plans/archive/editor-tools-and-immutable-assets.md).
   That plan builds storage and lookup, not asset serving or layout wiring.
2. Add something like `/asset/{hash}` which looks up the exact revision through the index and
   executes it through the normal bounded page runtime. Reuse its stored compiled assembly when
   the compilation runtime id matches the running host and the source fingerprint is valid; do
   not recompile a usable image. Never substitute the path's latest revision. Unknown or scrubbed
   hashes must not fall through to newer content.
3. Decide what to do when the stored compilation runtime id differs from the running host.
   Recompilation, compatibility and failure policy belong to this future work. Include cleanup
   of obsolete assemblies and debug symbols if they will no longer be used: permanent source
   history does not require permanent retention of unusable binaries. Decide whether cleanup
   happens at startup, lazily or as explicit maintenance. Preserve source, creation runtime id
   and revision hash when refreshing or clearing compilation data.
4. Resolve page path semantics explicitly: running `/asset/{hash}` may break code expecting its
   original path. Decide whether `ctx.Path`, `ctx.Request.Path` and relative-URL resolution refer
   to the original canonical page or the asset request, and test that contract. Also decide how
   query, method, headers and cookies affect execution. Do not silently rewrite only one path field.
5. Define which responses can promise `immutable`: the hash fixes source/runtime identity, not
   output that depends on requests, time or external state. Decide opt-in, public/private behavior,
   failure handling and GET/HEAD/304 semantics before granting a long freshness lifetime.
6. Decide whether tombstoning a page withdraws its historical asset URLs or merely its ordinary
   public URL. Scrub removes lookup data, but cannot revoke fresh browser/CDN copies; document that
   limit and the need to rotate exposed secrets and clean external copies separately.
7. Pass current asset URLs as plain values into PageLayout. Keep DB access and rendering out of
   the formatter, avoid recursion, and decide freshness after saves and fallback behavior for
   missing, deleted or broken assets. Opt existing edited templates in without overwriting them.
8. Test historical lookup/execution after later saves, matching-runtime image reuse without
   recompilation, runtime mismatch and obsolete-binary cleanup policy, path-sensitive
   pages, tombstones and scrub, new layout hashes after edits, and editor/error rendering when
   assets fail. Unversioned fallback URLs continue to revalidate, never claim immutable freshness.

This is separate from ARCH-06, ARCH-10 and ARCH-11; finishing those jobs does not imply the layout
has adopted immutable URLs.