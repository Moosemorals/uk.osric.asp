<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page runtime hardening

Implementation plan for ARCH-02, ARCH-04, ARCH-05 (all three closed by this work) and the
page-code restrictions from the 2026-10-31 hardening discussion. [ARCH-03](../issues/ARCH-03.md)
(no execution budget) stayed open afterwards; SEC-01 (the editor was unauthenticated) was closed by
[editor authentication](editor-authentication.md).

## Threat model

Closer to "avoid obvious footguns" than "resist a determined attacker". The plan therefore does
four things and stops:

1. Make the page-visible API an explicit, default-deny allow-list with good diagnostics.
2. Shrink the compiler's reference set so the app's own assemblies cannot be named from page code.
3. Isolate compiled images at load time so reflection cannot reach the app's assemblies.
4. Restrict only where a test proves the mechanism is load-bearing.

It deliberately does **not** attempt a sandbox: .NET has no supported one, and the real boundary is
a separate process with OS restrictions, which is out of scope here and recorded in the new
`SEC-03` issue.

## Decisions already made

- **Pages are a runtime, not a renderer.** Request values in, response out, both as plain records in
  `Core`. No ASP.NET type crosses the boundary (ARCH-02, option 3).
- **ARCH-05.4 (data migration) is dropped.** The database was deleted, so no row predates the
  canonical-key rule. Rows are only ever written through `PagePath`, so the shape cannot recur
  except by a direct database write.
- **ARCH-05.5 (reserved names) is kept, and the list is configuration.** `Pages:ReservedPaths` in
  `appsettings.json`, with a compiled-in default so a missing or empty section cannot silently
  disable the check.
- **`MethodImplOptions.NoInlining` is removed** from `CompiledTemplate`, not kept "just in case".
  It was not load-bearing: `MethodInfo.Invoke` throws `TargetInvocationException` whether the caller
  is inlined or not, and `ExceptionDispatchInfo` preserves the page's own stack either way. A test
  asserts the page frame is present in the stack trace so a future JIT change is visible.
- **The execution budget (timeout, cancellation, kill) is out of scope.** `IContext.Aborted` is
  threaded through so cooperatively-cancelling pages work, but the timeout that would enforce it
  lands with [ARCH-03](../issues/ARCH-03.md).

## Status

Implemented on 2026-10-31, with one deviation: the post-emit metadata scan in Phase 4 was dropped
(see that phase). Everything else landed as written. Test counts at the time: 87 core, 32 runtime,
135 web, 23 integration.

## Identity, and why every phase invalidates stored images

`TemplateCompiler.RuntimeId` is the compile format version plus `Core`'s module version id, and
`PageService.LoadAsync` recompiles anything whose `RuntimeId` differs. Every phase here changes
`Core`, so every stored image goes stale on deploy and each page recompiles on its next request.
That is the design working: no data migration, at the cost of one compile per page after a release.

The format version becomes:

```text
{FormatVersion}:{PolicyVersion}:{CoreMvid}
```

- `FormatVersion` — bumped when the compiler options, the reference set, or the statement-list rule
  change.
- `PolicyVersion` — bumped whenever the allow-list tightens. Without this, images compiled under the
  old rules keep being served unchecked, which is exactly the set of pages the change was meant to
  catch.

Both constants carry a comment saying so.

---

## Phase 0 — finish ARCH-05

Small, independent, and the only phase that changes behaviour users can already see.

### 0a — reserved names, from configuration

- `appsettings.json` gains a section; `Program.cs` binds it into a singleton:

  ```json
  "Pages": {
    "ReservedPaths": ["/edit", "/site.css", "/favicon.ico"]
  }
  ```

- New `src/uk.osric.asp.Web/Pages/ReservedPaths.cs`: a small class over `IReadOnlyList<string>`
  with `static Default` (the same three), constructed from configuration and used when the section
  is absent or empty. `PagePath` stays pure —
  [its own remarks](../../src/uk.osric.asp.Core/PagePath.cs) already say the routing layer owns this
  rule, and the shell owns the routes.
- Matching is **prefix**, not exact: `/edit/{**path}` shadows everything under `/edit`, so a page
  called `/edit/anything` is equally unreachable. Document that in the type.
- `EditorEndpoint` refuses a save whose key is reserved, with a flash message like
  `"/edit is reserved by the site."` — the same path as the existing validation returns.
- `MainEndpoint` answers 404 for a stored page whose key is reserved, so a row that appeared by
  another route (direct database write, a future import) is never half-served.
- Tests: the default list rejects `/edit` and `/edit/page` but not `/edits`; a configured list
  replaces the default; a save of `/site.css` returns a message and stores nothing; a page stored
  directly at `/site.css` renders 404 through `MainEndpoint`.

### 0b — ARCH-05.4

