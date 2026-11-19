<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page writes into its response

| | |
| --- | --- |
| Status | Merged 2026-11-16 — `pages-the-response-is-one-object` is in [the record](../../decisions.md) and its item is in [closed.md](../../backlog/closed.md) |
| Size | Medium — one `Sdk` interface with ten moved members and ten obsolete shims for one release, one new `Web` object over the two collaborators the split just made, the seven seeded pages and `/help`, the assistant's prompt and its version, the test templates that write pages as strings, and the docs. No store change, no migration, no configuration key, no new capability |
| Area | Sdk, Web (pages, seeds, assistant), docs |
| Spike | None. Every count below was taken off `trunk` on 2026-11-16, after the `SimpleContext` split merged |
| Backlog | [IDEA-048](../backlog/IDEA-048.md) is this step; the shims' removal is a separate item ([`idea-old-response-spelling`](../../backlog/idea-old-response-spelling.md)) written by this one |
| Decisions | New name proposed: **pages-the-response-is-one-object**. Edits `pages-capabilities-are-one-seam`. Relies on `pages-the-contract-is-the-seam`, `code-one-concern-one-collaborator`, `pages-the-response-is-buffered-until-a-flush`, `pages-page-io-is-async`, `pages-a-warning-never-stops-a-page` and `pages-what-a-diagnostic-is` |

## The short version

`IContext` is 20 members and exactly half of them are the response: `Write`, `WriteRaw`, `FlushAsync`,
`SetStatus`, `SetContentType`, `SetHeader`, `AllowScripts`, `Redirect`, `RedirectToLogin` and
`AntiforgeryField`. They move to a new `Sdk` interface, `IPageResponse`, reached as
`IContext.Response`. The other ten — `Request`, `Path`, `EditorPath`, `User`, `Aborted`, `AssetUrl`,
the three `Log` members and `Capabilities` — stay.

Three things make doing it worth the churn, and one of them is not "a smaller surface":

1. **One name for one job.** The commit rule is the reason `FlushAsync` carries a thirty-line remark
   today. "You write into this; it is applied when you return, or at the first flush" becomes the
   type's own summary, and the remark shrinks to a sentence.
2. **A helper can take the output and nothing else.** A page can write
   `static void Card(IPageResponse output, …)` and get a seam that cannot read identity, spend a
   capability or see the request. That is the one thing a page author gains.
3. **It stops the context growing again.** Every response-shaped member has a home beside the ones it
   must agree with, instead of being appended to twenty.

It is *not* fewer things to learn: it is ten and ten, and `ctx.Response.Write` is longer than
`ctx.Write`. The gain is where the members sit.

**The object is real, not the context in a different hat.** The contract names a response, so `Web`
gets one: `SimpleResponse` implements `IPageResponse` over `PageResponseState` (the settings being
assembled) and `PageBody` (the bytes and the commit), and owns the gates that decide who may set what:
the commit, the layout no-op, the stream refusals, the antiforgery mint and the login redirect. What is
left of `SimpleContext` is the request side — request, identity, the logging category, `AssetUrl`, the
capability registry — plus `Response` and the two members that make the response and the capabilities
agree. It lands at roughly half the size it is today.

**The old spelling has to keep working for a release.** `RuntimeId` includes the `Sdk` module's version
id, so the commit that adds the interface invalidates every stored image. The ten old members stay as
`[Obsolete]` default interface members that delegate to `Response`, as warnings and never errors, which
is exactly what `PAGE005` did for `ctx.Features`, and `PAGE009` is the next free code.

## Goals

1. `IContext` declares ten members and one of them is `Response`; `IPageResponse` declares the ten
   that shape the response, and `SimpleResponse` is the object behind it.
2. A page's helper can take `IPageResponse`; `IHandler.HandleAsync`, its `IContext` parameter, the
   API policy and `PAGE002` do not change.
3. A stored page that still says `ctx.Write` compiles, is served, and shows one `PAGE009` warning at the
   line that says it — so the owner's unedited pages keep working and the editor shows what to change.
4. Every seeded page, `/help`, the assistant's prompt and every test template say `ctx.Response`, so
   the shims exist only for pages the owner has not touched.
5. The wire is unchanged: the same status, headers, body, cookies and content type.

## Non-goals

- **Removing the shims.** That is `idea-old-response-spelling`, written by this change and due one release later; it is
  the step that makes the surface ten and ten for real.
- **A narrower response for a layout.** The layout rule stays "ignore and log", as
  [IDEA-048](../backlog/IDEA-048.md) says; narrowing what a layout's context offers is a change to what a
  layout is.
- **Moving anything else.** `AssetUrl` is a fact about the site, not about this response; the `Log`
  members are the only way a page's logging reaches the operator, and `LogLevel` is not in the page
  API.
