<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-13 — A page cannot stream its response

| | |
| --- | --- |
| Severity | Low |
| Area | Core, web shell |
| Location | `src/uk.osric.asp.Core/IContext.cs`, `src/uk.osric.asp.Web/SimpleContext.cs`, `src/uk.osric.asp.Web/Endpoints/MainEndpoint.cs` |

## Problem

`SimpleContext` writes into a `StringWriter` and `MainEndpoint` sends the whole thing once
`IHandler.Handle` has returned. That buffering is not an accident — it is what buys three
guarantees:

- a page that throws sends the error page, not half a document;
- `SetStatus`, `SetContentType`, `SetHeader` and `Redirect` are applied *after* the handler returns,
  so order inside the page does not matter;
- a render that exceeds its budget can be answered `504` with nothing sent, which is the only
  "abort" the [execution budget](../plans/page-execution-budget.md) has.

The cost is that a page which is slow to produce its body cannot send anything early. Every body is
held in memory until it is complete, and there is no way to write a page that starts answering while
it is still working — no progressive rendering, no long-poll, no server-sent events.

No page needs this today: bodies are static and small, and Caddy already streams whatever ASP.NET
writes. This is recorded so the constraint is a decision rather than an oversight, and so that the
first page that wants to stream starts from the right shape.

## Fix outline

1. Add `Task FlushAsync(CancellationToken)` to `IContext` (async, because the write is I/O), and make
   the **first flush the commit point**: status, content type and headers must already be final, so a
   page that has set none of them gets the defaults, and a page that sets them after flushing gets an
   exception rather than a silent no-op.
2. Keep the escaping rule: a streamed `Write` still HTML-encodes, and `WriteRaw` stays the escape
   hatch. Streaming must not become a new way to get unescaped output past the allow-list.
3. Leave the buffering path exactly as it is for pages that never flush. All-or-nothing, apply-after-
   run and a clean `504` remain the default behaviour; streaming is opt-in per page, at the point of
   the first flush.
4. Say plainly in the docs that a timeout after the commit **truncates** the response instead of
   answering `504`, and that `HEAD` still writes no body. This is the guarantee the buffer was
   protecting, and it is the real cost of the feature.
5. Keep the body cap for buffered pages; for a streamed page the cap becomes a per-flush limit or
   disappears, because the point is that the body does not live in memory. Decide it in the issue
   that implements this, not here.
6. Consider whether `MainEndpoint` should ask for the response to be sent early (an explicit
   `IHttpResponseBodyFeature.StartAsync` / disable buffering) or whether Kestrel's own buffering is
   enough once the page flushes. Measure before choosing; the honest default is to let the page's
   flush be the trigger and leave Kestrel alone.
7. Tests: a flushing page delivers bytes before the handler returns; a page that sets a header after
   flushing fails; a buffered page behaves exactly as it does today; a streamed page that throws mid-
   body yields a truncated response and a logged error; the deadline still ends the request.
8. Update `docs/architecture.md`'s "Page contract" section, which currently states flatly that the
   body is buffered, and the README's page writing notes.
