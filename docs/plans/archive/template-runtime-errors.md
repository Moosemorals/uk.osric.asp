<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Template runtime errors: authenticated diagnostics in template space

Implementation plan for showing a useful location when page code throws at runtime, without exposing
implementation details to anonymous visitors or requiring them to sign in.

## Status

Implemented on 2026-11-03. `make test` and `make format-check` pass.

## Goal

When a page handler throws, a signed-in editor should be able to identify the failure in the page
they wrote: the page path, template line and column, exception type and message, and a concise trace
that stops at template code. An anonymous visitor should continue to receive the existing generic
500 page. Handling errors must not redirect either visitor to login, make a page route private, or
make `/error` a public route.

## Current behaviour

- `TemplateSourceGenerator` already wraps code blocks in `#line` directives named `page`, mapping
  compiler diagnostics to template coordinates.
- `PageCompiler` emits only the PE image. `CompiledPage` loads that image without symbols, so runtime
  stack frames cannot reliably carry the mapped template line and column.
- The assembly image is stored in `PageCompilation` and the `Files.Image` column. A cache hit reuses
  the image, so symbols must follow the same cache and storage lifecycle.
- `UseAuthentication()` runs for all requests, while endpoint authorization is opt-in. The production
  exception-handler branch can inspect `HttpContext.User` after downstream authentication has run.
- The production `ErrorEndpoint` currently displays a generic message for unexpected failures and
  displays the message of a deliberate `UserFacingException`. It does not expose a stack trace.

## Security and response rules

1. A request with a valid authenticated principal may receive template diagnostics. The existing
   authentication cookie validation, including the security-stamp check, remains authoritative.
2. An anonymous request receives the same generic unexpected-error message as today. Do not challenge,
   redirect, or ask the visitor to sign in to view the error response.
3. Keep `UserFacingException` behaviour unchanged: its message is intentionally safe for visitors.
   Do not append runtime frames to that public message.
4. Never show generated C# coordinates, `GeneratedPage.Handle`, runtime/framework frames, file-system
   paths, assembly names, or raw PDB details. If template symbols are unavailable, omit the template
   trace rather than falling back to generated or host frames.
5. Encode the page path, exception type/message, and formatted trace as HTML. Mark personalized error
   responses `Cache-Control: no-store` so an intermediary cannot serve an editor's diagnostic page to
   another visitor.
6. Preserve full server-side exception logging independently of the filtered editor display. The
   editor-facing trace is not a replacement for operational logs.

## Design

### Portable symbols travel with the compiled image

Emit a portable PDB in `PageCompiler` alongside the PE image. Extend the compile result and
`PageCompilation` with nullable symbol bytes, and pass both streams to the symbol-aware
`AssemblyLoadContext.LoadFromStream` overload in `CompiledPage`. Keep the existing `#line` filename
`page`; use the original request path from the exception-handler feature as the displayed page name.

Persist symbols as a nullable BLOB on `StoredFile`, using a new EF migration, and update the whole-row
save and compilation-update paths in `SqlitePageStore` so image and symbols always belong to the same
source revision. A null symbol column remains valid for old rows and pages that have not yet been
compiled.

Bump `PageCompiler`'s format version in `RuntimeId`. This invalidates old PE-only page images on the
next request and ensures every served image from the new runtime has matching symbols. Keep the
compiler and storage behaviour deterministic; do not generate external `.pdb` files on disk.

### Format only mapped template frames

Add a small formatter in the Web shell that reads the exception's stack frames with file information
and accepts only frames whose mapped filename is exactly the generator's fixed `page` marker. Render
the retained frames as the page path plus 1-based template line and column. The first frame is the
failure location; subsequent frames, when present, are template call frames. Do not include frames
above or below that template-only sequence.

If no mapped template frame exists, the signed-in editor gets the exception type and message with a
clear indication that a template location was unavailable, but no unfiltered stack trace. This covers
exceptions originating in host/runtime code and any symbol-loading failure without leaking internals.

### Choose the error response from the existing principal

In `ErrorEndpoint`, continue to read the original exception from the exception-handler feature. For
unexpected exceptions, branch only on `context.User.Identity?.IsAuthenticated`:

- Anonymous: preserve the generic 500 page and generic message.
- Authenticated editor: show a concise template diagnostic and mapped template-only trace.

