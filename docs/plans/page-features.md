<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Features: capabilities a page asks for

Status: **proposed, not implemented**. Written 2026-11-08. This is a design and an implementation
plan, not a record of work done. It changes the page contract, so every stored image is recompiled
on its next request (DEC-26); it does not change the template language, the generator, the page
store schema or the API policy.

## The short version

`IContext` keeps the request, response, logging and identity core, and loses the two
protocol-shaped capabilities that were bolted onto it: `Storage` and `FetchAsync`. Each becomes an
interface a page asks for through one new member, `IContext.Features`.

`IPageFeatures` has one abstract member, `TryGet<T>`, and two default interface members over it:
`Get<T>` returns `null` when this render does not offer the capability, and `GetRequired<T>` returns
it or throws. A page picks which it wants at the call site — absence as a value it can test, or as
an error.

The capability interfaces are the SDK's existing `IPageStorage` and a new `IHttpFetch`, and the
value types that go with the second are renamed from `PageFetch*` to `HttpFetch*` so the call site
reads as one thing. `IHttpFetch` is `SendAsync(HttpFetchRequest)` with `GetAsync(url)` and
`PostAsync(url, body)` as default interface members over it — the same one-primitive shape as
`IPageFeatures`, so a new HTTP verb stays a value rather than a member.

Layout — title, head slot, body slot — is planned afterwards, on top of this. It is not in this
change.

## Goals

- `IContext` holds what every page needs and nothing that only some pages want. A page that never
  stores state and never fetches never meets storage or HTTP.
- One place for a capability to be added, and one interface per capability, so the page API grows by
  a vertical slice rather than by another member on a type every page already sees.
- The page decides whether a missing capability is fatal: `Get` for "if it is there, use it",
  `GetRequired` for "this page cannot render without it".
- `SimpleContext` stops being the type that implements everything.
- No change to the handler signature, the generated source, `PAGE002`, the API policy or the page
  store.

## Non-goals

- Layout, title, head and body slots. See [IDEA-005](../jobs/IDEA-005.md); that plan is written
  against this one, once this has bedded in.
- Per-visitor state ([IDEA-014](../jobs/IDEA-014.md)) or page-store access
  ([IDEA-015](../jobs/IDEA-015.md)). Both become features later; neither is built here.
- Cutting the rest of `IContext` into capability interfaces. The request, the response, logging and
  the identity are what a page *is*; moving them behind lookups is churn a page pays for on every
  line and gains nothing from.
- Extension-method sugar over `Features`. It is additive later if call sites get noisy.
- Any decision about what a render is *allowed* to offer. The host decides what it registers; the
  page API only reports whether it is there.

## What already exists

- `IContext` is 145 lines and 15 members: request, path, user, aborted, three log levels, two
  writes, status, content type, header, `AllowScripts`, two redirects, the antiforgery field,
  `Storage` and `FetchAsync`.
- `SimpleContext` is 364 lines and implements all of it, plus the response buffering, the body cap,
  the fetch budget, the deadline and the abandonment flag.
- `IPageStorage` is already a standalone capability interface: `TryGetValueAsync<T>`, `SetAsync`,
  `DeleteAsync`, `KeysAsync`. The shell hands a page `PageStorageScope`, which is bound to the
  page's key and to whether this render may write, by implementing `IRenderBoundPageStorage`.
- `FetchAsync` is a member on `IContext` that checks abandonment, refuses a `POST` during a `HEAD`,
  spends one of `MaxRequestsPerRender` and then delegates to `IPageFetcher`. Its failures are
  values (`PageFetchResponse`/`PageFetchFailure`), not exceptions, and the status is `0` when no
  response was received (DEC-129).
- `PageApiPolicy` allows every type in `uk.osric.asp.Sdk` wholesale and already collects
  `GenericNameSyntax` and `TypeOfExpressionSyntax`, so a generic lookup in page code needs no policy
  change.
- The generator emits `using uk.osric.asp.Sdk;`, so a page names `IHttpFetch` unqualified.
- No seeded page uses `ctx.Storage` or `ctx.FetchAsync`; the tests and the assistant's author prompt
  do.

## The contract

