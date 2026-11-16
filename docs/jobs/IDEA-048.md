<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-048 — A page writes into a response object

| | |
| --- | --- |
| Size | M — one `Sdk` interface, the `[Obsolete]` shims for one release, eight seeded pages and the manual, the assistant's prompt, the docs and the tests that pin the contract |
| Area | Sdk, Web (pages, seeds, assistant), docs |
| Location | `src/uk.osric.asp.Sdk/IContext.cs`, `src/uk.osric.asp.Sdk/IPageResponse.cs`, `src/uk.osric.asp.Web/Pages/SimpleContext.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs`, `docs/pages.md` |

> **Raised 2026-11-16.** Evaluated while [IDEA-022](../plans/simple-context.md) was being planned, because the two are
> the same split seen from opposite sides: 022 makes the response two collaborators inside `Web`, and
> this makes it one type in `Sdk`. [The 022 plan](../plans/simple-context.md) is the implementation this
> would expose; it is not a prerequisite for the shape, but doing this first would mean moving the
> members twice.

> **Planned 2026-11-16.** [The page response plan](../plans/page-response.md) is written and waits for
> the owner. It moves the ten response members to `IPageResponse` at `IContext.Response`, keeps the old
> names for one release as `PAGE009` warnings because `RuntimeId` invalidates every stored image, and
> migrates the seeds, `/help`, the prompt and the test templates in the same change. The shims' removal
> is a second job, written by that one.

## Idea

`IContext` is 20 members, and exactly half of them are the response:

| Today on `IContext` | What it is |
| --- | --- |
| `Write`, `WriteRaw`, `FlushAsync` | the body: escape, raw, and the commit |
| `SetStatus`, `SetContentType`, `SetHeader` | the settings that are applied at the end, or at the first flush |
| `AllowScripts`, `Redirect`, `RedirectToLogin` | the two response shapes and the sign-in detour |
| `AntiforgeryField` | the token for the form in the body |

The other ten — `Request`, `Path`, `EditorPath`, `User`, `Aborted`, `AssetUrl`, the three `Log`
members and `Capabilities` — are what a page reads or asks for. So the contract would be
`IContext.Response` returning an **`IPageResponse`**, and the twenty members become ten and ten.

Three things that buys, in order of how much they are worth:

1. **One name for one job, and the buffering rule becomes structural.** The commit rule is why
   `FlushAsync` carries its long remark today: the settings are collected and applied together, and the
   first flush is where they become final. A type called the response says that on its heading, and the
   remark becomes a sentence.
2. **A helper can take the output and nothing else.** A page can write
   `static void Card(IPageResponse output, Card card)` and get a seam that cannot read identity, spend a
   capability or see the request. Today it has to take `IContext` and is handed all of that. This is the
   only thing a page author gains; the rest is tidiness.
3. **It stops the context growing again.** Every future response-shaped member has an obvious home,
   beside the ones it has to agree with, instead of being appended to the twenty.

It is *not* a smaller surface in the sense of fewer things to learn: it is ten and ten, and
`ctx.Response.Write` is longer than `ctx.Write`. The gain is where the members sit, not how many there
are — and [`pages-capabilities-are-one-seam`](../decisions.md) is right that the response is not a
capability, because every page always has one.

## Sketch

**Members.** `Write`, `WriteRaw`, `FlushAsync`, `SetStatus`, `SetContentType`, `SetHeader`,
`AllowScripts`, `Redirect`, `RedirectToLogin` and `AntiforgeryField` move. `Request`, `Path`,
`EditorPath`, `User`, `Aborted`, `AssetUrl`, `Log`, `LogWarning`, `LogError` and `Capabilities` stay.

Three of those placements are judgement calls, and the line they draw is **the response is what
changes the response; a fact about the site stays on the context**:

- `AntiforgeryField` is the response: it is the token for the form in the body, and it is already
  minted under the body's gate, so the response object is where that gate lives after IDEA-022.
- `RedirectToLogin` is the response: it is a redirect. The guard still reads `ctx.User` on the context.
- `AssetUrl` is **not** the response: it answers "is this link a live asset?" about the site, not about
  this response, and `ctx.Response.AssetUrl` reads wrong.

`IsCommitted` is internal to the shell today (the endpoint asks it before wrapping). It should stay off
the contract: a page knows whether it flushed.

**Names.** `IPageResponse` (the thing a page writes into) beside `PageResponse` (the value it produces)
is the pairing; the implementation is `SimpleResponse` in `Web`, beside `SimpleContext`, built over the
two collaborators [IDEA-022](../plans/simple-context.md) creates.

**Migration is the expensive half, and `PAGE005` is the template.** `RuntimeId` includes the `Sdk`
module's version id, so the commit that adds this invalidates every stored image and recompiles every
page on its next render. The archived
[capability naming](../plans/archive/capability-naming.md) decision did exactly this once: the old
members stay as `[Obsolete(Deprecated, DiagnosticId = "PAGE009")]` default interface members that
delegate to `Response`, warnings rather than errors, so a stored page keeps compiling while it is
edited. `PAGE005` is retired and never reused, and the next free code is `PAGE009`.

The same change moves the seeded pages (112 response-member uses across eight files, 44 of them in
`/help`), the assistant's prompt (47 `ctx.` mentions), `docs/pages.md` and the two `IContext`
implementations in the tree (`SimpleContext` and the compiler tests' `TestContext`). The shims are
deleted a release later, by a job of their own, exactly as `IPageFeatures` was.

**Rejected, deliberately:** `IContext : IPageResponse` with `Response => this`. It breaks nothing, gives
the narrow type immediately and needs no shims — but the members never leave `IContext`, so the
surface shrinks on paper only, and two names for one thing become permanent.

## Not in scope

- **Logging.** Three members, and the capability decision put them on the context on purpose; a
  `Log(LogLevel, …)` would need a Microsoft type in the page API, which the policy refuses.
- **A narrower response for a layout.** A layout is itself a stored page — the seeded `/_layout` — that
  the host renders *around* another page once that page's code has run, with the result in
  `IPageLayout.Body` ([shell](../shell.md#layouts)). Because the content owns the response, a layout's
  `SetStatus`, `SetContentType`, `SetHeader` and `Redirect` are ignored and logged today, and only
  `AllowScripts` is shared with it. This idea keeps that rule: it does not hand a layout a smaller
  `IPageResponse` — write and scripts, no status or redirect — which would make the no-op something
  the compiler enforces. The generated handler takes `IContext` for every page kind, so narrowing it
  changes what a layout *is* and wants its own decision.
- **Turning the response into a capability.** It is contract, not something a render offers.