- **Touching `PageResponse`.** The value is unchanged; only where the page records what it wants moves.

## What exists today

| Surface | Says | Count |
| --- | --- | --- |
| `IContext` | twenty members, ten of them the response | [IContext.cs](../../../src/uk.osric.asp.Sdk/IContext.cs) |
| `SimpleContext` | implements all twenty over `PageBody` and `PageResponseState` since the split | 373 lines |
| Seeded pages | `ctx.Write`, `ctx.FlushAsync`, … | 108 uses across seven files: `_lib/document` 34, `/help` 44, `/pages` 16, `_lib/editor` 8, `/editor-mode` 3, `/scratch` 2, `/admin` 1 |
| Assistant prompt | the same spelling in prose and examples, plus a contract section built by reflection from `IContext` | 47 uses; `page-author-v34` |
| Test templates | pages written as C# strings and compiled by the real compiler | 233 uses across 40 files in four suites |
| Docs | the contract, the streaming section, the header allow-list | 32 uses outside `docs/archive` |
| Diagnostics | `PAGE001`–`PAGE008` are taken, `PAGE005` is retired and never reused | `PAGE009` is free |

## The design

### D1 — `IPageResponse` declares the ten, and `IContext` declares `Response`

The members move with their documentation, unchanged in wording:

| Member | Why it is the response |
| --- | --- |
| `Write`, `WriteRaw` | the body, escaped and raw |
| `FlushAsync` | the commit |
| `SetStatus`, `SetContentType`, `SetHeader` | the settings applied at the end or at the first flush |
| `AllowScripts` | the script half of the security policy |
| `Redirect`, `RedirectToLogin` | the two response shapes |
| `AntiforgeryField` | the token for the form in the body |

Three of those placements are judgement calls, and the line is **the response is what changes the
response; a fact about the site stays on the context**:

- `AntiforgeryField` is the response: it is minted under the body's gate already, and
  `IContext.Response` is where that gate now hangs.
- `RedirectToLogin` is the response: it is a redirect. The guard still reads `ctx.User` on the context.
- `AssetUrl` stays: it answers "is this link a live asset?" about the site, not about this response, and
  `ctx.Response.AssetUrl` reads wrong.

`IsCommitted` stays off the contract: a page knows whether it flushed.

### D2 — `SimpleResponse` is the response, and `SimpleContext` is what is left

```csharp
internal sealed class SimpleResponse : IPageResponse { … }   // Web/Pages/SimpleResponse.cs

internal sealed class SimpleContext : IContext {
    public SimpleResponse Response { get; }
    IPageResponse IContext.Response => Response;
}
```

`SimpleResponse` composes the two collaborators and owns the rules about who may ask:

| It takes | It answers |
| --- | --- |
| `PageResponseState` | `ToResponse`, and the settings behind `SetStatus`, `SetContentType`, `SetHeader`, `AllowScripts`, `Redirect` |
| `PageBody` | `Write`, `WriteRaw`, `FlushAsync`, `CompleteAsync`, `GetBody`, `IsCommitted`, the token mint |
| its path and logger | the layout no-op and its warning |
| the request method and the login path | `RedirectToLogin` |
| `wouldWrap` and the layout flag | the four stream refusals |

The commit gate, `IgnoreAsLayout` and `ThrowIfCannotStream` move with it, because all three answer "may
this layer set that?" about the response. `SimpleContext` keeps `AttachLifetime` and `Abandon` because
they are the one point where the response and the capability registry have to agree, and it keeps
`GetBody`, `IsCommitted`, `ToResponse` and `CompleteAsync` off its own surface: the shell says
`page.Response.…`, which is what those calls always meant.

The shell change is nine call sites in `PageEndpoint` and `PageChain`; `PageRenderer`, `PageHarness` and
`PageChain`'s construction still take `SimpleContext`.

**Rejected: `Response => this`.** It is cheap — the context already implements all ten — but the
contract would then be shaped by the class that also holds the request, the identity and the logging,
which is the coupling the last split was for. It also leaves `ctx.Response` castable back to `IContext`.
The object is worth a second move of members that have already been reviewed once.

### D3 — The old names survive one release as `PAGE009` warnings

`Sdk` gains one internal constant, as `PAGE005` did, so the message cannot drift:

```csharp
// Sdk/PageResponseDeprecation.cs
internal const string Deprecated =
    "Use IContext.Response and IPageResponse: a page writes into its response.";
```

and each of the ten old members becomes a default interface member on `IContext`:

```csharp
/// <summary>Writes escaped HTML to the output. Deprecated: use <see cref="Response"/>.</summary>
[Obsolete(Deprecated, DiagnosticId = "PAGE009")]
public void Write(string message) => Response.Write(message);
```