Nothing to do: there is no old data. Record that in the issue so the omission is a decision rather
than an oversight.

---

## Phase 1 — ARCH-04, unwrap the reflection wrapper

Done first because Phase 2's error path depends on it, and it is tiny.

1. `CompiledTemplate.Invoke`: wrap `_handle.Invoke` in
   `catch (TargetInvocationException ex) when (ex.InnerException is not null)` and rethrow with
   `ExceptionDispatchInfo.Capture(ex.InnerException).Throw()`. Remove
   `[MethodImpl(MethodImplOptions.NoInlining)]` from `Invoke` **and** `Dispose` — it was there
   without a recorded reason and nothing in the design needs it. Leave a comment saying the runtime
   owns the reflection, so it owns the wrapper, and that the `NoInlining` was removed deliberately.
   If ALC unloading ever appears stuck, this is the knob to revisit, and the comment says so.
2. `ErrorEndpoint`: delete the `TargetInvocationException` case; match `UserFacingException` only.
3. Document the contract on `UserFacingException`: the only exception whose message may be shown to
   a visitor.
4. Tests:
   - `Runtime.Tests`: the exception surfaced by `Invoke` **is** the page's instance (assert on the
     object, not just the type), and its stack trace contains the page's `Handle` frame.
   - `Web.Tests`: `MainEndpointTests.StoredHandlerIsCompiledAndExecutedAsync` expects
     `InvalidOperationException` directly.
   - `Web.Tests`: `ErrorEndpointTests` gains a pair — a `UserFacingException` message is shown, an
     unexpected message is replaced with the generic text.

Done when nothing under `src/uk.osric.asp.Web` mentions `System.Reflection`.

---

## Phase 2 — ARCH-02, the page runtime contract

The largest phase, and the only one that changes what pages can do. Doing it before the allow-list
means the API is frozen once.

### 2a — values in `Core`

```csharp
public sealed record PageRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    IReadOnlyDictionary<string, string> Form,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Cookies);

public sealed record PageResponse(
    int Status,
    string ContentType,
    IReadOnlyDictionary<string, string> Headers,
    string? Location);
```

Single-valued dictionaries, as ARCH-02 proposes: `n` is small, and it keeps
`IContext` from growing a multi-value collection API.

### 2b — `IContext` grows

| Member | Note |
| --- | --- |
| `PageRequest Request { get; }` | the snapshot taken before the page runs |
| `string Path { get; }` | **kept**, so existing page sources do not change |
| `CancellationToken Aborted { get; }` | threaded now, cooperatively observable; enforcement is ARCH-03 |
| `void SetStatus(int status)` | the shell validates 100–599 |
| `void SetContentType(string contentType)` | |
| `void SetHeader(string name, string value)` | restricted, below |
| `void Redirect(string location, int status = 302)` | sugar for status plus `Location` |

`SetHeader` is narrow on purpose. Allowed: `Cache-Control`, `Expires`, `Content-Language`, `Vary`,
`ETag`, and any `X-` name. Refused: `Set-Cookie` (a page must not clobber the editor's flash cookie
or the future auth cookie), `Location` (use `Redirect`), `Content-Type` (use `SetContentType`),
`Content-Length`, `Transfer-Encoding`, `Connection`. The shell throws `ArgumentException` for
anything else, so a page that tries gets a 500 and a log line rather than a silent no-op.

Document on `IContext` that status and headers are applied **after** `Handle` returns, which is why
the body is buffered: a throwing page contributes nothing.

### 2c — the shell

- `SimpleContext` is promoted from a primary constructor to explicit fields: it now validates,
  accumulates response state, and exposes `PageResponse ToResponse()` with defaults `200` and
  `text/html; charset=utf-8`.
- New `PageRequestSnapshot` factory in `Web` builds the record from `HttpContext`:
  `Request.Method`, `Path`, and `Query`/`Form`/`Headers`/`Cookies` collapsed to single values.
  Guard form reading with `HasFormContentType`.
- `MainEndpoint` keeps buffering (it is what makes apply-after-run and all-or-nothing possible —
  say so in the comment). After `RenderAsync`: `Location` set means redirect with no body;
  otherwise set status, content type and headers, then write the buffer. Skip the body for `HEAD`.
- `Program.cs`: `app.MapMethods("{**path}", ["GET", "POST", "HEAD"], MainEndpoint.Handle)` so other
  verbs get a 405 from routing instead of running page code. Add a form size guard: a `ContentLength`
  over 1 MiB returns 413 before the form is read.

### 2d — tests

- `Web.Tests`: status and headers set by a page are applied; `Set-Cookie` is refused; `Redirect`
  yields 302 with `Location` and no body; a `POST` page reads `Form["x"]`; `PUT` returns 405; `HEAD`
  writes no body; an oversized form returns 413.