This is not a new authorization policy or route. `UseExceptionHandler` remains the only entry to the
error response, and its branch is not exposed as `/error`. Development continues to use the
developer exception page as it does now.

## Implementation phases

### Phase 0: prove portable PDB stack mapping

Add a focused runtime test or spike that emits and loads a page with portable symbols, throws from a
known template line, and verifies that the exception stack frame reports `page` with the expected
template line and column. Include a second code block and a multiline block so `#line` reset behaviour
is exercised. If the runtime does not surface embedded/loaded portable PDB locations through
`StackFrame`, settle the symbol-loading mechanism here before changing the stored format.

Done when the mapping is demonstrated through the same load/invoke path used in production.

### Phase 1: compile and load symbols

- Extend `PageCompileResult` and `IPageCompiler`/`PageCompiler` flow to return both image and symbols.
- Emit portable PDB data in memory, using the existing cancellation token.
- Extend `CompiledPage` loading to accept the paired PE/PDB bytes and pass both streams to the
  collectible load context.
- Bump the compiler format version in `RuntimeId`.
- Keep failures in generated compiler code as compiler faults; a missing PDB must not turn a
  successful compilation into a page compile error.

Runtime tests prove that a page still runs, exceptions remain unwrapped, and mapped file, line, and
column are present on thrown template exceptions. Include a test that verifies there are no
generated-source line numbers in the reported location.

### Phase 2: persist paired symbols with images

- Add the nullable symbol bytes to `PageCompilation` and `StoredFile`.
- Add an EF migration for the nullable BLOB column; existing databases migrate without data loss.
- Update `SqlitePageStore.SavePageAsync`, `TrySetCompilationAsync`, and materialization so the image
  and symbols are read and written atomically with the same page content and runtime ID.
- Require a current runtime ID before reusing a stored image. Old rows are recompiled due to the
  format-version bump; null symbols in old rows must never be treated as a current compiled result.
- Update test fakes, stored-page helpers, and compile fixtures to carry symbols where appropriate.

Storage tests prove a compile result round-trips both blobs, a save cannot pair symbols with another
content revision, and an existing row migrates and recompiles successfully.

### Phase 3: build the filtered diagnostic and authenticated presentation

- Add a formatter that extracts only frames mapped to `page` and converts them into explicit
  template-space values before rendering.
- Extend `ErrorEndpoint` to show the detailed form only when the principal is authenticated.
- Keep anonymous responses and `UserFacingException` presentation unchanged.
- HTML-encode all dynamic values and apply `Cache-Control: no-store` to personalized diagnostic
  responses.
- Keep the error handler branch private; do not add an error route or an authorization redirect.

Unit tests cover an authenticated principal, an anonymous principal, a user-facing exception, missing
symbols, a non-template exception frame, HTML-sensitive exception text, and private cache headers.

### Phase 4: verify the real request pipeline

Integration tests run the production host and cover:

- An anonymous request to a throwing page gets the generic 500 and no exception text or stack.
- A signed-in editor requesting the same page gets the page path and correct template line/column,
  without generated/runtime frames.
- A signed-in editor is recognized on a page route that itself does not require authorization.
- An anonymous request is not challenged or redirected to `/login` by the exception handler.
- `/error` remains a normal not-found page; it cannot be used to request diagnostics directly.
- Existing GET/POST exception handling, `UserFacingException`, and development exception behaviour
  remain unchanged.

Run the Runtime, Web and IntegrationTests suites, then `make test` and the repository's normal
`make check` gate.

## Acceptance criteria

- A thrown exception from page code produces a stable 1-based template location after both a fresh
  compile and a stored-image cache hit.
- A logged-in editor sees a concise trace containing template frames only, with the original page
  path and no generated C# or host/runtime frames.
- An anonymous visitor sees no additional detail and is not asked to sign in.
- Existing compiled rows migrate safely and are rebuilt with symbols through normal cache
  invalidation.
- An image and its symbols cannot be stored, loaded, or served as a mismatched pair.
- The error branch remains private and personalized responses are not cacheable.

## Out of scope

- Stopping, cancelling, or sandboxing arbitrary page code; execution budgets remain a separate
  concern.
- Showing local variables, source snippets, or a full host stack trace in the browser.
- Persistent error history or a public error-reporting endpoint.
- Changing the safe-message contract of `UserFacingException`.

## To think about

- A diagnostics dashboard for authenticated editors, built on the filtered template-space
  diagnostics without exposing host/runtime details.
