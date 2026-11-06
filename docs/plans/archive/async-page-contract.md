<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The asynchronous page contract

Implementation plan for IDEA-012 — a page cannot await its I/O. The idea recorded an argument, not a
decision; this document is the decision set, the shape of the generated code, the exact edits in every
project, and the revisions [fetch and storage](page-fetch-and-storage.md) must take if it is
accepted.

## Status

Implemented 2026-11-06, in five commits from `b3e9715`. Every phase below shipped; IDEA-012 is in the
closed table in [the job index](../../jobs/README.md#closed), and the revisions in
[What fetch and storage must change](#what-fetch-and-storage-must-change) landed in
[fetch and storage](page-fetch-and-storage.md) before its Phase 1 started, so the synchronous
members it described are never written.

The decisions are DEC-123 (the asynchronous contract and its tokenless members) and DEC-124 (the
`Task` admission and the member-level denial, amending DEC-35).

## The short version

1. `IHandler.Handle` becomes `Task HandleAsync(IContext context)`. This is the whole contract change;
   `IContext` keeps every member it has today.
2. The generated handler is always `async`, so `await` is an ordinary expression in page code and in
   a write expression: `ctx.Write(await ctx.SomethingAsync())`.
3. `System.Threading.Tasks.Task` joins the page API policy as a *type*, and the members that start
   work the render cannot stop — or turn an `await` into a block — are denied by name.
4. `LoadedPage.InvokeAsync` awaits the returned task and keeps the rule that the shell only ever
   sees the page's own exception.
5. `Task.Run` and the `Task.WhenAny` deadline stay exactly as they are. Async changes what a waiting
   page costs, not whether a page can be interrupted — it cannot.
6. Abandonment is counted as a *render*, not a thread, in the doc comments and the log line. The
   `/heath` wording already says "renders" and does not change.
7. The page-author prompt is **not** touched here. Its two false sentences are corrected once, by
   fetch and storage, when `await` has somewhere to go.
8. `FormatVersion` and `PolicyVersion` both bump, so no stored image is ever invoked with the old
   protocol.

Nothing a page can do changes at runtime in this release: with no `Task`-returning member owned by
`IContext`, the only tasks a page can reach are already complete — `Task.CompletedTask`,
`Task.FromResult` — so the state machine never suspends and every existing behaviour test is a
regression test for the whole change.

## Goals and non-goals

**Goals**

- A page's I/O is a `Task`. `IContext` gains no synchronous twin, in this change or the next.
- The template language needs no new form: `await` is C# and the expression blocks already take an
  arbitrary expression.
- Page code cannot use the contract to start work outside its render, or to turn an `await` back
  into a blocked thread.
- The shell continues to see the page's own exception, the page's own template line, and a `504`
  when the render is abandoned.
- Every test that proves today's behaviour still proves it, with only signature and wording churn.

**Non-goals**

- Making a synchronous page asynchronous. A page that blocks still blocks, still holds a thread, and
  is still escaped only by abandonment and the container's restart.
- Adding `Storage`, `Fetch` or any other capability. Their contract is the fetch and storage plan's;
  this plan only fixes the shape it must take.
- Removing `Task.Run`, the render slots, the body buffer or the deadline.
- Streaming (ARCH-13), background work, queues, or parallel fetches started beside a render.
- An analyzer for dropped tasks, or any static rule about what a page awaits. A page that drops a
  task is a page bug, documented, not policed.
- Rewriting plans that are records of a past decision (`page-declarations.md`,
  `page-runtime-hardening.md`, `metrics-and-status-dashboard.md`). They keep the `void Handle`
  signatures they were written against.

## The contract

### Today

```csharp
public interface IHandler {
    /// <summary>Runs the page against the request context.</summary>
    void Handle(IContext context);
}
```

```csharp
// generated
using uk.osric.asp.Sdk;
public class GeneratedPage : uk.osric.asp.Sdk.IHandler {
public void Handle(uk.osric.asp.Sdk.IContext ctx) {
...
}}
```

### After this plan

```csharp
public interface IHandler {
    /// <summary>Runs the page against the request context.</summary>
    /// <returns>A task that completes when the page has finished.</returns>
    Task HandleAsync(IContext context);
}
```

```csharp
// generated
using uk.osric.asp.Sdk;
public class GeneratedPage : uk.osric.asp.Sdk.IHandler {
#pragma warning disable CS1998
public async global::System.Threading.Tasks.Task HandleAsync(uk.osric.asp.Sdk.IContext ctx) {
...
#line 12 "page"
}}
#pragma warning restore CS1998
```

A page writes nothing differently today. When fetch and storage land it writes:

```html
<% PageFetchResponse response = await ctx.FetchAsync(new PageFetchRequest("https://example.com/api")); %>
<p><%= response.Body %></p>
```

and inside a write expression:

```html
<p>Visited <%= (await ctx.Storage.TryGetValueAsync<int>("visits")).Count.ToString() %> times.</p>
```

## Decisions

### D1 — The async handler ships on its own, before fetch and storage

Two things are separable here and only one of them is worth waiting for:

- **The decision** — the page surface is asynchronous — must be made before fetch and storage Phase
  1, or the plan is written twice.
- **The implementation** — one interface, one generator, one `await`, one policy rule, one error-page
  mapping — is a day's work that touches nothing page authors write.

So this plan ships the async handler entirely and alone, and the fetch plan is revised to specify
`Task`-returning members from the start. The synchronous members it describes today are never
written, which is the outcome the idea asks for.

The alternative — folding this into fetch and storage Phase 1 — buys one deploy instead of two, and
one lazy recompile instead of two. It costs a bigger change to review, and it parks a contract
decision behind the largest piece of unimplemented work in the backlog. Pages recompile lazily on
their next request after any deploy (see [deployment](../../../deploy/README.md#storage-and-migrations)),
so the second recompile is the same work, not extra work. Not worth it.

### D2 — `Task`, admitted as a type, not the namespace

`System.Threading.Tasks.Task` joins the allow-list as an entry in `s_allowedTypes`, beside
`System.Threading.CancellationToken`. `Task<T>` needs no separate entry: `INamedTypeSymbol.Name` is
`Task` for both arities, and `PageApiChecker.Describe` already reports the containing type's name.

Admitting the *namespace* would hand a page `TaskCompletionSource`, `TaskScheduler`, `Parallel`,
`TaskFactory` and `ValueTask` in one line. Admitting the type hands it `Task` and then the member
rule below decides the rest. `System.Threading.Tasks.TaskCompletionSource<T>`,
`System.Threading.Tasks.TaskScheduler`, `System.Threading.Tasks.Parallel` and
`System.Threading.Tasks.ValueTask` therefore stay `PAGE001` without being named anywhere, which is
the default-deny rule doing its job.

`ValueTask` is rejected deliberately: the generated handler is a coroutine returning `Task`, a page
that met a `ValueTask` would have to `AsTask` it, and a `ValueTask` awaited twice is undefined
behaviour — a footgun with no benefit at these sizes.

A page may still write `<%! using System.Threading.Tasks; %>` in a declaration block: a `using` is
not itself walked, only the symbols the page binds, so `Task` and `Task<int>` resolve and everything
else in the namespace is `PAGE001` at the line that names it. `AUsingDirectiveDoesNotBypassThePolicy`
already proves the rule from the other direction with `System.IO`, and `PageApiPolicyTests` gets the
`System.Threading.Tasks` twin.

### D3 — Member-level denial, as data, with one message

The policy has no member-level rule today: `IsAllowed` tests namespace, then type. Admitting `Task`
by type would hand a page `Task.Run`, which is a thread-pool task the render deadline cannot stop —
the one capability the whole budget design exists to withhold.

So `PageApiPolicy` gains a second frozen set, checked before anything is allowed:

```csharp
// Admitted by type, denied by member: Task is the page's shape for waiting, not a way to start
// work beside the render or to turn an await back into a blocked thread.
private static readonly FrozenSet<string> s_deniedApis = new[] {
    "System.Threading.Tasks.Task.Run",
    "System.Threading.Tasks.Task.Factory",
    "System.Threading.Tasks.Task.ContinueWith",
    "System.Threading.Tasks.Task.Wait",
    "System.Threading.Tasks.Task.WaitAll",
    "System.Threading.Tasks.Task.WaitAny",
    "System.Threading.Tasks.Task.Result",
    "System.Threading.Tasks.Task.GetAwaiter",
    "System.Threading.Tasks.Task.Delay",
    "System.Threading.Tasks.Task.Yield",
}.ToFrozenSet(StringComparer.Ordinal);
```

`IsAllowed` returns `false` for a reference in that set before it consults the type list, and the
diagnostic is one message for the whole set:

```text
PAGE001: pages may not use System.Threading.Tasks.Task.Run; it starts or blocks work the render cannot stop.
```

Two reasons, one sentence, because both come to the same thing: the render's deadline is the site's
only bound, and every entry is a way round it.

Per entry:

| Member | Why denied |
| --- | --- |
| `Run` | A thread-pool task that outlives the render; the fire-and-forget vector on a page with nowhere to put work. |
| `Factory` | Reaches `TaskFactory.StartNew`, which is `Run` with more steps. |
| `ContinueWith` | A continuation that only runs if the page awaits it — and if it does not, it is `Run` again. |
| `Wait`, `WaitAll`, `WaitAny` | Turns a task into a blocked thread, which is what the async surface exists to stop. |
| `Result` | The same, as a property. |
| `GetAwaiter` | The explicit route to `GetResult()`; `await` synthesises this in IL, which the walk never sees, so denying the name costs a page nothing. |
| `Delay` | `Thread.Sleep` by another name. `System.Threading` already denies `Thread.Sleep`; parity matters more than the token-linked form the page could not build anyway (`CancellationTokenSource` is denied). |
| `Yield` | A hop to the thread pool with no I/O behind it: it holds a render slot to do nothing. |

Allowed and expected: `Task.CompletedTask`, `FromResult`, `FromException`, `FromCanceled`,
`WhenAll`, `WhenAny`, and the operators `await`, `is`, `as` and the `==` comparison. `WhenAll` and
`WhenAny` stay allowed so a future page can run two fetches in parallel *inside* its render, which
the deadline still bounds; that is a new capability the fetch plan may price, not a hole.

`await foreach`, `await using`, `IAsyncEnumerable`, `IAsyncDisposable`, `IAsyncStateMachine` and
`AsyncTaskMethodBuilder` need no denial: none of those types is allow-listed, so naming one is
already `PAGE001`.

### D4 — The generated method is always `async`, and CS1998 is suppressed on purpose

`async` is emitted whether or not the page awaits anything. A page with no `await` compiles with
warning CS1998, which lands on the generated signature: unmapped, so `PageCompiler.MapDiagnostics`
drops it today. Relying on that is a trap for whoever maps unmapped warnings next, and the fault
would be invisible — a page that stopped compiling for a reason nobody could see.

So the generator wraps the method in `#pragma warning disable CS1998` / `restore`, and the warning
never exists. Nothing else about the emit changes: the `#line` regions, the column padding, the
mapped closing braces and the write-expression split all stay exactly as they are, so diagnostics
keep landing on the author's line and column.

The return type is emitted fully qualified — `global::System.Threading.Tasks.Task` — so the
generator's prelude keeps its single `using uk.osric.asp.Sdk;`. Adding `using System.Threading.Tasks;`
to the prelude would put the whole namespace in scope for page code, which is precisely what D2
avoids.

### D5 — `LoadedPage.InvokeAsync` awaits, and unwrapping gets simpler

```csharp
public async Task InvokeAsync(IContext context) {
    ArgumentNullException.ThrowIfNull(context);

    object? result;
    try {
        result = _handle.Invoke(Activator.CreateInstance(_handler), [context]);
    } catch (TargetInvocationException ex) when (ex.InnerException is not null) {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
    }

    if (result is not Task page) {
        throw new InvalidOperationException(
            "The page handler did not return a task. This is a compiler fault, not a page error.");
    }

    await page;
}
```

- The `TargetInvocationException` branch is now nearly unreachable — an `async` method hands its
  exception to the task, including one thrown before the first `await` — but it stays as the guard
  it already is, and it keeps the reflection wrapper out of the shell's `catch` blocks.
- `await page` rethrows the page's exception with its own stack, stitched from the portable PDB. No
  `ExceptionDispatchInfo`, no `AggregateException`, no nested frame: the guarantee is unchanged, it
  is just cheaper to uphold.
- The `Task` cast failure is a compiler fault, so it throws instead of returning a diagnostic, which
  is the same rule the checker and `MapDiagnostics` already follow.
- `Dispose` is untouched; the renderer still only unloads after the task completes.

### D6 — A cancelled render is not a page fault

An abandoned async page unwinds as `OperationCanceledException` at its next `await`, and the task
that carries it is *cancelled*, not faulted. The renderer must not turn that into a `500`.

`PageRenderer.RunAsync` gains one branch after the `WhenAny`:

```csharp
if (page.IsCanceled && (deadline.IsCancellationRequested || cancellationToken.IsCancellationRequested)) {
    // The page stopped because we asked it to: the answer was already given, or the visitor left.
    metrics.RecordInvoke(path, false, Stopwatch.GetElapsedTime(invokeStart));
    return RenderResult.TimedOut;
}

await page;
```

- The test is `IsCanceled` **and** one of our two tokens, in that order. A task cancelled by a token
  nobody in this process owns is a page cancelling itself, which is a page bug and gets the error
  page like any other throw.
- `RenderResult.TimedOut` rather than a new status: the request is either already answered with
  `504` or already gone, and `PageEndpoint` does the same thing with it in both cases.
- Nothing else in `RunAsync` moves. The metrics, the `template.Dispose()` in the success path and
  the abandon continuation all stay.

### D7 — `Task.Run` and the `WhenAny` deadline stay; abandonment counts a render

```csharp
Task page = Task.Run(() => template.InvokeAsync(context), CancellationToken.None);
```

`Task.Run(Func<Task>)` returns a proxy that completes when the page's task completes, so the
`WhenAny` race is unchanged and the abandonment path is unchanged.

Keeping `Task.Run` is the whole point: async does not make arbitrary C# interruptible, and a page
can still block synchronously. Running the page off the request thread is what keeps a slow *or*
blocking page from holding the request. What async adds is that a page which is *waiting* returns
its thread to the pool, so an abandoned render that is waiting will unwind as soon as the token
fires instead of holding a thread until the network gives up.

Because the counted thing is now a task that may or may not hold a thread, the wording changes in
exactly three places:

| File | Today | After |
| --- | --- | --- |
| `PageRenderer` class remarks | "the thread is counted against the site's health" | "the render is counted against the site's health" |
| `PageHealth` remarks | "A lost thread cannot be reclaimed" | "A render that never returns cannot be reclaimed" |
| `PageMetrics.RecordAbandoned` doc | "the thread it was using is lost" | "it may still be running" |

`AbandonedRenderHealthCheck`, `page.render.abandoned` and the `/heath` JSON do not change: they
already say "renders". Test assertion messages that read "no thread was lost" become "no render was
abandoned" — the assertions themselves are unchanged.

### D8 — The deadline's token must outlive an abandoned page

`RenderAsync` creates the deadline with `using`, so the `CancellationTokenSource` is disposed when
the call returns — including on the path that abandons the page. Today nothing observes that token
after the answer is given except the `WhenAny`'s own `Task.Delay`, which is already registered.

Once a page can suspend, an abandoned page may reach an `await` *after* the source is disposed and
try to register on that token, which throws `ObjectDisposedException` inside the page instead of
cancelling it. The fetch plan's `SimpleContext.AttachDeadline` makes this reachable.

The fix is a lifetime change, not a new type: `RunAsync` takes ownership of the deadline and
disposes it on every path it completes on — immediately when the page finished, and inside the
abandon continuation when it did not. `RenderAsync` keeps disposing the source on the paths that
never reach `RunAsync` (a dropped queue wait, a read that spent the budget, a missing page, a
compile error).

This plan records the requirement and the shape. The regression test — an abandoned page whose next
`await` is cancelled rather than faulted — cannot be written until a `Task`-returning member exists,
so it belongs to fetch and storage Phase 2 and is listed in [What fetch and storage must
change](#what-fetch-and-storage-must-change).

### D9 — Async frames are named by their declaring type, and deduplicated by line

`ErrorEndpoint.EditorDetails` filters stack frames on `GetFileName() == "page"`, which still works:
the mapped sequence points name the template. What breaks is the label —
`location.Method == "Handle" ? "Handle(IContext ctx)" : "page code"` — because the method is now
`MoveNext` on the generated state machine.

Match the **declaring type** instead of the method name, so the projection carries
`frame.GetMethod()` rather than its name:

```csharp
string method = location.Method?.DeclaringType?.Name == "GeneratedPage"
    ? "HandleAsync(IContext ctx)"
    : "page code";
```

That is both more accurate and more future-proof than a name test: a page-declared type with a
`Handle` method currently gets labelled as the handler, and after this change it will not.

Async frames can also repeat a line — a resumption point and the throwing statement can sit on the
same template line — so the projection gains `.DistinctBy(location => location.Line)` after the
`Line > 0` filter. The author link, the `pre.diagnostics` list and the "no template location is
available" fallback are unchanged.

The rendered text changes from `at Handle(IContext ctx) in /throw:line 3` to
`at HandleAsync(IContext ctx) in /throw:line 3`, and the tests that pin that string change with it.

### D10 — The page-author prompt waits, deliberately

`PageAuthorPrompt` says two things that this change makes false:

- "`<% %>` contains synchronous C# statements."
- "The handler is synchronous and its response is buffered."

Neither will be changed here. With no async member on `IContext`, the prompt that says
"synchronous" produces exactly the pages that work, so the lie is benign for one release — and
changing it now means a prompt version bumped for a wording correction, then bumped again days
later to add `Storage`, `Fetch` and the `await` examples. The idea is explicit that there should be
one bump, not two in quick succession.

The change is written into fetch and storage Phase 4 instead:

- the two sentences above;
- one `await` example in the worked examples;
- one sentence that a dropped task is a bug, because the `Abandon` check refuses later writes but
  cannot stop work already started.

`PageAuthorPromptTests` is untouched by this plan — it reflects over `IContext` to check the contract
section, and `IContext` does not change.

### D11 — Both version stamps bump

| Stamp | Today | After | Why |
| --- | --- | --- | --- |
| `PageCompiler.FormatVersion` | `4` | `5` | The handler is invoked differently: reflect, cast, await. Its own comment names exactly this case. |
| `PageCompiler.PolicyVersion` | `1` | `2` | The policy tightens: ten `Task` members become `PAGE001`. |

The SDK's module version id already covers the interface change, so no page is ever served from an
image built against `void Handle`; the explicit bumps make the reason legible in the runtime id
rather than implicit in an assembly id.

The fetch plan's Phase 1 step 2 bumps `PolicyVersion` to `2` for the `System.Text.Json` types. That
step becomes `2` → `3`.

### D12 — `IContext` members take no cancellation token

`HandleAsync(IContext context)` has no token parameter, and neither will the async members that
follow. The context already owns `Aborted` and, from the fetch plan, the render deadline; an
implementation links them per call. Adding a token per call would be the codebase's token-last
convention applied to a surface where the caller has nothing better to pass than what the context
already holds.

This is a deliberate exception to the convention, so both the SDK doc comments and
[DEC-123](../../decisions.md) say so in as many words.

## Runtime

### `LoadedPage`

| Change | Detail |
| --- | --- |
| Signature | `public void Invoke(IContext context)` → `public async Task InvokeAsync(IContext context)` |
| Lookup | `_handle = _handler.GetMethod("HandleAsync")`; the "no handle method" message follows the name |
| Body | Cast the reflection result to `Task` and await it; see [D5](#d5--loadedpageinvokeasync-awaits-and-unwrapping-gets-simpler) |
| Unchanged | Collectible `AssemblyLoadContext`, dispose semantics, one handler instance per invocation, by-name handler type lookup |

`Activator.CreateInstance(_handler)` still runs per call, so page instance fields still cannot leak
between requests.

### `PageApiChecker`

`HandlerMethodName` becomes `"HandleAsync"`. Nothing else in the checker moves: the shape rule still
requires the generated handler to be the last member and the only one with a body, and an `async`
modifier adds no member, so a page declaring types above it is unaffected.

### `PageRenderer`

Only the invoke line and the cancellation branch of [D6](#d6--a-cancelled-render-is-not-a-page-fault),
plus the deadline lifetime from [D8](#d8--the-deadlines-token-must-outlive-an-abandoned-page) and the
wording table from [D7](#d7--taskrun-and-the-whenany-deadline-stay-abandonment-counts-a-render).

### `SimpleContext`

No member, no behaviour. One comment: "The page's thread cannot be stopped" becomes "The page cannot
be stopped", because a page may now be a task that is not on a thread of its own.

## Diagnostics and the error page

Covered in shape at [D9](#d9--async-frames-are-named-by-their-declaring-type-and-deduplicated-by-line).
What the author sees, unchanged in kind:

- a compile error, at its template line and column, mapped through the same `#line` regions;
- a throw from page code, at its template line, named `HandleAsync(IContext ctx)`;
- a `504` with no body when the render was given up on;
- a `500` from the shell — never the reflection wrapper, never an `AggregateException`.

What is new is that the throwing frame is `GeneratedPage.<HandleAsync>d__0.MoveNext()` and that its
mapped position is the sequence point at the start of the faulting statement. Two test assertions
need care:

- `PageCompilerTests.TemplateExceptionsSurfaceUnwrapped` asserts
  `failure.StackTrace` contains `"GeneratedPage.Handle"` — that becomes
  `Does.Contain("GeneratedPage.<HandleAsync>d__")`. Do not assert the state machine's ordinal
  (`d__0`): it is a compiler implementation detail that moves when the generator emits more members.
- the same test asserts the page frame's line **and** column. Line 3 should survive. The column may
  move to the statement's start rather than the `throw` keyword: assert the line exactly and keep the
  column assertion only if it holds, with the reason recorded in the test.

## Policy

The rule, as data, after this plan:

| Namespace or type | State |
| --- | --- |
| `uk.osric.asp.Sdk` (all) | allowed, as today — now includes `Task` in an `IHandler` signature a page may declare for itself |
| `System.Threading.CancellationToken` | allowed, as today |
| `System.Threading.Tasks.Task` | **new** — type allowed, members per [D3](#d3--member-level-denial-as-data-with-one-message) |
| `System.Threading.Tasks.ValueTask`, `…TaskCompletionSource`, `…TaskScheduler`, `…Parallel` | denied by default; naming one is `PAGE001` |
| `System.Threading` beyond `CancellationToken` | denied, as today — `Thread.Sleep` still fails, which is what makes the `Task.Delay` denial consistent |
| `System.Net`, `System.Net.Http` | denied, as today |

The class doc comment's "deliberately absent, and why" paragraph gains `System.Threading.Tasks` (the
namespace) and keeps `System.Threading` (the namespace) with the `Task` exception named beside it.

## Generated source, before and after

The generator's postlude is load-bearing: the mapped closing brace is what turns an unclosed block
into a positioned author error. Keep it, and put the `#pragma` restore after it.

```text
using uk.osric.asp.Sdk;
public class GeneratedPage : uk.osric.asp.Sdk.IHandler {
#pragma warning disable CS1998
public async global::System.Threading.Tasks.Task HandleAsync(uk.osric.asp.Sdk.IContext ctx) {
ctx.WriteRaw(@"<p>Hello</p>");
#line 3 "page"
      ctx.Write(
#line 3 "page"
        await ctx.VisitsAsync()
#line default
);
#line default
ctx.WriteRaw(@" times.</p>");
#line 4 "page"
}}
#pragma warning restore CS1998
```

Three details of that emit are load-bearing and unchanged from today:

- HTML text is generated, unmapped code (`ctx.WriteRaw(@"…")`); only code, declaration and write
  tokens carry `#line` regions, so the author's file names the page and nothing else.
- A write expression splits the wrapper (`ctx.Write(`) from the expression, so a diagnostic on the
  argument points at the author's expression and one on the wrapper points at the opener. `await`
  inside that split is just part of the expression.
- The closing braces are mapped to one line past the template, which is what turns an unclosed block
  into a positioned author error. The `#pragma` restore therefore goes after them.

## Versions, wiring and configuration

- `PageCompiler.FormatVersion` `4` → `5`, `PolicyVersion` `1` → `2` ([D11](#d11--both-version-stamps-bump)).
- No configuration key, no `appsettings.json` change, no DI registration, no migration.
- `RuntimeId` keeps its five-part shape, so `PageImage.IsCurrent` needs no change.
- `deploy/README.md` needs no edit: it already explains that an SDK or Core change invalidates every
  stored image.

## Documentation

| File | Change |
| --- | --- |
| `docs/architecture.md` — page contract | `Handle` → `HandleAsync` where the return is described; the sentence "status, content type and headers are applied after `Handle` returns"; the `IContext` member list keeps `Aborted` and gains one line that the handler is asynchronous and `await` is available |
| `docs/architecture.md` — page execution budget | "the thread it is using is lost" → the render; the handler "runs on a worker" gains "and may suspend, returning its thread while it waits" |
| `docs/architecture.md` — page API policy | the policy paragraph names the `Task` admission and the member denial |
| `docs/decisions.md` | DEC-123 (async contract, tokenless members) and DEC-124 (member-level denial, amends DEC-35); DEC-31's "after `Handle` returns" becomes `HandleAsync`; two lines under "Open policy questions" |
| `docs/jobs/IDEA-012.md` | deleted into git history; its row moved to the closed table |
| `docs/jobs/README.md` | the IDEA-012 row moved to the closed table, and the rough order dropped its step |
| `docs/plans/page-fetch-and-storage.md` | only on acceptance, and only as listed below |
| `deploy/README.md`, `README.md`, `CONTRIBUTING.md` | no change |

Proposed entries for `docs/decisions.md`:

| ID | Decision | Recorded in |
| --- | --- | --- |
| DEC-123 | The page handler is asynchronous: `IHandler.HandleAsync` returns `Task`, the generated handler is always `async`, and the I/O members `IContext` gains are `Task`-returning and take no cancellation token — the context owns `Aborted` and the render deadline and links them per call. A synchronous twin is never added. | [async page contract](async-page-contract.md) |
| DEC-124 | `System.Threading.Tasks.Task` is admitted to the page API policy as a type, and ten of its members are denied by name — `Run`, `Factory`, `ContinueWith`, `Wait`, `WaitAll`, `WaitAny`, `Result`, `GetAwaiter`, `Delay`, `Yield` — because each starts work the render deadline cannot stop or turns an `await` back into a blocked thread. The namespace stays denied, so `TaskCompletionSource`, `TaskScheduler`, `Parallel` and `ValueTask` remain `PAGE001`. | [async page contract](async-page-contract.md), amends DEC-35 |

## What fetch and storage must change

These are the revisions to [page-fetch-and-storage.md](page-fetch-and-storage.md). They are part of
accepting this plan, and land before that plan's Phase 1 starts.

### The short version, item 4

Replace item 4 with: both capabilities are asynchronous at the page surface because
`IHandler.HandleAsync` is; they run on the page's own task, are bounded by the render deadline, and
stop accepting work once `SimpleContext.Abandon` has been called. Item 5, the `System.Text.Json`
types, is unchanged.

### The page API

```csharp
public interface IPageStorage {
    Task<PageValue<T>> TryGetValueAsync<T>(string key);
    Task SetAsync<T>(string key, T value);
    Task<bool> DeleteAsync(string key);
    Task<IReadOnlyList<string>> KeysAsync();
}

public readonly record struct PageValue<T>(bool Found, T? Value);

public interface IContext {
    // ...
    IPageStorage Storage { get; }
    Task<PageFetchResponse> FetchAsync(PageFetchRequest request);
}
```

`TryGetValue`'s `out` parameter cannot survive: `out` parameters are illegal on an `async` method.
The `PageValue<T>` result keeps plan D1's rule intact — a stored JSON `null` is `Found == true` with
a null `Value`, an absent key is `Found == false` — so the test and the read are still one operation
and there is no has-key/fetch race.

The page snippet in the plan becomes:

```html
<% PageValue<int> stored = await ctx.Storage.TryGetValueAsync<int>("visits");
   int visits = stored.Found ? stored.Value : 0;
   visits++;
   await ctx.Storage.SetAsync("visits", visits); %>
<p>Visited <%= visits.ToString() %> times.</p>
```

### D3 — storage calls

Becomes "asynchronous, per call, and deadline-bound". The body changes as follows:

- EF Core's synchronous calls become `ToListAsync`/`SaveChangesAsync`; the per-call context, the
  immediate commit, the absence of a page-wide transaction and the `SimpleContext.Abandon` check all
  stand.
- The `HEAD` rule, the caps and the last-write-wins rule stand, but a refusal now surfaces from the
  returned task at the `await`, so the SDK docs say "throws when awaited".
- Keep the note that a page which drops the task has dropped a write; it is now a real hazard, and
  the prompt sentence from [D10](#d10--the-page-author-prompt-waits-deliberately) is where it is
  taught.

### D6 — both capabilities

Becomes "both capabilities are asynchronous because the handler is". Its three rejected
alternatives stand except the first ("making `IHandler.Handle` async would change every page"), which
is deleted with one sentence pointing here for why it was wrong: pages are templates, the generated
signature is not written by hand, and the surface was new.

### Wiring

- `PageRenderer` calls `context.AttachDeadline(deadline.Token)` before `RunAsync`, as written — and
  `RunAsync` owns the deadline's lifetime per [D8](#d8--the-deadlines-token-must-outlive-an-abandoned-page),
  so the token is alive for as long as an abandoned page might register on it.
- The `SimpleContext` abandoned checks are read under `Volatile.Read` (or the body lock), as
  written; with an async page the abandoning thread and the page's continuation no longer run on the
  same thread, so the memory-model note becomes a requirement rather than a nicety.

### Phase 1

1. Add the SDK types above — `Task`-returning — plus `PageValue<T>`, with XML docs that say a
   refusal throws when awaited.
2. Add the `System.Text.Json` types to `PageApiPolicy`; bump `PolicyVersion` `2` → `3`.
3. Placeholder `SimpleContext` implementations, as written.
4. Update the `IContext` implementers in the tests, as written — including the `Runtime.Tests`
   `TestContext`, whose new members return `Task.FromResult`/`Task.CompletedTask`.
5. Tests: the contract compiles with `await` in a code block and inside a write expression; the JSON
   types are allowed; `HttpClient` is still `PAGE001`.

### Test matrix and risks

Add rows: an async storage read inside a write expression emits the awaited value; an abandoned page
suspended in a storage or fetch call is cancelled, not faulted, and its task releases the health
count promptly (the [D8](#d8--the-deadlines-token-must-outlive-an-abandoned-page) regression); a
dropped fetch task cannot add a write to a response that has already been sent. In "Risks", the
synchronous-blocking risk becomes "a page that blocks synchronously is unchanged; a page that waits
is now cheap to abandon", and a new risk records that a dropped task is undetectable by the policy.

## Implementation phases

Test-first throughout, one commit per phase, starting from green `make check` on `trunk`. Every new
file carries its SPDX header; `make license-check` is part of the gate.

### Phase 1 — The generated method

1. Write the failing tests in `Core.Tests`: the generated source contains
   `public async global::System.Threading.Tasks.Task HandleAsync(uk.osric.asp.Sdk.IContext ctx) {`;
   a template with no `await` generates source whose `#pragma warning disable CS1998` precedes the
   signature; an empty template still generates.
2. `TemplateSourceGenerator`: add the `TaskTypeName` constant, emit the `async` signature, wrap it in
   the pragma pair, and update the class comment that explains why the SDK type is named explicitly.
3. Update the two assertions in `TemplateSourceGeneratorTests` that spell the old signature, and the
   `#line` ordering test if the postlude moved.
4. Keep `PolicyVersion` and `FormatVersion` for Phase 2/3, where their stated reasons actually happen.

### Phase 2 — The handler contract and the policy

1. `IHandler.HandleAsync` with XML docs, including the note that the host awaits the task and that a
   page must not start work it does not await.
2. `PageApiChecker.HandlerMethodName` → `"HandleAsync"`.
3. `PageApiPolicy`: admit `System.Threading.Tasks.Task`; add `s_deniedApis` and check it first; extend
   the class doc comment.
4. `PageCompiler`: `FormatVersion` → `5`, `PolicyVersion` → `2`.
5. Failing-then-passing tests:
   - `Runtime.Tests` (`PageApiPolicyTests`): a `[TestCase]` per denied member, each asserting
     `PAGE001` and the member's name; `Task.CompletedTask` and a page-declared `async Task` helper
     compile; `Task.Run` and `System.Threading.Thread.Sleep` are `PAGE001`;
     `System.Threading.Tasks.TaskScheduler` and `TaskCompletionSource<int>` are `PAGE001`; the
     `using System.Threading.Tasks;` twin of `AUsingDirectiveDoesNotBypassThePolicy`.
   - `Web.Tests` (`PageScopeTests`): a page that awaits a page-declared async helper compiles through
     the real compiler and renders its awaited value.
6. Every hand-written generated source in `PageCompilerTests` — five literals — must be rewritten as
   what the generator actually emits: `using uk.osric.asp.Sdk;`, then
   `public async global::System.Threading.Tasks.Task HandleAsync(uk.osric.asp.Sdk.IContext ctx)`.
   The stray `using uk.osric.asp.Core;` on those literals goes with them. The decoy template in
   `APageDeclaredHandlerIsNotTheEntryPoint` becomes
   `public Task HandleAsync(uk.osric.asp.Sdk.IContext ctx) => Task.CompletedTask;`.

### Phase 3 — Invocation and abandonment

1. `LoadedPage.InvokeAsync` per [D5](#d5--loadedpageinvokeasync-awaits-and-unwrapping-gets-simpler);
   `PageRenderer` invokes it inside `Task.Run`, per [D7](#d7--taskrun-and-the-whenany-deadline-stay-abandonment-counts-a-render);
   add the cancelled-render branch from [D6](#d6--a-cancelled-render-is-not-a-page-fault).
2. Deadline lifetime per [D8](#d8--the-deadlines-token-must-outlive-an-abandoned-page).
3. Wording in `PageRenderer`, `PageHealth`, `PageMetrics`, `AbandonedRenderHealthCheck` and
   `SimpleContext`; test assertion messages from "no thread was lost" to "no render was abandoned".
4. Tests: `OneImageCanBeLoadedAndInvokedMoreThanOnce` and `TemplateExceptionsSurfaceUnwrapped` await
   `InvokeAsync`; a page whose only statement throws still surfaces unwrapped; a page that returns a
   task nothing awaits still renders its body. `PageRendererTests` keeps every existing abandonment
   assertion and gains one: a page that throws after an `await` is a page fault, not a timeout.

### Phase 4 — The error page

1. `ErrorEndpoint.EditorDetails` labels by declaring type and deduplicates by line
   ([D9](#d9--async-frames-are-named-by-their-declaring-type-and-deduplicated-by-line)).
2. `ErrorHandlingTests`: the pinned string becomes `at HandleAsync(IContext ctx) in /throw:line 3` in
   both the first and the cached assertion; `Does.Not.Contain("GeneratedPage.Handle")` becomes a
   check that no state-machine name leaks; add a case where the throw is after an `await`, asserting
   one line and no duplicate.

### Phase 5 — Docs and bookkeeping

1. Architecture, decisions, job files and the index per [Documentation](#documentation).
2. `docs/plans/page-fetch-and-storage.md` revisions per [What fetch and storage must change](#what-fetch-and-storage-must-change),
   in the same commit as the decision entries so the two documents cannot disagree.
3. On completion, IDEA-012 moves to the closed table in `docs/jobs/README.md` and its file is deleted
   into git history, following the IDEA-004 and ARCH-10 convention.

## Test matrix

| Area | Test | Suite |
| --- | --- | --- |
| Generator | The generated source declares `async … Task HandleAsync(IContext ctx)` | `Core.Tests` |
| Generator | No `await` in the template is still a valid page (pragma present; no warning) | `Core.Tests` |
| Generator | `await` inside `<%= %>` generates the awaited expression inside `ctx.Write(...)` | `Core.Tests`, `Web.Tests` |
| Contract | An empty page compiles and renders | `Runtime.Tests`, `Web.Tests` |
| Contract | A page-declared `async Task` helper compiles and is awaited by page code | `Runtime.Tests` |
| Contract | A page-declared type implementing `IHandler` is not the entry point | `Runtime.Tests` |
| Policy | `Task` is allowed; `Task.CompletedTask`, `WhenAll`, `WhenAny` allowed | `Runtime.Tests` |
| Policy | Each denied member is `PAGE001` at its template position | `Runtime.Tests` |
| Policy | `TaskScheduler`, `TaskCompletionSource`, `Parallel`, `ValueTask` are `PAGE001` | `Runtime.Tests` |
| Policy | `System.Threading.Thread.Sleep` is still `PAGE001` | `Runtime.Tests`, `Web.Tests` |
| Runtime | An exception reaches the caller unwrapped, with the page's line | `Runtime.Tests` |
| Runtime | One image can be loaded and invoked more than once | `Runtime.Tests` |
| Renderer | A page that outruns the budget is abandoned, counted once, and released when it stops | `Web.Tests` |
| Renderer | A page that throws after an `await` is a page fault, not a timeout | `Web.Tests` |
| Diagnostics | The error page names `HandleAsync(IContext ctx)` and the template line | `Web.IntegrationTests` |
| Diagnostics | Async frames on one line are reported once | `Web.IntegrationTests` |
| Regression | Every existing page behaviour test, unchanged in substance | all four suites |

## Risks and open questions

- **State-machine frame names.** `GeneratedPage.<HandleAsync>d__0.MoveNext()` is compiler output.
  Assert the shape, never the ordinal; a test that pins `d__0` will break on an unrelated generator
  change.
- **Async column numbers.** Sequence points move column reporting from the faulting token to the
  statement start. Assert the line; treat the column as advisory until it is proven stable.
- **A denied member that is not reached through `Task`.** `Task.Wait()` on a variable is
  `System.Threading.Tasks.Task.Wait` and is caught. A future BCL type that starts work would need its
  own entry: the list is default-deny because the *type* is allow-listed, not the namespace, so this
  is a small, reviewable exception list, not a growing one.
- **Fire-and-forget is not provable yet.** With no async `IContext` member, no page can obtain a task
  that is still running, so this plan cannot test the guarantee it documents. The test lands with the
  first async capability (fetch and storage), which is the first release where the hazard exists.
- **The deadline's lifetime.** [D8](#d8--the-deadlines-token-must-outlive-an-abandoned-page) is a
  requirement this plan cannot exercise; it is written down so the fetch plan inherits it, and its
  test lands there.
- **No second contract window.** If fetch and storage is written against a synchronous handler after
  all, this plan's whole argument — one breaking change, one policy bump, one recompile — is lost,
  and the async change becomes a rewrite of every page that started using the sync members. That is
  the reason for the ordering, not a preference for small commits.
- **Dropped tasks in async lambdas.** A page can write `items.Select(async item => …)` and drop the
  result; the policy walk sees the lambda's `Task` and allows it. Undetectable without an analyzer,
  and the analyzer is deliberately out of scope.

## Folding the jobs

- IDEA-012 is closed: it moved to the closed table in [the job index](../../jobs/README.md#closed) and
  its file was deleted into git history, following the IDEA-004 and ARCH-10 convention.
- The rough order in `docs/jobs/README.md` no longer carries the async step, because it shipped;
  fetch and storage is next.
- [IDEA-002](../../jobs/IDEA-002.md) and [IDEA-003](../../jobs/IDEA-003.md) are untouched here. Their plan
  changes shape, not scope, and their job files already defer to it.
