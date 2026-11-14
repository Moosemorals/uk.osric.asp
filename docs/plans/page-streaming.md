<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Pages can stream

| | |
| --- | --- |
| Status | Approved 2026-11-14; implementation in progress on `page-streaming` |
| Size | Medium — one contract member, one response sink, a commit rule, a disconnect guard and their tests |
| Spike | None. `SimpleContext` already buffers through one writer, the endpoint already shapes the whole response in one place, and the render already owns a deadline and an abandonment switch |
| Decisions | Named in prose below; numbers are taken from `trunk` when the work merges (`DEC-09`, DEC-281) |
| Backlog | [ARCH-13](../jobs/ARCH-13.md) — a page cannot stream — and BUG-17's one loose end, the failed-request line a mid-run reload leaves behind |

## The short version

A page can now opt into writing its response while it runs:

> **`await ctx.FlushAsync()` commits the response and sends what has been written.**

The first flush is the whole of the opt-in and the whole of the commit point. It fixes the status,
content type and headers, sends the body written so far, and starts a chunked response. Later writes
are buffered until the next flush, and whatever is still buffered when the handler returns is sent
then. A page that never flushes is exactly the page the site has today: buffered, wrapped, and
answerable with a clean error or `504`.

Four consequences follow, and each is deliberate:

- A flushing page must be served alone. It clears `IPageLayout.Layout` and writes its own document;
  a flush while a layout is still named is refused, because a layout needs the complete body before
  it can start.
- After the commit, the response is final. `SetStatus`, `SetContentType`, `SetHeader`, `AllowScripts`,
  `Redirect` and reading `AntiforgeryField` throw rather than silently doing nothing.
- A failure after the commit — an exception, or the render deadline — truncates the response and
  aborts the connection. It cannot become an error page or a `504`.
- A streamed page still holds its render slot and its deadline. This change makes the shape right; it
  does not make a long-lived server-sent-event stream safe. That is [IDEA-025](../jobs/IDEA-025.md)'s
  plan, which is the first customer.

BUG-17's loose end closes here because it is the same write path. A response that is already
committed can no longer accept the error page, so a client that leaves mid-write must not be reported
as an unhandled exception. A small guard around the endpoint pipeline recognises a disconnect, logs it
at Debug and aborts quietly; a genuine failure on a live response is still an Error.

## Goals

1. A page can send part of its body before its handler returns, without a new template concept and
   without a second output member beside `Write` and `WriteRaw`.
2. The buffered path is unchanged in behaviour: all-or-nothing, status and headers applied after the
   handler, a clean error page for a throw and a clean `504` for the deadline.
3. The escaping rule survives the new path: a streamed `Write` is HTML-encoded and `WriteRaw` remains
   the explicit escape hatch.
4. The response's shape — status, content type, allow-listed headers, the script policy, `no-store`,
   the session cookie and the antiforgery cookie — is applied once, by the same code as today.
5. A committed response that cannot be completed is honestly truncated, and the journal says why.
6. A client that disconnects during a write is a normal end, not an unhandled exception.
7. The author manual, the page contract and the README say what a page author may rely on.

## Non-goals

- **Streaming through a layout.** A layout needs the finished body before it can write its prefix, so
  the chain cannot wrap a stream. Supporting a streaming layout would be a second protocol; it is not
  needed by the first customer and is not attempted here.
- **Lifting or replacing the render deadline.** A streamed page keeps `Pages:Render:TimeoutSeconds`
  and its slot. The deadline is what makes the "truncate, do not `504`" rule meaningful.
- **Releasing the render slot after the commit.** The page is still running and still holding a
  deadline; pretending the render is over would let a public SSE page exhaust the site. This is
  [IDEA-025](../jobs/IDEA-025.md)'s problem.
- **A demo page or a seeded example.** No stored page wants to stream yet, and an example that sleeps
  between flushes would need a delay primitive the page API deliberately does not offer. The tests and
  `/help` are the worked example.
- **A progress bar, a websocket, or a second protocol.** This is HTTP response streaming: progressive
  HTML, long-poll and server-sent events, all written with the ordinary members.
- **A new metric.** The existing `page.render.duration`, `page.render.abandoned`, the page log and the
  access log already describe the request. A `page.render.truncated` counter can be added when a
  dashboard wants it; adding one now means the metric catalogue, its tests and `docs/metrics.md`
  for a feature with no customer.