- **A warning, never an error.** `[Obsolete]` defaults to `error: false` and the default is kept.
  `RuntimeId` moves because the `Sdk` module id is part of it, so every stored page recompiles from its
  stored source on the next request. With a warning, all of them compile, each old use records one
  `PAGE009`, and the site keeps serving; with an error, every unedited page fails and the site answers
  diagnostics instead of pages. `pages-a-warning-never-stops-a-page` is the rule.
- **The warning is the migration.** A page compiled with warnings stores its image and says "Compiled
  with warnings" in the editor and the page list, and the diagnostic points at the author's line.
- **`PAGE005` is retired and never reused** (`pages-what-a-diagnostic-is`); `PAGE001`–`PAGE004` and
  `PAGE006`–`PAGE008` are live, so this takes `PAGE009`.
- **The shim is one implementation.** `SimpleContext` implements `IPageResponse` and nothing obsolete,
  so the host builds with no suppression, and the prompt's existing `[Obsolete]` filter keeps the old
  spelling out of what the assistant is taught.

### D4 — The migration lands in the same change

- **The seeded pages**: 108 uses in seven files, rewritten to `ctx.Response.*`. `/help` is the author
  manual, so its "Context and capabilities" section is rewritten rather than renamed: it says what the
  response is, that it is applied at the end or at the first flush, and that a helper can take
  `IPageResponse`.
- **The assistant's prompt**: the reflective contract section lists both interfaces (`IContext` and
  `IPageResponse`), the prose and the examples say `ctx.Response`, and `Version` moves to
  `page-author-v35` — or the next free if [page-move](../page-move.md) lands first.
- **The test templates**: 233 uses in 40 files across the four suites. Mechanical, but it is where a
  missed use would live on as a warning forever, so a test compiles every seeded page and asserts it
  records no `PAGE009`.
- **The docs**: `pages.md` (the contract, streaming, the header allow-list, which now names
  `PageResponseState`), `architecture.md` (the diagnostics table gains `PAGE009`, with the retired-with-
  the-shim note), `testing.md` where it names the suite that pins the contract, and the glossary, which
  gains a **Response** entry so the word the prompt and `/help` use has a defined meaning.

### D5 — The record

> **pages-the-response-is-one-object** — what a page writes into is one object on the context,
> `IContext.Response`, of type `IPageResponse`. It is contract rather than a capability, because every
> page always has one; it is a member rather than ten so the buffering and commit rule has a home; and
> a page's helper can take it to get a seam that cannot read the request or spend a capability.

`pages-capabilities-are-one-seam` is edited rather than retired: its sentence says the response "stays
on `IContext`", which is still true — it stays there as `IContext.Response`.

## What it touches

`Sdk`: `IPageResponse.cs` new; `IContext.cs` keeps ten members, gains `Response`, and keeps the ten old
ones as obsolete shims; `PageResponseDeprecation.cs` new.

`Web`: `SimpleResponse.cs` new, holding the ten members and the gates; `SimpleContext.cs` keeps the
request side and the wiring; nine call sites in `PageEndpoint` and `PageChain` say `page.Response.…`.
The seeds, `PageAuthorPrompt` and `/help` change their spelling.

Tests: 40 test files across `Core`, `Runtime`, `Web` and `IntegrationTests` for the templates; new
tests for the two spellings, the helper seam and the prompt's two contract sections; `TestContext` in
the compiler tests gains a `Response`.

Docs: `pages.md`, `architecture.md`, `testing.md`, `glossary.md`, `decisions.md`.

## Phases

One branch, three commits, so each can be read alone:

1. **The contract, the object and the shims** — `IPageResponse`, `IContext.Response`, the obsolete
   members, `SimpleResponse`, the slimmed `SimpleContext`, the shell's call sites, `TestContext`, and
   the tests that pin the new spelling compiling clean, the old one warning with `PAGE009`, a helper
   taking `IPageResponse`, and the gates that moved to `SimpleResponse`.
2. **The pages we ship** — the seven seeds, `/help`'s prose, the prompt and its version, and the test
   templates.
3. **The docs and the record** — the four docs, the decision entry, the edit to
   `pages-capabilities-are-one-seam`, IDEA-048 moved to closed, and `idea-old-response-spelling` written for the removal.

## Verification

- `make check` is the gate before the merge stop.
- The new tests are the proof of the seam: a page using `ctx.Response.Write` compiles with no
  diagnostics; a page using `ctx.Write` compiles with exactly one `PAGE009` at `Warning`, not an error;
  a declaration-block helper taking `IPageResponse` compiles; every seeded page compiles with no
  `PAGE009`; the prompt lists every member of both interfaces and never teaches the old spelling.
