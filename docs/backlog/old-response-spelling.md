<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The old response spelling is still on `IContext`

| | |
| --- | --- |
| Difficulty | contented-capybara |
| Utility | low |
| Status | open |
| Blocked by | — |
| Plan | [page-response](../plans/archive/page-response.md) |
| Area | Sdk, docs |
| Location | `src/uk.osric.asp.Sdk/IContext.cs`, `src/uk.osric.asp.Sdk/PageResponseDeprecation.cs`, `docs/architecture.md` |

## Idea

[IDEA-048](closed.md) moved the ten response members to `IPageResponse` at `IContext.Response` and left
the old spelling in place for one release as `[Obsolete]` members under `PAGE009`, because a change to
`Sdk` invalidates every stored page image and a warning lets an unedited page keep compiling. That was
the same shape `PAGE005` used for `ctx.Features`, and it was retired a release later by IDEA-026.

This is that retirement: delete the ten obsolete members and their constant from `IContext`, retire the
`PAGE009` row in the diagnostic table, and let the page contract be ten members and eleven with
`Response`. Nothing else moves — `IPageResponse`, `SimpleResponse` and the seeded pages already say the
new spelling.

## Sketch

1. Delete the ten `[Obsolete]` default interface members from `IContext` and the internal
   `PageResponseDeprecation` constant with them.
2. Retire `PAGE009` in `docs/architecture.md` beside `PAGE005`, using the same words: a code is never
   reused for a different meaning.
3. Add the check that nothing came back: a page that says `ctx.Write` no longer compiles, and the
   author manual and the prompt describe the contract as `IContext` plus `IPageResponse`.
4. Take stock of what is left in the store. A stored page the owner has not edited since the alias went
   in will fail to compile the moment this lands — which is the point of the shim expiring, and is why
   this is a job rather than a line in the other one. Read the store's warnings before doing it, and
   only do it when nothing reports `PAGE009` any more.

## Not in scope

- Any change to what a page can do, or to `IPageResponse`. This is a deletion.
- A second alias for a different rename. One release is the rule.
