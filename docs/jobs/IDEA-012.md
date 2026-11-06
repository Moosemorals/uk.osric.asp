<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-012 — A page cannot await its I/O

| | |
| --- | --- |
| Size | Medium |
| Area | Sdk, Core, Runtime, Web |
| Location | `src/uk.osric.asp.Sdk/IHandler.cs`, `src/uk.osric.asp.Sdk/IContext.cs`, `src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs`, `src/uk.osric.asp.Runtime/LoadedPage.cs`, `src/uk.osric.asp.Runtime/PageApiChecker.cs`, `src/uk.osric.asp.Web/Pages/PageRenderer.cs`, `src/uk.osric.asp.Web/Errors/ErrorEndpoint.cs`, `src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs` |

## Status

Open, 2026-11-06. The recommendation is **now and all the way**: make `IHandler` asynchronous
(`HandleAsync` returning `Task`) and make every I/O member on `IContext` asynchronous, before the
fetch and storage plan's Phase 1 starts. This file deliberately does not change that plan:
[D3 and D6](../plans/page-fetch-and-storage.md) still choose a synchronous page surface, and they
are exactly what this idea argues against. If the idea is accepted, those sections and Phase 1 are
revised first.

## Idea

`IHandler.Handle` returns `void`, so by construction every capability a page can reach is
synchronous. The [fetch and storage plan](../plans/page-fetch-and-storage.md) already follows that
logic: synchronous EF Core calls, a synchronous `HttpClient` call, the page on a dedicated thread,
and a blocked thread accepted as the price. The plan then buys safety around that price — a fetch
timeout below the render budget so a slow API becomes a value the page can render, and the existing
abandoned-thread accounting for the calls that outlive it.

The alternative is asynchronous all the way:

- `Task HandleAsync(IContext context)` on `IHandler`;
- `Task<T>` from every I/O member on `IContext`, not just the handler.

A `Task`-returning `Handle` with a synchronous `IContext` is the option to avoid. Pages would either
block inside an async signature or write `.Result`, which is the worst of both shapes and hides
where the blocking is.

### Why now

The fetch and storage plan is **Proposed 2026-11-06 and unimplemented**: there is no `IPageStorage`,
`IPageFetcher`, `PageState` table or new SDK member in `src/`. Nothing in page code calls the
synchronous APIs yet, so the page-author migration is zero today. The plan already changes the SDK
assembly and bumps `PageApiPolicy.PolicyVersion`, and every stored image is recompiled on its next
request, so changing the handler signature in the same release adds no recompile and no second
policy bump.

Deferred until after fetch and storage ship, the same change is a second breaking contract change,
a second policy bump and a rewrite of every page that has started using `ctx.Storage` and
`ctx.Fetch`. The plan's claim that async "would change every page, every template expression and
the compiler's generated signature" overstates the author cost — pages are templates, and the
generated handler is not written by hand — but it does not make the change free later.

### What async would and would not buy

| Lever | Synchronous (plan today) | Async, all the way |
| --- | --- | --- |
| Thread use while a page waits | One thread parked per in-flight I/O call; bounded by `Pages:Render:Slots` (default 2) | Thread returned to the pool during each await |
| Abandoned render | A blocked thread is the known loss; it unwinds when the call's token fires | The task unwinds on cancellation and releases its thread; a synchronously blocked page still holds one |
| Cancellation | Linked tokens cancel the SQLite/HttpClient call | The same linked tokens cancel the await; no extra mechanism |
| Storage | Synchronous EF over local SQLite, which is effectively synchronous anyway | Async EF adds a state machine and buys little on a local file database |
| Fetch | Blocks a worker for up to the per-fetch timeout | Natural `HttpClient.SendAsync`; no sync-over-async |
| Runtime | Reflect, invoke, unwrap `TargetInvocationException` | Reflect, invoke, await the task, classify cancellation, unwrap without an `AggregateException` |
| Author diagnostics | Stack frames name `Handle`, mapped straight to the page | Async frames are `MoveNext`; the error page and its test need remapping |
| Page policy | `System.Threading` denied beyond `CancellationToken` | `Task` must be admitted for the generated signature; `Task.Run` and friends become reachable unless explicitly denied |
| Fire-and-forget | Not expressible in the page API | `_ = ctx.FetchAsync(...)` is possible and can outlive the render |

The honest summary: at `Slots = 2` and one local database, async buys little capacity. What it buys
is the honest shape for network I/O, cheap unwinding of abandoned renders, and not having to
reverse the contract later. The costs are real but bounded, and they are all implementation costs
rather than page-author costs while the surface is still new.

## Sketch

1. **Decide the timing first.** Accept or reject this before fetch and storage Phase 1. If accepted,
   revise the plan's D3 and D6 and fold the new signatures into Phase 1 rather than shipping sync
   members first. Do not add both a sync and an async API: that doubles the surface and invites
   sync-over-async.
2. **Name it `HandleAsync`.** The repository's async convention is the `Async` suffix, and the
   generated signature is internal to the pipeline, so the rename is free now. Update
   `TemplateSourceGenerator`, `PageApiChecker`'s `HandlerMethodName`, `LoadedPage`'s `GetMethod`
   lookup, and the diagnostic label in `ErrorEndpoint`.