- **A second way to set response headers after the commit.** The commit is final; there is no "late
  header" escape hatch.

## What exists today

Checked against the code on 2026-11-14.

- **One buffered context.** `SimpleContext` collects the page's writes in a `StringWriter`, its status,
  content type, headers and redirect, and applies them only in `PageEndpoint.WritePageAsync` after
  `IHandler.HandleAsync` returns. Every write is under one lock and charged to a shared `BodyBudget`
  ([SimpleContext.cs](../../src/uk.osric.asp.Web/Pages/SimpleContext.cs),
  [PageEndpoint.cs](../../src/uk.osric.asp.Web/Pages/PageEndpoint.cs)).
- **One response-shaped path.** `WritePageAsync` sets the status, content type, page headers,
  `no-store`, removes `ETag`, writes the session cookie, applies `AllowScripts` and then writes the
  encoded body once. A redirect is written instead of a body, and `HEAD` keeps the length but writes
  nothing.
- **One chain.** The content renders first into a buffer; `PageChain.WrapAsync` then renders each
  layout with the previous layer's body as `IPageLayout.Body`. A redirect, a non-HTML response, a
  cleared layout or a page with no code is served alone
  ([PageChain.cs](../../src/uk.osric.asp.Web/Pages/PageChain.cs),
  [shell.md](../shell.md#the-asset-response)).
- **One budget.** `PageRenderer.OpenSessionAsync` takes one slot and one deadline per request and every
  layer renders under it. A page that outruns the deadline is abandoned: `SimpleContext.Abandon` stops
  its writes, the renderer logs and counts it, and the endpoint answers `504`
  ([PageRenderer.cs](../../src/uk.osric.asp.Web/Pages/PageRenderer.cs),
  [pages.md](../pages.md#page-execution-budget)).
- **One contract rule for I/O.** Every I/O member on `IContext` and every capability returns a `Task`
  and takes no cancellation token; the render owns `Aborted` and the deadline and links them per call
  (DEC-123). ARCH-13's sketch of `FlushAsync(CancellationToken)` predates that rule.
- **One loose end from BUG-17.** The assistant's server-sent-event write can still throw
  `NotSupportedException` when the browser has gone, and the framework's exception handler logs that
  as an unhandled exception. Nothing user-visible breaks, but the journal gains a failed request for a
  run that is still working (ARCH-13, BUG-17).
- **One public contract surface.** `Sdk` is API-analysed, so the new member is an explicit edit to
  `PublicAPI.Unshipped.txt`; adding it changes the `Sdk` module version id, so every stored image is
  recompiled on its next request (DEC-26).

## The design

### 1. The contract: `Task FlushAsync()`

`IContext` gains one member:

```csharp
public Task FlushAsync();
```

It takes no cancellation token, because DEC-123 makes the render the owner of `Aborted` and the
deadline. The implementation links both per call, exactly as `IPageStorage` and `IHttpFetch` do. The
alternative — ARCH-13's `FlushAsync(CancellationToken)` — would be the only member on the contract
that asks a page to supply half of the render's own lifetime, and two token conventions is worse than
one.

The member is deliberately not a capability. Streaming is part of shaping the response, beside
`Write`, `SetStatus` and `SetHeader`; it is not an optional service a render might withhold. A page
that never calls it meets nothing new.

`FlushAsync` is a normal `await`able in a statement block or an output expression, like every other
I/O member.

### 2. The first flush is the commit point

`SimpleContext` gains a small internal sink, supplied by `PageEndpoint`, and a commit state shared
with the page-scoped capabilities. On the first flush, under the body lock:

1. If the render is abandoned, return without writing; an abandoned page's output is discarded.
2. If the page redirected, throw: a redirect has no body to stream.
3. If the page would be wrapped — its content type is HTML and `IPageLayout.Layout` still names a
   layout — throw a message naming the page and telling the author to clear the layout and write its
   own document.
4. Take the buffered text, mark the commit state, and clear the buffer before awaiting.
5. Hand the snapshot and `ToResponse()` to the sink.

The sink applies the response exactly as `WritePageAsync` does today: status, content type, the page's
allow-listed headers, `no-store`, `ETag` removal, the session cookie, the script policy, then
`HttpResponse.StartAsync`. The explicit `StartAsync` is the commit: it runs `OnStarting` (which writes
the shell's security headers), sends the headers, and fixes the response. The sink does not call
`IHttpResponseBodyFeature.DisableBuffering`; Kestrel already sends what it is given, and a test proves
that the endpoint asks for the start before its first body write.

Subsequent flushes write the buffer and flush the response stream. They do not reapply headers. The
first flush commits even when the buffer is empty, which is what a page that wants to send headers
before its first event needs.

When the handler returns with a committed response, the endpoint writes whatever is still buffered and
finishes. There is no `Content-Length` on a streamed response, because its length is not known when
the headers go; HTTP/1.1 uses chunked encoding and HTTP/2 uses frames. `HEAD` keeps the same commit
path and discards every write, so a streamed `HEAD` has no body and no `Content-Length`. That is the
honest trade: a buffered `HEAD` can keep the representation length; a streamed one cannot.

### 3. After the commit, the response is final

These throw `InvalidOperationException` with a sentence naming the page and the first flush:

- `SetStatus`, `SetContentType`, `SetHeader`, `AllowScripts`;
- `Redirect` and therefore `RedirectToLogin`;
- reading `AntiforgeryField`.

`AntiforgeryField` is included because asking for it after the commit either mints a token whose cookie
can no longer be sent or returns a field the browser cannot verify. A page that needs a form must ask
for the field before its first flush.

The page session has the same problem, but it is narrower: a session write after the commit is fine
when the visitor already presented a session cookie, because no header is needed; a *first* write
after the commit would mint a row whose cookie cannot be sent. `PageSessionScope` refuses that case
with the same kind of sentence. The row is not minted, so an abandoned commit does not leave an
orphan.

Writes themselves stay permissive: `Write` and `WriteRaw` after a flush go into the next buffer. That
keeps the model simple — the page writes, then flushes what it wrote — and preserves the escaping and
budget rules. Raw output remains raw and escaped output remains escaped across a flush; streaming is
not a new way past the HTML encoder.

### 4. A flushing page is served alone

A page that flushes while HTML and still wrapped is refused, not silently unwrapped. Dropping the
layout on the first flush would mean a page that set a title, added a head element or a sheet suddenly
loses all of it with no signal; refusing makes the author write the opt-out that the contract already
has:

```csharp
layout.Layout = null;   // this page writes its own document
await ctx.FlushAsync();
```

The check mirrors `PageChain.WrapAsync` exactly: HTML *and* a layout. A stylesheet, script, JSON or
text page is never wrapped, so it may stream with `IPageLayout.Layout` untouched. A layout layer may
never flush: it is chrome, it renders after the content, and it has no sink.

If a page flushes and then names a layout anyway, the endpoint serves the committed response and logs
a warning naming the page and the layout it ignored; the bytes are already on the wire, so wrapping
would be a second document. The page's own `IPageLayout.Body` was empty in the content layer, so
nothing is lost beyond the chrome the page chose not to have.

The chain is skipped entirely for a committed response. `PageEndpoint` asks `SimpleContext` whether it
committed; if so, it completes the tail and returns, never calling `PageChain.WrapAsync` or
`WritePageAsync` again.

### 5. Failure, timeout and the body cap

**An exception after the commit.** `PageEndpoint` catches a render exception when the context is
committed. It cannot write an error page: the headers and possibly half a document are already sent.
It logs an Error naming the page and the exception, aborts the connection and returns. A client sees
a truncated response, not a `500`; that is the cost of streaming, and the page author asked for it.

**The deadline after the commit.** `PageRenderer` returns `RenderStatus.Timeout` exactly as it does
today and has already called `Abandon`; the endpoint sees the committed context, logs a Warning that
the response was truncated and aborts. It does not call `WriteTimeoutAsync`, because a `504` cannot
replace headers that are already sent. The existing `page.render.abandoned` counter and the renderer's
own log line remain the audit trail.

**Before the commit, nothing changes.** A page that throws before its first flush has written only to
the buffer, so it is answered by the error page; a deadline before the first flush is answered `504`.
The buffering guarantee survives for every page that does not opt in, and for the part of a streaming
page's life before its first flush.

**The body cap.** `Pages:Render:MaxBodyChars` stays exactly as it is for a buffered page: the whole
body is charged once. Once a page has committed, the cap no longer applies to its writes. That is
deliberate: the point of streaming is that the body does not live in memory, and a total cap would
stop a streamed response at the buffered limit. The cost is that a page that writes in a loop after
its first flush can still grow the buffer without bound; the render's deadline is the only bound left,
and the container's memory limit is the backstop it already is. Before the first flush the cap still
applies, so a page cannot buffer an unbounded body on its way to committing. The
`RenderOptions.MaxBodyChars` documentation says which side of the commit it binds.

### 6. BUG-17's failed request

A committed response has no room for the exception handler's error page. When the client has gone,
the body write throws `NotSupportedException` (or an I/O exception), the exception handler logs it as
an unhandled exception, and the journal records a failed request for a run that is still working.

The fix is one shared predicate and one guard:

- `ResponseWrite.IsDisconnect(context, exception)` is true when the response has started **and** the
  exception is one a broken or disposed body produces (`NotSupportedException`, `IOException`,
  `ObjectDisposedException`), or when the request is already aborted and the exception is a
  cancellation. It is deliberately about the write, not about the run loop.
- `DisconnectGuard` is a small middleware registered *after* `UseExceptionHandler` and before the
  status-code pages. It catches a disconnect-shaped failure, logs at Debug (the request scope already
  carries the method and path), aborts the connection and does not rethrow. Anything else is rethrown
  to the exception handler, whose behaviour is unchanged.
- `PageEndpoint`'s committed-exception branch uses the same predicate: a disconnect is Debug and an
  abort; a page fault is Error and an abort.

The ordering is the load-bearing part: `UseExceptionHandler` logs before it invokes its branch, so the
guard has to sit inside it to catch first. The integration suite's `ErrorHandlingTests` already pins
the opposite case — an uncaught exception *must* still be logged at Error with the request scope.

The assistant's stream keeps using `TypedResults.ServerSentEvents`; it does not need a second writer.
The guard is the write path both the page and the shell's own stream pass through. That also means a
buffered page whose final `WritePageAsync` races a disconnect gets the same quiet treatment, which is
the same bug in a second place.

## Decisions this plan proposes

These are proposals; the numbers are taken from `trunk` at merge and every citation in the branch is
updated then (DEC-09, DEC-281).

**D1 — `FlushAsync` takes no cancellation token.** The render owns the lifetime, as DEC-123 says.
ARCH-13's sketch predates DEC-123 and is not followed. Rejected: a token parameter that no other
contract member has.

**D2 — streaming is opt-in at the first flush.** A page that never flushes keeps the buffered,
all-or-nothing behaviour. Rejected: making every page stream (loses the error page and the clean
`504`) and an automatic rule keyed on content type (implicit, and surprising for an ordinary HTML
page whose body is slow to produce).

**D3 — a flushing page is served alone.** The first flush is refused while the page would be wrapped;
the page clears `IPageLayout.Layout` and writes its own document. Rejected: silently dropping the
layout (a title and head vanish with no signal) and streaming through a layout (a second protocol).

**D4 — the first flush commits, and the response is final after it.** Status, content type, headers
and the script policy are set before `StartAsync`; response-shaping members and `AntiforgeryField`
throw after it. Rejected: late headers (the framework cannot send them) and silent no-ops (the author
cannot discover the mistake).

**D5 — a committed failure is a truncation.** An exception logs at Error and aborts; the deadline logs
at Warning and aborts; neither attempts an error page or `504`. Rejected: buffering the whole response
"just in case" (the feature would do nothing) and closing cleanly without aborting (a truncated HTML
document would look complete to a reader).

**D6 — the body cap stops at the first flush.** Before the commit, `Pages:Render:MaxBodyChars` still
caps the buffer, so a page cannot commit an unbounded body. After the commit, writes are not charged:
the body is already on the wire and a total cap would defeat the feature. Rejected: a per-flush cap
(tempting, but it would still turn a long stream into a failed stream at an arbitrary chunk size) and
a total cap (a streamed page could never exceed the buffered limit). The owner accepted the residual
risk: a post-commit loop can grow the buffer until the deadline or the container's memory limit stops
it.

**D7 — a first session write after the commit is refused; a write with an existing session cookie is
allowed.** Rejected: minting a session whose cookie cannot be sent, and disabling the session for
every streamed page (it would make streaming incompatible with a stateful page for no reason).

**D8 — a client disconnect is not an unhandled exception.** A shared guard logs it at Debug and
aborts; genuine failures still reach the exception handler. Rejected: catching in the assistant's run
loop (the run is still working; the write is what failed) and catching only in the SSE endpoint (the
page sink has the same write).

**D9 — no new metric in this step.** The existing render duration, abandonment counter and logs are
the evidence; a truncation counter waits for a dashboard that wants it. Rejected: a metric nobody
reads and a catalogue change for every future streaming detail.

## Phases

Each phase is test-first. The branch is `page-streaming`; this plan is written on `arch-13` and waits
for the owner before any code is written (DEC-281).

### Phase 0 — pin today's behaviour

Add only the tests that are missing for the guarantees streaming must not break: a buffered page's
status, headers, `Content-Length`, clean error page and clean `504`; `HEAD` retaining the length;
`MaxBodyChars` refusing the whole buffer. Existing `PageEndpointTests`, `PageLayoutChainTests` and
`SimpleContextTests` already cover most of this; Phase 0 fills the holes rather than duplicating them.

### Phase 1 — the contract and the buffered sink

- Add `Task FlushAsync()` to `IContext` and its XML documentation; add the signature to
  `PublicAPI.Unshipped.txt`.
- Add the internal sink and commit state; wire them through `PageEndpoint` and `PageChain`.
- Make `SimpleContext` write through the sink on first flush and subsequent flushes, without changing
  the buffered path.
- Refuse the response-shaping members, `AntiforgeryField` and a first session write after the commit.
- Prove escaping across a flush, default status/content type, the empty first flush, and the tail sent
  at completion.

### Phase 2 — the wrapping rule

- Refuse a flush while the page would be wrapped; give the message the layout key and the remedy.
- Let a non-HTML page stream with the layout value untouched.
- Make a layout's flush impossible and make a post-commit layout name log and serve alone.
- Prove the chain is never entered for a committed response.

### Phase 3 — failure and the body cap

- Catch a committed exception at the endpoint, log Error and abort.
- Handle `RenderStatus.Timeout` after the commit as a Warning and an abort, not a `504`.
- Stop charging the body budget once the page has committed, and document the boundary in
  `RenderOptions.MaxBodyChars`.
- Prove the cap still refuses an oversized body before the first flush, and that a streamed body past
  `MaxBodyChars` is accepted once the page has committed.
- Prove `HEAD` sends no body and no `Content-Length`.

### Phase 4 — the disconnect guard

- Add `ResponseWrite.IsDisconnect` and `DisconnectGuard`.
- Register the guard after `UseExceptionHandler` and before the status-code pages.
- Use the same predicate in the endpoint's committed-exception branch.
- Prove a disconnect is Debug and quiet; prove a live exception still reaches the error handler.
- Attempt an integration proof with the assistant's real SSE path (a stalled scripted provider and a
  heartbeat), and keep the unit proof as the deterministic one if `TestServer` does not reproduce
  Kestrel's body-disposal exception.

### Phase 5 — documentation and the job

- `docs/pages.md`: the page contract, the streaming rules, the deadline's truncation and the body cap
  stopping at the first flush.
- `docs/shell.md`: the third response shape and the chain rule.
- `docs/testing.md`: the new test files and what each suite owns.
- `README.md`: one paragraph in the page-writing notes.
- The seeded `/help`: `FlushAsync` in the context list and a "Streaming a response" section with a
  progressive-HTML example, the layout opt-out, the commit rule, the deadline, `HEAD`, the cap and the
  session/antiforgery caveat.
- `docs/jobs/ARCH-13.md`: deleted; its row leaves the index, joins "Closed" with a pointer here, and
  the programme marks step 7 done. BUG-17's closed row loses its "one thing left" sentence.
- `docs/decisions.md`: the D1–D9 entries, numbered from `trunk` at merge.

## Tests

| Suite | Test | Proves |
| --- | --- | --- |
| Web | `AFlushingPageWritesBeforeTheHandlerReturns` | The first chunk is in the response body while the page is still awaiting, not only when it finishes |
| Web | `TheFirstFlushAppliesTheDefaultStatusContentTypeAndHeaders` | A page that set nothing gets `200`, the key's content type, `no-store` and the shell's security headers |
| Web | `TheFirstFlushAppliesWhatThePageSet` | Status, content type, an `X-` header and `AllowScripts` are on the response before the handler returns |
| Web | `AnEmptyFirstFlushCommitsTheHeaders` | A page can start a response before its first event |
| Web | `AFlushingPageIsRefusedWhileItIsWrapped` | The message names the page and the layout, and the error names the layout opt-out |
| Web | `ANonHtmlPageMayStreamWithALayoutNameUntouched` | The wrapping check is HTML-and-layout, not layout alone |
| Web | `APageThatClearsItsLayoutStreamsWithoutChrome` | The committed page is served alone and the chain is never entered |
| Web | `ALayoutCannotFlush` | A layout's flush is refused as a page fault, not written into the document |
| Web | `APageThatNamesALayoutAfterFlushingIsServedAloneAndWarned` | The post-commit layout is ignored with a warning, and no second document appears |
| Web | `AResponseSettingAfterTheFlushThrows` | `SetStatus`, `SetContentType`, `SetHeader`, `AllowScripts`, `Redirect` and `RedirectToLogin` each refuse |
| Web | `AntiforgeryFieldAfterTheFlushThrows` | The token cannot be minted after the headers are gone |
| Web | `AFirstSessionWriteAfterTheFlushIsRefused` | No orphan row and no form that cannot be verified |
| Web | `ASessionWriteWithAnExistingCookieAfterTheFlushIsAllowed` | The refusal is about the missing cookie, not about streaming |
| Web | `AStreamedWriteIsStillHtmlEncoded` | `Write("<b>")` arrives as `&lt;b&gt;`; `WriteRaw` stays raw |
| Web | `TheTailAfterTheLastFlushIsSentAtCompletion` | A page need not flush its final bytes |
| Web | `AnExceptionAfterTheFlushTruncatesAndLogs` | The prefix is the body, the status is the committed one, the log is Error and the connection is aborted |
| Web | `ATimeoutAfterTheFlushTruncatesInsteadOfAnswering504` | The status stays committed, the prefix is the body and no error page is written |
| Web | `AnExceptionBeforeTheFlushIsAnErrorPage` | The buffered guarantee is unchanged for a streaming page that never committed |
| Web | `ABufferedPageKeepsItsContentLength` | The ordinary path still sends one body with a length |
| Web | `AStreamedPageHasNoContentLength` | The response is chunked/framed and does not claim a length |
| Web | `AStreamedHeadWritesNoBodyAndNoLength` | `HEAD` commits but sends nothing and does not claim a length |
| Web | `TheBodyCapStopsAtTheFirstFlush` | An oversized body before the commit is still refused; a streamed body past `MaxBodyChars` is accepted once the page has committed |
| Web | `DisconnectGuardTests.AClientDisconnectIsLoggedAtDebugAndNotRethrown` | The guard catches the disconnect-shaped write failure inside the exception handler's scope |
| Web | `DisconnectGuardTests.ALiveExceptionStillReachesTheHandler` | The guard does not swallow a real failure |
| Integration | `PageStreamingTests.AStreamedPageIsServedWithoutTheLayout` | The real host applies the commit and the chain rule together |
| Integration | `PageStreamingTests.AStreamedPageTruncatesOnAnException` | The real host does not replace a committed response with an error page |
| Integration | `PageStreamingTests.AStreamedHeadHasNoBody` | The real host's `HEAD` behaviour |
| Integration | `AssistantDisconnectTests` (best-effort) | Disposing the SSE response mid-run leaves no unhandled-exception entry; if `TestServer` cannot reproduce Kestrel's disposal, the unit guard test is the proof and this test is removed rather than left flaky |

The page-driven tests live in `test/uk.osric.asp.Web.Tests/PageStreamingTests.cs` and drive
`PageEndpoint.HandleAsync` with a stored page and a `DefaultHttpContext`, as `PageLayoutChainTests`
does. The response body is a `MemoryStream` the test can read while the page is still running; the
page signals the test with a `TaskCompletionSource` rather than sleeping, so there is no timing
assertion. The guard tests build the middleware with an `ApplicationBuilder` and a `DefaultHttpContext`
whose body throws, as `SecurityHeadersTests` builds its feature.

## What is deliberately not changed

- **`PageRenderer`'s slot, deadline, abandonment, health count and metrics.** A streamed page is still
  a render; the truncation rule is the only new outcome. A long-lived stream holding a slot is a
  problem for IDEA-025, not something to paper over here.
- **The `504` path for a buffered page.** The existing tests stay green and are the proof.
- **The response header allow-list, the content-type map, the script policy, `no-store`, the session
  and antiforgery cookies.** They are applied by the same helper as today; the sink is a second caller,
  not a second policy.
- **The page API policy, the load context and the compiler.** No new namespace, type or banned
  construct.
- **The shell's own SSE endpoint.** It keeps `TypedResults.ServerSentEvents`; it gains the guard, not
  a new writer.

## Risks

- **A streamed page loses the site chrome.** It must write its own document, so the site's look, menu
  and skip link are the author's job. This is the same choice a page with no code already makes, and
  `/help` says so. The first customer may want a streaming layout; that is a later plan.
- **A streamed page holds a render slot and a deadline.** With the shipped two slots and five-second
  deadline, a stream is short and concurrency is limited. This is intentionally not solved here. An
  owner who sets `Pages:Render:TimeoutSeconds` to its maximum gets a sixty-second stream and no more.
- **A committed exception cannot be explained to the reader.** The client sees a truncated document,
  not the error page. The journal has the Error line with the page's key; the access log has the
  truncated request. That is the honest cost of the feature and the reason streaming is opt-in.
- **A post-commit loop has no body cap.** The owner chose no cap once streaming begins, so a page that
  writes without flushing after its first flush grows the buffer until the render deadline fires or
  the container's memory limit does. The deadline and the container remain the backstop; there is no
  per-flush limit to soften it.
- **`NotSupportedException` after a commit is ambiguous.** A page that deliberately throws one on a
  started response will be treated as a disconnect (Debug, abort) rather than a page fault (Error,
  abort). The alternative is misclassifying the framework's client-disconnect signal. The guard
  requires the response to have started and the exception to be a body-write exception, so the
  ambiguity is narrow.
- **`TestServer` is not Kestrel.** The integration suite can prove the body and the truncation, but
  only a real socket proves that a browser sees the first chunk before the handler returns. The unit
  test proves `StartAsync` is called and the bytes are written early; the gate's manual check should
  use `curl -N` against `make run` if the owner wants socket-level evidence.
- **A contract bump recompiles every stored page.** Adding a member changes `Sdk`'s module version id,
  so every stored image is stale on its next request (DEC-26). Expected, and the same cost the last
  contract changes paid.
- **Two ways to be a response.** There are now three response shapes — buffered, asset, streamed — and
  `docs/shell.md` must say which is which. The code keeps one shaping helper so the shapes cannot
  drift in headers or cookies.

## Settled at review

The owner answered on 2026-11-14.

| Question | Answer |
| --- | --- |
| Should a page that flushes while wrapped be refused, or should the first flush silently drop the layout and serve the page alone? | Refuse, and name the layout and the `Layout = null` remedy. |
| Should a committed failure abort the connection, or end the response cleanly at the truncation point? | Abort. A cleanly ended HTML response looks complete; an aborted one tells the client it is not. |
| Should the body cap cover the whole streamed body? | No. The cap stops at the first flush; a streamed body is not charged. The residual risk is accepted (see Risks). |
| Should a streamed page keep the render deadline and slot, leaving long-lived SSE to IDEA-025's plan? | Yes. Lifting either here would let one public page exhaust the site. |
| Should a disconnect be handled by a pipeline guard as well as the page sink? | Yes; it is the same write path BUG-17 records, and it closes the failed-request line for the assistant too. |
| Is `FlushAsync()` the right member name? | Yes. It matches the write-then-flush model and the job's own name. |

## Status

**Approved 2026-11-14; implementation on `page-streaming`.** The decisions above take numbers from
`trunk` at merge, every citation in the branch is updated then, `ARCH-13` leaves the job index, and
step 7 of the programme is recorded as done (DEC-281).