- `Web.IntegrationTests`: a POST form page round-trip through the real host, and a redirect page
  followed with `AllowAutoRedirect = false`.

---

## Phase 3 — compile-time policy (hardening steps 1 and 2)

### 3a — policy version first

Add `PolicyVersion` to `RuntimeId` (see "Identity") before any rule is written, so the first
tightening already invalidates the images it should.

### 3b — the policy as data, in `Core`

```csharp
public readonly record struct ApiReference(string Namespace, string Type, string Member);

public static class PageApiPolicy {
    public static IReadOnlyList<ApiReference> Allowed { get; }
    public static IReadOnlyList<string> Check(IEnumerable<ApiReference> used);
}
```

Default deny, type-level, with member-level carve-outs only where a type is a known escape.

| Allow | Why |
| --- | --- |
| `uk.osric.asp.Core.*` | the SDK: `IContext`, `PageRequest`, `PageResponse`, `UserFacingException` |
| `System.String`, `System.Text.StringBuilder`, `System.Char`, `System.Math`, `System.Convert`, `System.Guid`, `System.DateTime`, `System.DateTimeOffset`, `System.TimeSpan`, `System.Uri`, `System.Enum` | ordinary page work |
| `System.Collections.Generic.*` | `List<T>`, `Dictionary<TKey, TValue>`, `IEnumerable<T>` |
| `System.Linq.Enumerable` | `Where`, `Select`, query syntax |
| `System.Func`, `System.Action`, `System.Predicate` | lambdas resolve to their target type, so LINQ fails without these |
| `System.Globalization.CultureInfo` | formatting |

Everything else is denied by omission: `System.IO`, `System.Net`, `System.Diagnostics`,
`System.Reflection*`, `System.Linq.Expressions`, `System.Threading*`,
`System.Runtime.InteropServices`, `System.Runtime.Loader`, `System.Runtime.CompilerServices`,
`System.Text.RegularExpressions` (ReDoS), `Microsoft.*`, `uk.osric.asp.Web`, `uk.osric.asp.Runtime`.

**Do not allow `System.Object`.** `GetType()` is declared there; with `System.Object` allowed, the
resolved symbol is `System.Object.GetType` and a `System.Type` ban would not catch it. Denying it
costs nothing — `ToString()` on a `string` resolves to `System.String.ToString`. Record that reason
in the source.

Symbols declared in the tree being compiled — `GeneratedPage`, `Handle`, locals, parameters, local
lambdas — are allowed without appearing on the list, detected by comparing the symbol's declaring
syntax tree with the tree being checked.

### 3c — the walk, in `Runtime`

New `PageApiChecker` in `Runtime`:

1. Parse the generated source once and reuse the tree for both checking and emitting.
2. Walk every node; for identifier, generic-name, member-access, invocation, object-creation,
   base-type, attribute and `typeof` nodes, take `GetSymbolInfo`/`GetTypeInfo` plus each type
   argument, and project to `ApiReference`.
3. Reject outright, with their own messages: `stackalloc` (`StackAllocArrayCreationExpressionSyntax`
   — stack overflow in a loop), `dynamic`, pointer types, function pointers.
4. **Statement-list rule:** the compilation unit must hold exactly one type, `GeneratedPage`, with
   exactly one member, `void Handle(IContext)`. This single check blocks escaping the method with a
   closing brace, extra statics, a page-declared entry point, and `[ModuleInitializer]` (which would
   otherwise run at load time, before `Handle`).
5. Project to `PageApiPolicy.Check` and emit diagnostics like
   `PAGE001: pages may not use System.IO.File.ReadAllText (line 12)`. The line is the node's line
   minus the generator's header, which is approximate until
   [BUG-05](../issues/BUG-05.md) maps generated lines back to the page; note that in a comment.
   Fail with `Success = false`, `Image = null`, and the diagnostics — the shape `PageService.Compile`
   and the editor already handle.
6. Check **before** `Emit`, so a rejected page never produces or stores an image.

### 3d — tests

- `Core.Tests/PageApiPolicyTests`: an allowed type passes; a denied namespace fails; `GetType`
  fails; a page-declared helper passes; `Func` passes; messages are asserted exactly, not with
  `Contains`.
- `Runtime.Tests/PageApiPolicyTests`: pages calling `File.ReadAllText`, `Environment.Exit`,
  `typeof(...)`, `stackalloc`, `[ModuleInitializer]`, and a closing brace that declares a second type
  are each rejected with diagnostics and no image; a normal page with interpolation, a loop and LINQ
  still compiles.

Expect a second pass after real pages run: the first page opened after deploy will probably trip on
something innocuous. The fix should be an obvious addition to `Allowed` and nothing else, which is
why the table is readable data and the diagnostic names the exact API.

---

## Phase 4 — reference trim and post-emit scan (hardening step 3)

