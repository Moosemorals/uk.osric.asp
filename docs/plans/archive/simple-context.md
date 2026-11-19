<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# `SimpleContext` composes its parts

| | |
| --- | --- |
| Status | Built 2026-11-16 — waits for the merge. Two commits on `idea-022`; see [Status](#status) |
| Size | Small — one file split into two internal collaborators beside it, and the tests that test each part moved to meet it. No contract change, no migration, no configuration key, no build change |
| Area | Web (pages), docs |
| Spike | None. Every measurement below was read off the working tree on 2026-11-16 |
| Backlog | IDEA-022, closed with this plan ([closed jobs](../../backlog/closed.md)) |
| Decisions | Records `code-one-concern-one-collaborator`. Relies on `code-shapes`, `code-nothing-public-without-a-caller`, `pages-capabilities-are-one-seam`, `pages-the-response-is-buffered-until-a-flush`, `pages-page-io-is-async` and `code-tests-are-written-first` |

## The short version

[IDEA-022](../../backlog/closed.md) was right to insist on a re-measure first, and it was right about the
shape. The file did not shrink when the [page-features](page-features.md) plan took
storage and fetch out; it grew from 364 lines to 551, because
[page-streaming](page-streaming.md) then put the flush, the sink, the commit rule and
the tail on top of the buffer. The body is now the largest single job in the file, not the smallest.

The idea offers three routes and says the third is the one that matters: *a split that does not let a
test build the part it tests is cosmetic*. So the answer is composition, not partials. `SimpleContext`
stays the one type a page binds to — `IContext` does not change — and delegates two concerns to two
`internal` collaborators beside it:

- **`PageResponseState`** — *what the page asked the response to be*: the status, content type,
  allow-listed headers, the redirect and the script policy, with the validation each member carries.
  It needs nothing constructed: no request, no logger, no writer, no capability set.
- **`PageBody`** — *the body being written*: the buffer, the one lock, the shared budget, the commit,
  the sink, the lifetime and the abandonment flag, plus the flush and the tail.

What is left is the context's own job: the request, the signed-in visitor, the layer's key, the
logging that is filed under one category, the antiforgery token, `AssetUrl`, the layout rule and the
delegation itself. That is a class a reader can walk in one pass.

The test payoff is concrete today. Of the 51 test methods in
[SimpleContextTests](../../../test/uk.osric.asp.Web.Tests/SimpleContextTests.cs), 19 are about the
response surface and 13 about buffering and streaming, and every one of them builds a full context
through one of two helpers — a logger factory, a request, a capability set, a content-type table and a
writer — to assert something about a status code, a header or a buffer. After the split,
`PageResponseStateTests` starts at `new PageResponseState("text/html; charset=utf-8")`, and
`PageBodyTests` starts at a writer, a budget, a commit state and a fake sink.

## Goals

1. A member can be found without scrolling past the four other jobs in the file.
2. A test builds the part it tests: the response surface with no context at all, the body with a
   writer and a sink, and the context only for what is genuinely the context's.
3. One file per job, and a rule in the record so the next member joins a collaborator rather than the
   context.
4. Nothing a page can see changes: `IContext`, the generated handler, `PAGE002`, the API policy, the
   wire and `RuntimeId` are all untouched, so no stored page recompiles and no image is rebuilt.
5. Every rule stays where it is enforced today: the commit gate, the layout no-op, the header
   allow-list, the body cap, the one-token-per-layer rule and the flush order.

## Non-goals

- **No partial classes.** They move the members and leave one type, one constructor and one set of
  fields, so the testability target is missed by design. See
  [Considered and rejected](#considered-and-rejected).
- **No seam on `IPageCapabilities`.** The response is not a capability a page asks for; it is part of
  the contract every page always has ([`pages-capabilities-are-one-seam`](../../decisions.md)). The
  collaborators are constructed by the context, not offered through the registry.
- **No logging or antiforgery collaborator.** Logging is 21 lines and one category rule; antiforgery
  is 15 lines and one token per layer. Each reads in place, and neither gains a test setup worth the
  indirection. The rule this plan records says what to do if one of them grows.
- **No constructor change.** `PageChain`, `PageEndpoint` and `PageHarness` keep calling the same
  seventeen parameters. The collaborators are built inside it.
- **No behaviour change at all.** This is a move; a moved test keeps its name and its assertion.

## What exists today

`SimpleContext` is 551 lines. By concern, with the line ranges a reader has to scroll through:

| Concern | Lines | Today |
| --- | --- | --- |
| Constants and fields | 26–71 (46) | One block, half of it only one concern reads |
| Constructor and its docs | 73–158 (86) | Stays; it is the one place the wiring is visible |
| Identity, capabilities, `AttachLifetime`, `Abandon` | 160–221 (62) | Stays |
| Logging | 223–243, 383–394 (33) | Stays |
| Writing, flushing and the tail | 245–373, 396–412 (146) | Moves to `PageBody` |
| Response surface: `ToResponse`, status, content type, headers, scripts, redirect, antiforgery, allow-list | 183–189, 414–551 (145) | Status, content type, headers, redirect and scripts move to `PageResponseState`; the commit gate, the layout no-op, `RedirectToLogin` and the antiforgery token stay |

The two helpers in the test file are `CreateContext` (42 call sites) and `CreateStreamingContext` (13),
and between them they name a logger factory, a request, a page path, a capability set, a content-type
table, an editor key, a writer, a budget and a sink.

## The design

### D1 — `PageResponseState` is what the page asked for

A new `internal sealed class` in `uk.osric.asp.Web.Pages` that holds the response being assembled and
the rules each member enforces:

```csharp
internal sealed class PageResponseState {
    public PageResponseState(string contentType);
    public string ContentType { get; }
    public string? Location { get; }
    public void SetStatus(int status);
    public void SetContentType(string contentType);
    public void SetHeader(string name, string value);
    public void AllowScripts();
    public void Redirect(string location, int status = 302);
    public PageResponse ToResponse();
}
```

- The allow-list (`Content-Language`, any `X-` name, never one the shell owns), the line-break refusal
  and the `100`–`599` range move with the members that enforce them, unchanged in wording.
- `ToResponse` writes `Cache-Control: no-store` into the dictionary it holds, as today: the default is
  the state's, and the host's header is still written after it.
- `ContentType` and `Location` exist because the flush needs both to refuse a stream: a redirect has no
  body, and a content type that would be wrapped has no place for the bytes.
- **What stays on `SimpleContext`:** `ThrowIfCommitted` (the commit is shared with the capabilities, so
  the gate is the context's answer), the layout no-op and its warning, `RedirectToLogin` (it reads the
  request method and the layer's login path) and the antiforgery token. Each delegating member keeps
  its present order — check committed, check layout, then validate — because that order is observable:
  a layout calling `SetStatus(99)` is ignored and logged today, not refused.

### D2 — `PageBody` is the body being written

A new `internal sealed class` that owns the buffer and everything that has to agree with it:

```csharp
internal sealed class PageBody {
    public PageBody(StringWriter writer, PagePath path, CancellationToken aborted,
        BodyBudget budget, PageCommitState commit, IPageBodySink? sink = null);
    public bool IsCommitted { get; }
    public bool CanStream { get; }
    public void AttachLifetime(RenderLifetime lifetime);
    public void Abandon();
    public void Write(string text);
    public string GetText();
    public string MintOnce(ref string? slot, Func<string> mint);
    public Task FlushAsync(PageResponse response);
    public Task CompleteAsync();
}
```

- `Write` keeps the cap and the abandonment check under the gate, and keeps the rule that the cap stops
  mattering once the response is committed.
- `FlushAsync` takes the finished `PageResponse` rather than asking for one: the response belongs to
  the layer, and the body has no back-reference to the context. The order is unchanged — linked token,
  flush gate, take the buffer under the body lock, `BeginStreamAsync`, mark committed, `StartAsync`,
  write, `ReArm` — and the tail is the same `CompleteAsync` that exists today.
- `MintOnce` is the abandonment gate made explicit for the one value that is minted rather than
  written: it runs the factory under the body lock and returns empty once the render is abandoned, so
  the token-and-cookie pair cannot be minted after the response has gone. It exists on the body
  because the lock lives there; the field and the factory stay on the context.
- `CanStream` is `sink is not null`. The refusal *messages* stay on the context, because each names the
  page and the remedy, and three of the four checks are facts about the layer (a layout, a redirect, a
  content type that would be wrapped) rather than about the body.
- The `CA1001` suppression moves with the `SemaphoreSlim` it exists for.

### D3 — What `SimpleContext` becomes

Fields that move: `_body` (the lock), `_writer`, `_flushing`, `_bodyBudget`, `_abandoned`, `_bodySink`,
`_lifetime`, `_deadline`, `_commit` and the five response fields. Fields that stay: `_request`,
`_path`, `_editorPath`, `_loginRedirect`, `_user`, `_isLayout`, `_logger`, `_assetUrl`, `_wouldWrap`,
`_antiforgeryFactory` and `_antiforgeryField`, plus the two collaborator references and `Aborted`.

Every member a page sees keeps its signature and stays on the context; the ones that only touch the
response or the body become one-line delegations. `ToResponse()` remains the endpoint's question, and
`IsCommitted` remains the renderer's, both answered through the collaborator.

### D4 — The name the record takes

> `code-one-concern-one-collaborator` — a type that implements a wide contract composes one
> `internal` collaborator per concern rather than collecting members, and it is split by composition
> rather than by `partial`. The test is the point of it: a collaborator can be built and asserted
> without the whole contract's dependencies, so a test that needs the whole context is testing the
> context and not the part.

It is recorded under `Code` beside `code-shapes`, and is a straight extension of that entry's "prefer
composition over inheritance" to composition over a single do-everything type. `SimpleContext` is its
first recorded customer; the rule is written for the next type that grows a fifth job.

## What it touches

`Web`:

- `Pages/PageResponseState.cs` — new.
- `Pages/PageBody.cs` — new.
- `Pages/SimpleContext.cs` — keeps the contract, the constructor, identity, logging, antiforgery,
  `RedirectToLogin`, `AssetUrl`, the layout rule and the stream refusal; delegates the rest.
- Nothing else. `PageChain`, `PageEndpoint`, `PageRenderer` and `PageSessionScope` compile unchanged
  against it, because the constructor and the `IContext` members do not move.

Tests:

- `SimpleContextTests.cs` — the response-surface tests move to `PageResponseStateTests.cs` and the
  streaming ones to `PageBodyTests.cs`; they keep their names and assertions, and lose the setup they
  no longer need. What is left is the context's own: `CreateContext` for identity, `RedirectToLogin`,
  the antiforgery token, and the logging; `CreateStreamingContext` for the throw-after-commit, the
  layout no-op and the stream refusal.
- `PageHarness.cs`, `PageSessionRenderTests`, `PageFetchRenderTests` — unchanged.

Docs and the record, at the merge:

- `docs/decisions.md` — the **code-one-concern-one-collaborator** entry, recorded in
  `architecture.md#shapes` and this plan.
- `docs/architecture.md#shapes` — one sentence: composition over a single type, with this as the
  example.
- `docs/pages.md` — where the buffered and streaming implementations are described, the two names
  beside `SimpleContext`.
- `docs/jobs/IDEA-022.md`, its row and its step — deleted, and the job recorded in
  [closed](../../backlog/closed.md) with this plan linked.
- `/help` is not touched: no page author can do anything new.

## Phases

Two commits on one branch, so the smaller one can be read on its own.

1. **`PageResponseState`.** Move the state and the five members and their validation; delete the moved
   tests from `SimpleContextTests` and add them to `PageResponseStateTests`; keep the context tests for
   the commit gate, the layout no-op and `RedirectToLogin`.
2. **`PageBody`.** Move the buffer, the gate, the budget, the commit reference, the sink, the lifetime
   and the abandonment flag; move the flush and the tail; move the 13 streaming tests and add the
   writer-and-sink setup they actually need; turn `Write`, `GetBody`, `Abandon`, `AttachLifetime`,
   `IsCommitted` and `AntiforgeryField` into delegations.

## Verification

- `make test-web` runs the two suites and the rest of `Web`; `make check` is the gate before the merge
  stop.
- The proof a move preserved behaviour is that a moved test keeps its name and its assertion and still
  passes. No moved test is rewritten to say something new; where a test now needs less setup, the
  assertion and the test name are what is left.
- `make decisions` resolves the new entry and every citation, including this plan's.
- `RuntimeId` does not move: `IContext` and the API policy are untouched, so `PageApiPolicyTests` and
  the seed compilation are unchanged, and no stored page is recompiled.

## Risks

- **The flush order is the subtle part.** `BeginStreamAsync` before the commit, the commit before
  `StartAsync`, the buffer taken under the lock before either, `ReArm` last: each line exists for a
  reason recorded in `pages-the-response-is-buffered-until-a-flush` and `pages-page-io-is-async`. The
  move is line for line and the 13 streaming tests travel with it; if one of them needs a change to
  pass, the move is wrong rather than the test.
- **The abandonment gate can be lost in translation.** `MintOnce` exists so the antiforgery token is
  still minted under the same lock that stops the writes, and still empty after `Abandon`. The two
  tests that pin it travel unchanged.
- **Delegation can hide a rule.** The context-level rules — throw after the commit, a layout's setting
  is a logged no-op, `RedirectToLogin` picks `303` for a `POST` — keep their tests on the context, so
  a delegate that skips a gate fails a test rather than passing quietly.
- **Two types with `Page` names beside `PageBodySink`.** The sink is what consumes what the body
  produces; the names are meant to read as one pipeline. If that is a puzzle on the branch, the body
  is `PageBodyBuffer` and the plan is unchanged otherwise.

## The questions this plan asked the owner

The owner said yes on 2026-11-16, and chose composition over partials before the plan was written:

1. **Both phases** — both are built, in the two commits below.
2. **The name** — `code-one-concern-one-collaborator`, as proposed.
3. **The type names** — `PageResponseState` and `PageBody`, beside the existing `PageBodySink`.

## Considered and rejected

- **Partial classes by concern** (the idea's option 1). It is a pure move with no risk, and no call site
  changes — but it leaves one type, one constructor, one field block and one `SimpleContext` in every
  test. The idea already names this as cosmetic, and the owner has chosen composition.
- **One collaborator for "the response"**, holding both the state and the body. The two do not share a
  lifetime: the state is finished at the first flush and the body carries on, and a test of the header
  allow-list has no business constructing a writer, a budget and a flush gate. The seam is where the
  commit happens, so that is where the split goes.
- **A collaborator behind `IPageCapabilities`** (the idea's option 2, taken literally). The response is
  contract, not capability; a page asks for storage or a fetch, never for its own status code.
- **A `PageLog` wrapper for the logging.** It would move 33 lines and eight tests, but it is a proxy
  around `ILogger` with one scope, and the category rule is easier to see where the calls are.

## Not in scope

- **`IContext` itself.** Making the response one object in `Sdk` is [IDEA-048](../backlog/IDEA-048.md),
  raised while this plan was written: it is the same split seen from the contract side, and the two
  collaborators below are what its implementation would be built over. It is a contract change with a
  migration of its own, so it waits until this has landed, rather than moving the members twice.
- Anything a page author can do. There is no new member, capability, content type or response rule, so
  `/help` and the assistant's prompt stay as they are.
- The rest of IDEA-022's "five jobs" beyond these two: logging and antiforgery stay on the context, and
  the rule the record takes says where a future one goes.
- The capability registry, the renderer and the shell. `PageRenderer` still takes the `SimpleContext`;
  narrowing that to an interface is a different question about a different seam.

## Status

Merged. `code-one-concern-one-collaborator` is in [the record](../../decisions.md) and its item is in
[closed.md](../../backlog/closed.md). Built on `idea-022`, in two commits.

**What landed.** `SimpleContext` is 373 lines, down from 551, and the two collaborators are
`PageResponseState` (121 lines) and `PageBody` (226, including the flush that used to be the largest
block in the context). The context keeps the contract, the constructor, identity, the logging, the
antiforgery token, `AssetUrl`, the layout rule, the commit gate and the four stream refusals; every
other member is a delegation.

**What proved it.** Fifteen response-surface tests moved to `PageResponseStateTests` and seven buffer
and stream tests to `PageBodyTests`; both suites construct only the part they test. The two
status-range tests were one behaviour and are now one test. Six of the streaming tests did not move
because they are the context's: the no-sink, layout, redirect and wrapping refusals, the escaping
rule on `Write`, and the settings-after-the-commit gate. The layout no-op had no test at all, so the
context gained three — that a layout cannot set the response, that the layout gate runs before
validation, and that a layout may still allow scripts. `make test-web` is 1931 green, `make format-check`,
`make decisions` and `make license-check` are clean.

**What is unchanged.** `IContext`, the generated handler, `PAGE002`, the API policy and `RuntimeId`:
no stored page recompiles, and no seed, `/help` entry or assistant prompt moves.

**What is unsure.** `PageBody.MintOnce` is the one member that does not read like the rest: it exists so
the antiforgery token is still minted under the same gate that stops the writes, and it takes the
context's field by `ref`. The alternative was an exposed lock or a second gate object, and both were
worse. The name of `PageBody` beside `PageBodySink` is the other thing to look at; the plan kept it,
and the sink reads as the consumer of what the body produces.