```csharp
namespace uk.osric.asp.Sdk;

using System.Diagnostics.CodeAnalysis;

/// <summary>The optional capabilities this render offers.</summary>
/// <remarks>
/// Read-only: a page asks, and the host has already decided. A capability is either registered with
/// an implementation or absent — never registered as <see langword="null"/>.
/// </remarks>
public interface IPageFeatures {

    /// <summary>Reads a capability that this render may or may not offer.</summary>
    /// <param name="feature">The implementation, when this render offers the capability.</param>
    /// <returns>Whether this render offers the capability.</returns>
    public bool TryGet<T>([NotNullWhen(true)] out T? feature) where T : class;

    /// <summary>Reads a capability, or <see langword="null"/> when this render does not offer it.</summary>
    public T? Get<T>() where T : class => TryGet<T>(out T? feature) ? feature : null;

    /// <summary>Reads a capability this page cannot render without.</summary>
    /// <exception cref="InvalidOperationException">This render does not offer the capability.</exception>
    public T GetRequired<T>() where T : class =>
        TryGet<T>(out T? feature)
            ? feature
            : throw new InvalidOperationException($"This render does not offer {typeof(T).Name}.");
}

public interface IContext {
    // ... the request, response, logging and identity members, unchanged

    /// <summary>The capabilities this render offers.</summary>
    public IPageFeatures Features { get; }
}

/// <summary>Makes bounded outbound HTTP requests.</summary>
/// <remarks>
/// One capability, three call sites: the two conveniences cover the ordinary case and are default
/// interface members over the general form, so a host implements one method and a new HTTP verb
/// stays a value in <see cref="HttpFetchMethod"/> rather than a new member here.
/// </remarks>
public interface IHttpFetch {

    /// <summary>Makes one bounded outbound HTTP request.</summary>
    /// <param name="request">The request to make.</param>
    /// <returns>The response. A failure is a value, not an exception.</returns>
    public Task<HttpFetchResponse> SendAsync(HttpFetchRequest request);

    /// <summary>Makes a <c>GET</c> request.</summary>
    /// <param name="url">An absolute <c>http</c> or <c>https</c> URL.</param>
    /// <returns>The response. A failure is a value, not an exception.</returns>
    public Task<HttpFetchResponse> GetAsync(string url) =>
        SendAsync(new HttpFetchRequest(url));

    /// <summary>Makes a <c>POST</c> request with a text body.</summary>
    /// <param name="url">An absolute <c>http</c> or <c>https</c> URL.</param>
    /// <param name="body">The text body; the host sends it as <c>text/plain</c> unless a header says otherwise.</param>
    /// <returns>The response. A failure is a value, not an exception.</returns>
    public Task<HttpFetchResponse> PostAsync(string url, string body) =>
        SendAsync(new HttpFetchRequest(url) {
            Method = HttpFetchMethod.Post,
            Body = body,
        });
}
```

A page:

```text
<% IPageStorage store = ctx.Features.GetRequired<IPageStorage>();
   PageValue<int> found = await store.TryGetValueAsync<int>("visits");
   int visits = (found.Value ?? 0) + 1;
   await store.SetAsync("visits", visits); %>
<p>Visited <%= visits %> times.</p>
```

A page that can live without the capability:

```text
<% if (ctx.Features.Get<IHttpFetch>() is { } fetch) {
       HttpFetchResponse response = await fetch.GetAsync("https://example.com/");
       ...
   } %>
```

## Decisions

### 1. `TryGet` is the primitive; `Get` and `GetRequired` are default interface members

The host implements one member, a fake implements one member, and the two ergonomic forms cannot
drift between implementations. Making `Get` and `GetRequired` abstract would let two
implementations disagree about what "missing" means; making them extension methods would put them
outside the interface where a future implementation cannot opt into a better story.

The cost is that `Get` and `GetRequired` are reachable only through `IPageFeatures`, never on a
concrete variable. Pages only hold the interface, so the cost is confined to tests.

Two rejected shapes:

- **`PageValue<T>`-style result.** The SDK uses `PageValue<T>` for storage because an `out`
  parameter cannot cross an `await`. Feature lookup is synchronous, so `TryGet` with `out` and
  `[NotNullWhen(true)]` is the idiomatic form and allocates nothing.
- **Returning `object?` and casting at the call site.** That needs `System.Type` or a cast to a
  page-denied type, and loses the null-flow analysis that makes `Get` usable.

### 2. `Features` is read-only to a page and mutable only while the render is built

`Sdk` declares the read contract. `Web` owns the implementation, which can add a capability while
the context is being constructed and hands the page a set that cannot be added to afterwards. That
keeps the registration API out of the SDK entirely, so there is no `Set<T>` for a page to call.

Rejected: making `PageFeatures` a public SDK type with a public `Add`. It reads harmless and it is
not — a page that could register a capability could replace the one the host gave it.

### 3. `Storage` and `FetchAsync` leave `IContext`; nothing on `IContext` forwards to them