3. **Async the I/O surface, not the whole contract.** `Write`, `Log`, `SetStatus`, `SetContentType`,
   `SetHeader` and `Redirect` stay synchronous — they touch a buffer and a logger. Only
   `Storage.Get/Set/Delete/Keys/ContainsKey` and `Fetch` become `Task`-returning. Keep the
   `CancellationToken` out of the signatures: the context already owns `Aborted` and the render
   deadline, and the implementation links them per call. Document that, because it is a deliberate
   exception to the codebase's token-last convention.
4. **Generator.** Emit `public async Task HandleAsync(uk.osric.asp.Sdk.IContext ctx)`. A page's
   `await` inside a write expression is then just an expression: `ctx.Write(await ctx.FetchAsync(...))`
   is valid. Emit `#pragma warning disable CS1998` around the method rather than relying on the
   warning happening to be unmapped and dropped by `PageCompiler.MapDiagnostics`; a page with no
   `await` must still compile cleanly.
5. **Runtime.** `LoadedPage.InvokeAsync` awaits the returned task, rethrows the page's own exception
   with `ExceptionDispatchInfo`, and distinguishes a cancelled task from a page that threw
   `OperationCanceledException` deliberately. Keep the unwrapping rule: the shell sees the page's
   exception, never a reflection or aggregate wrapper.
6. **Renderer.** Keep `Task.Run` and the `Task.WhenAny` deadline. Async does not make arbitrary C#
   interruptible, and a page can still block synchronously; running it off the request thread stays
   the protection for the request. `Abandon` counts an unfinished task rather than a thread, and
   the health wording should say "render" rather than "thread" where that is now accurate.
7. **Diagnostics.** `ErrorEndpoint` filters stack frames by `GetFileName() == "page"` and labels the
   frame `Handle(IContext ctx)`. Async frames are `MoveNext`, and a throw inside an awaited call
   can produce duplicates. Map async frames to the page's line, deduplicate, and keep the author
   link intact. Update `ErrorHandlingTests`, which asserts the exact `at Handle(IContext ctx) in
   /throw:line 3` string.
8. **Policy.** The generated `Task` return type must be allowed or every page fails as a compiler
   fault, but a type-level allow also grants page code `Task.Run`. Add a member-level denial for the
   static entries that start work outside the render (`Task.Run`, `Task.ContinueWith`, `Factory`)
   and a compile test for each. `Task.Delay` needs a decision: harmless when token-linked, a way to
   outlive the render when not.
9. **Fire-and-forget.** A page that drops a task can write, fetch or store after the response. The
   existing `SimpleContext.Abandon` check refuses later calls, and the buffered response prevents a
   late body, but in-flight work already started is not stopped. Document it as page-author error;
   add a test that a late continuation cannot change the response. Do not build an analyzer for it
   in this change.
10. **Tests.** An async page compiles and renders; `await` inside a write expression emits the awaited
    value; an exception after an `await` reaches the error page with the page's line; a page that
    outlives the deadline is abandoned and its late writes are refused; a cancelled fetch is not
    reported as a page fault; the generated signature and the policy denials are asserted. Keep the
    existing synchronous-page behaviour tests as the regression baseline.
11. **Docs and prompt.** `docs/architecture.md`: the budget section's "thread" language and the page
    contract's synchronous statement. `docs/decisions.md`: one decision for the async contract and
    the tokenless I/O members, superseding the plan's D3/D6 rationale. The page-author prompt gains
    `await` in its examples and one sentence that a dropped task is a bug; it should ship as part of
    the same prompt version as `Storage`/`Fetch`, not a third version immediately afterwards.
12. **Plan revision, if accepted.** Update fetch and storage D3 (synchronous EF) and D6
    (synchronous because the handler is), the storage/fetch type tables, the wiring section, and
    Phase 1's steps. The security and caps design does not change: failure-as-value, the allow-list,
    the per-fetch timeout and the caps all work exactly the same on async calls.

## Relationships

- [ARCH-13](ARCH-13.md) already assumes `Task FlushAsync` because the write is I/O. An async page
  contract makes that a natural member instead of the one awkward exception.
- [IDEA-002](IDEA-002.md) and [IDEA-003](IDEA-003.md) are the trigger: they are the first
  capabilities that make a page wait, and they are still unimplemented.
- [IDEA-005](IDEA-005.md) page-declared types would let a page write its own async helper; that is
  only possible if the policy admits `Task`, which is a reason to settle the policy question here
  rather than there.
- [ARCH-12](ARCH-12.md) changes write expressions. It is unaffected as long as the generator keeps
  accepting an arbitrary expression in `ctx.Write(...)`, `await` included.

## Not this idea

- Streaming the response (ARCH-13). This changes how a page waits, not when bytes are sent.
- Background work, queues, hosted services or parallel fetches started from a page.
- Per-user storage (IDEA-011) or any change to what storage is scoped to.
- Removing `Task.Run`, the render slots, the body buffer or the deadline.
- Making synchronous page code asynchronous by itself. A page that blocks still blocks.