1. Build metadata references from `TRUSTED_PLATFORM_ASSEMBLIES` **minus** a deny-list:
   `uk.osric.asp.Web`, `uk.osric.asp.Runtime`, `Microsoft.*`. Assert in a test that
   `typeof(Program).Assembly` and `typeof(TemplateCompiler).Assembly` are absent.
2. Comment the honest limitation: in .NET 5+ most of `System.IO` and `System.Environment` live in,
   or forward from, `System.Private.CoreLib`, so the trim cannot remove them. Its job is to make the
   app's own assemblies and the web stack unreachable at compile time, turning a silent capability
   into CS0246. Prove it: a page naming `uk.osric.asp.Web.SimpleContext` fails with CS0246, not
   PAGE001.
3. ~~Post-emit `MetadataReader` scan over `TypeReference` rows.~~ Dropped. Every API a page can use
   has to appear as syntax, so the walk already sees it; the scan could only add false rejections for
   compiler-emitted helpers (`DefaultInterpolatedStringHandler` for every `$"..."`,
   `IteratorStateMachineAttribute`, the nullability and `RefSafetyRules` attributes), and it cannot
   see reflection by name, which is what it was supposed to catch. The reasoning is recorded next to
   the reference trim in `TemplateCompiler` so it is not silently re-added.
4. Bump `FormatVersion` if the reference set changes after `PolicyVersion` shipped.

---

## Phase 5 — load-time isolation (hardening step 4)

1. Replace `new AssemblyLoadContext(null, isCollectible: true)` with a `PageLoadContext` whose
   `Load(AssemblyName)` **throws** `FileNotFoundException` for `uk.osric.asp.Web`,
   `uk.osric.asp.Runtime`, `Microsoft.EntityFrameworkCore*`, `Microsoft.Data.Sqlite`,
   `Microsoft.AspNetCore*`, and returns `null` for everything else so `Core` and the BCL resolve
   normally. Throwing, not filtering is the point: returning `null` falls back to the default
   context, which is the current hole. This is what defeats
   `Type.GetType("uk.osric.asp.Web.Program, uk.osric.asp.Web")` and `AssemblyLoadContext.Default`.
2. Document the limit plainly on the type: it isolates pages from the app's internals; it is not a
   sandbox, because a page still runs with the site's identity.
3. Tests: the policy now prevents compiling a reflection-using image, so `Runtime.Tests` builds one
   with its own `CSharpCompilation` (Roslyn is already referenced there) that names
   `uk.osric.asp.Web`, then asserts `new CompiledTemplate(image)` throws
   `FileNotFoundException` at load. Positive case: an ordinary page still loads under the new
   context.

This is the last in-process step, and it is the one that makes a future process boundary a drop-in:
`CompiledTemplate` already owns one load context per page.

---

## Phase 6 — documentation and bookkeeping

- `docs/architecture.md`: a "Page contract" section (the `IContext` surface, buffering and
  apply-after-run, 405 for other verbs), a "Page API policy" section (default deny, statement-list
  rule, `PolicyVersion` → `RuntimeId`), and a "Load isolation" section (what the ALC blocks, what it
  does not). Add rows to the "Where does code go?" table: "API policy and page values → `Core`",
  "Roslyn walk over the policy → `Runtime`".
- `docs/issues`: close ARCH-02, ARCH-04 and ARCH-05 with rows in the Closed table; add `SEC-03` —
  *page code has no execution budget and no process boundary* — recording the deferred work
  (timeout and kill, `--network=none` sidecar, memory limits) and the residual risks this plan does
  not buy: unbounded allocation, `Environment.Exit`, `Environment.FailFast`, shared-static mutation,
  background threads that survive `Unload`.
- `docs/testing.md`: point at the policy test matrices.
- `deploy/README.md`: mention `Pages:ReservedPaths` if deployment ever overrides it.

---

## Sequencing

| Phase | Scope | Risk |
| --- | --- | --- |
| 0 | Reserved names from config; ARCH-05.4 dropped | Low |
| 1 | ARCH-04, drop `NoInlining` | Trivial |
| 2 | ARCH-02 contract, endpoints, 405/413 | Largest surface; only phase that changes what pages can do |
| 3 | Allow-list plus `PolicyVersion` | Medium; content needs iteration against real pages |
| 4 | Reference trim and IL scan | Low value per unit of risk; keep only what a test proves |
| 5 | Load-context guard | Small, but must be tested with a hand-built image |
| 6 | Docs and issues | — |

Every phase ends with `make build && make test && make format-check`; Phase 0 and Phase 6 also need
`make license-check` because they add files.

Note: `make license-check` currently fails on `CONTRIBUTING.md`, which predates this plan and carries
no SPDX tags. That is unrelated to this work, so it is recorded here rather than fixed — decide
separately whether the file should be covered by `REUSE.toml` or given a header.