`Storage` becomes `ctx.Features.GetRequired<IPageStorage>()`; `FetchAsync` becomes
`ctx.Features.GetRequired<IHttpFetch>().SendAsync(...)`.

Rejected: keeping `ctx.Storage` and `ctx.FetchAsync` as conveniences that delegate to the feature.
It preserves the old call sites but hides where the capability came from, and it means the next
feature arrives as one more forwarding property. The call site should say what it is asking for.

Rejected: re-cutting the *rest* of `IContext` into capability interfaces behind the same lookup.
Pages would still name every capability they touch, so it adds noise without reducing what a page
can reach. The line is "everything a page always has stays on `IContext`; capabilities a page might
not have become features".

### 4. The capability interfaces are `IPageStorage` and `IHttpFetch`

`IPageStorage` is already the right shape and needs no wrapper: the host registers the per-page
`PageStorageScope` it already builds. `IHttpFetch` mirrors the capability it replaces — one
`SendAsync` that spends the render's fetch budget, `Task`-returning and tokenless (DEC-123) — so no
page needs to learn a new calling convention.

The general form is `SendAsync(HttpFetchRequest)` and the two ordinary cases are default interface
members over it. That is the same shape as `Get`/`GetRequired` over `TryGet`, and it is what keeps a
new verb cheap: `PUT` or `DELETE` later is a value in `HttpFetchMethod`, not a member on the
interface, so no host implements it and no page learns a second name.

Rejected: `GetAsync`/`PostAsync` as the interface's abstract members. It reads better at the common
call site and it puts the curl back in the contract: every verb becomes a member, the budget,
abandonment and `POST`-during-`HEAD` bookkeeping ends up either duplicated or behind a private core
that is the real shape anyway, and a page needing one header falls back to the general form
regardless.

Rejected: `IFetchClient`. `Client` in .NET means `HttpClient` — disposable, long-lived, pooled,
DI-registered — which is the opposite of a per-layer object bound to one render's budget and
deadline, and it sits badly beside `IPageFetcher`, the host type that really does hold the
`HttpClient`. The method is `SendAsync` rather than `FetchAsync` so the pair reads as a verb on a
capability (`fetch.GetAsync(url)`, `fetch.SendAsync(request)`) instead of repeating the word.

The new names are the vote: the capability is "make an HTTP request", not "fetch a page". The
request and response types are the protocol's, so they follow it — `PageFetchRequest`,
`PageFetchResponse`, `PageFetchFailure` and `PageFetchMethod` all become `HttpFetch*`. `Web`'s
host-side names — `IPageFetcher`, `PageFetcher`, `PageFetchOptions`, `PageFetchHeaders` — stay as
they are; they are configuration and plumbing, not page API, and `IPageFetcher.FetchAsync` stays its
own vocabulary.

Rejected: renaming only the interface. `IHttpFetch.SendAsync(new PageFetchRequest(...))` is the
kind of seam that survives forever because nothing ever forces it shut, and the SDK's module version
id is changing in this change anyway, so the rename costs nothing extra in invalidation.

### 5. A capability is registered or absent, never null

`[NotNullWhen(true)]` is a promise the compiler relies on in `Get`, and it is only true if the
registry refuses to hold a null. The host asserts this at construction rather than documenting it.

### 6. Features are per layer; the fetch budget is per render

Storage is already bound to the layer's own key and write permission, so its feature object is
per-layer by nature. `IHttpFetch` is bound to the layer's key (it logs and meters under that path)
and reads the render's deadline, so it is per-layer too.

The fetch allowance is not: `MaxRequestsPerRender` names the render, and a layout must not double
the allowance simply because there are two layers. The counter moves out of `SimpleContext` into one
object created per request and shared by every layer's `IHttpFetch`.

The deadline is attached to the context after construction (`AttachDeadline`), so `IHttpFetch` takes
a `Func<CancellationToken>` rather than a token, and reads it per call.

### 7. `IContext` stays the handler parameter, and the generator does not change

`IHandler.HandleAsync(IContext ctx)` is unchanged, so `LoadedPage`, `PAGE002`, the editor's
"Generated C#" view and the reference set all stay as they are. One member on `IContext` replaces
two, and the page API decides nothing about what a page may do — that stays with the policy and
the host.

Rejected: an empty `IContext` that only offers `Features`. It buys nothing over one member and
breaks the rule that a page always has a request and a response.

### 8. No `FormatVersion` or `PolicyVersion` bump

The compiler options, the reference set and the way the handler is invoked are unchanged, and
`PageApiPolicy` is unchanged; the SDK's module version id in `RuntimeId` already covers the contract
change, so every stored image is recompiled by the existing rule (DEC-26).