- The proof that behaviour did not move is that no existing test changes what it asserts — the wire is
  the same value, and the integration suites exercise it through the real host. One assertion does
  move: a call-level diagnostic inside a write now reports nine columns further right, because the
  emitted call is `ctx.Response.Write(` and the compiler names the member (`Write`). Expression errors
  keep their exact position, and the test says why. The gate tests that
  move to `SimpleResponseTests` (the three layout, the four refusals, settings-after-the-flush) keep
  their names and their assertions.

## Risks

- **The one line that must be right is `error: false`.** An obsolete-as-error would take every stored
  page down at its next render, including the ones the owner cannot see from the repository.
- **The shims outliving their release.** They are invisible in the prompt and in `/help` by design, so
  nothing but `idea-old-response-spelling` closes them; the plan writes that job in the same change.
- **A missed template keeps the old spelling silently**, because a warning still compiles. The
  seeded-page sweep is covered by a test; the test-template sweep is checked by the compiler's own
  warning count in `make check`, which treats a warning as an error in the suites themselves.
- **The prompt's contract section is generated**, so it changes without anyone editing prose: a test
  that nothing documents is the guard, and it must be widened to both interfaces in the same commit
  as the interface, or the assistant loses the member list.
- **The gates move with the object**, and they are the part that is observable: a layout's setting
  must still be a logged no-op, a flush after the commit must still refuse, and the antiforgery token
  must still be minted once and never after abandonment. The eight tests that pin them move with the
  members rather than being rewritten.

## The questions this plan asked the owner

1. **The object behind `Response`.** The owner chose a real `SimpleResponse` over `Response => this`:
   after the split the response is a thing in `Web`, so the contract should name that thing rather than
   the context (see D2).
2. **Shims for one release**, as planned — a hard cut would stop every stored page compiling.
3. **The prompt teaches only `ctx.Response`** during the shim release, so the assistant stops writing
   pages that warn.

## Considered and rejected

- **A hard cut with no shims.** Tidy in the repository and hostile to the live store: `RuntimeId`
   moves, so every unedited page fails to compile and the site answers diagnostics.
- **`IContext : IPageResponse` with `Response => this`.** Breaks nothing and gives the narrow type
   immediately, but the members stay *declared* on `IContext`, so the surface does not shrink and the
   old spelling never goes.
- **`Response => this`.** See D2: it avoids a second move, and it leaves the contract shaped by the
  context and the response castable back to it.
- **Moving `AssetUrl` and the `Log` members too.** A link resolver is not a response, and logging has no
   other route to the operator.
- **Leaving the seeds on the old spelling during the shim release.** The manual and the prompt are
   what teach the next page; the shims are for pages that already exist.

## Not in scope

- Removing the obsolete members — `idea-old-response-spelling`, one release later.
- A narrower response for a layout, or any change to the layout rule.
- Making the response a capability.
- Anything a page can do that it could not do yesterday: the same ten operations, reached one step
  further away.

## Status

Built 2026-11-16 on `idea-048`, in two commits, and waiting for the merge. The contract, the object, the generator, the seeds, the manual, the prompt and the test templates are one commit, because the generator writes the page source and the seeds are the examples: splitting them would land a revision where every page warns.

**What landed.** `IPageResponse` declares the ten response members and `IContext` declares
`Response`; the ten old members stay as `[Obsolete]` default interface members under `PAGE009`, one
implementation that delegates to `Response`. `SimpleResponse` is the object behind it, over
`PageResponseState` and `PageBody`, and owns the commit gate, the layout no-op, the four stream
refusals, the antiforgery mint and the sign-in redirect. `SimpleContext` is 215 lines and holds the
request, the identity, the logging, `AssetUrl`, the capability registry, `Response`, and the two
members that make the response and the capabilities agree.

**The migration.** Seven seeded pages and `/help` (108 uses), the assistant's prompt (prose, examples,
a contract section that now lists both interfaces, and `page-author-v35`), 48 test files, and every
live doc. The generated source changed with them: `TemplateSourceGenerator` emits
`ctx.Response.Write(` and `ctx.Response.WriteRaw(`, which is why every page — not just the pages that
wrote the old spelling — would otherwise have warned.

**What proved it.** 845 + 180 + 1931 + 241 green, `make format-check`, `make decisions` and
`make license-check` clean. New tests pin the three things the change is: a page that writes through
`ctx.Response` compiles with no diagnostics, a page that writes through `ctx` compiles with exactly one
`PAGE009` at `Warning`, and a declaration-block helper can take an `IPageResponse`. The eight gate
tests moved to `SimpleResponseTests` with their names and assertions; `SimpleContextTests` keeps the
content-type mapping, the identity and the logging.

**What is unsure.** The shims are the point and the risk: they are visible nowhere except the store's
warnings, and `idea-old-response-spelling` is what removes them. `PAGE009`'s column shift (above) is the one observable
behaviour change. `page-move`'s plan claimed `page-author-v35` first and now says v36.