## What changes

`Sdk`:

- `IContext.cs` — remove `Storage` and `FetchAsync`; add `Features`.
- `IPageFeatures.cs` — new.
- `IHttpFetch.cs` — new.
- `PageFetchRequest.cs`, `PageFetchResponse.cs`, `PageFetchFailure.cs`, `PageFetchMethod.cs` —
  renamed to `HttpFetch*`.

`Web`:

- `SimpleContext.cs` — drop `_storage`, `_boundStorage`, `_fetcher`, `_fetchOptions`, `_fetches`,
  `_logger`'s fetch use, `FetchAsync` and `RefuseFetch`; add `Features`.
- `PageFeatures.cs` — new: the registry, built once per layer.
- `PageHttpFetch.cs` — new: the `IHttpFetch` implementation, carrying the abandonment, `HEAD`
  and budget checks that `FetchAsync` performs today, behind `SendAsync`.
- `RenderFetchBudget.cs` — new: the per-render counter, shared by every layer.
- `PageEndpoint.cs` — build the feature set and the budget, and pass them in.
- `Assistant/PageAuthorPrompt.cs` — the examples and prose teach `ctx.Storage` and
  `ctx.FetchAsync`; they have to teach the new calls or the assistant will keep writing pages that
  do not compile.

Docs and tests: `docs/architecture.md` where it names the contract; `SimpleContextTests`,
`PageFetchRenderTests`, `PageStorageRenderTests`, `PageScopeTests`, `PageHarness`, `ShellViews`,
`FakePageFetcher`, `PageFetcherTests`, `PageCompilerTests.TestContext` and the integration fetch
tests — templates and helpers move to the new calls, and `TestContext` implements only `TryGet`.

Not changed: `PageRenderer`, `LoadedPage`, `PageCompiler`, `TemplateSourceGenerator`,
`TemplateTokenizer`, `PageApiChecker`, `PageApiPolicy`, `IPageFetcher`, `PageFetcher`, the storage
schema and migrations.

## Tests

- `Get<T>` returns the registered capability; `Get<T>` returns `null` for one that was not
  registered; `GetRequired<T>` throws naming the interface; `TryGet<T>` reports true and false and
  leaves `default` otherwise.
- A page can branch on `Get<IPageStorage>() is null` and render without touching storage.
- The storage feature is the caller's own page's store: two pages do not see each other's keys, and
  a `HEAD` render cannot write through the feature. The existing scope tests move to the new call
  and keep proving it.
- The fetch feature refuses a `POST` during `HEAD`, refuses once the render's budget is spent, and
  reports abandonment, all as values rather than exceptions.
- `GetAsync` and `PostAsync` build the request the general form would, so a test over `SendAsync`
  covers all three: the same URL, the same method, and `text/plain` for a `POST` with no header.
- Two layer contexts sharing one budget make one request too many — the second layer is refused.
- `ctx.Features.GetRequired<IHttpFetch>()` from a page whose render registered no fetch throws
  `InvalidOperationException` whose message names `IHttpFetch`.
- A page that names `IHttpFetch` compiles under `PageApiPolicy` without a policy change, and one
  that names `System.Type` still does not.
- The generated source is byte-identical to today for a page that only writes text, so the editor's
  "Generated C#" view is unaffected.

## Open questions

- **`Features` or `Capabilities`?** `Features` matches the framework's vocabulary and is one word;
  `Capabilities` is more literal. Leaning `Features`.
- **Should `GetRequired` say why?** A message that names the interface is enough for a first cut; a
  reason ("this render is a `HEAD`") would need the host to pass a reason per feature, which is a
  second field on every registration.
- **Does the registry belong in `Sdk` after all?** Keeping it in `Web` keeps the SDK to interfaces
  and keeps registration out of reach; if a second host ever appears, the registry moves and the
  first host stops having its own copy.
- **Is `PostAsync(url, body)` enough?** It covers a form post and defaults the content type to
  `text/plain`. A page that needs JSON or an `Accept` header uses `SendAsync` with a
  `HttpFetchRequest`; if that turns out to be common, the second parameter of `PostAsync` is where a
  content type goes, not a third method.

## What it unlocks

[IDEA-005](../jobs/IDEA-005.md) phase 2 — the layout — is the first plan written on top of this:
the title, the head slot and the body slot are all "state one layer has and another layer reads",
which is what a feature is. [IDEA-014](../jobs/IDEA-014.md) then lands as `IPageSession` from
`ctx.Features` instead of another member on `IContext`, and
[IDEA-015](../jobs/IDEA-015.md)'s page-store access lands the same way.
