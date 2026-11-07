<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page authentication: one guard at the top of a page

Implementation plan for [IDEA-011](../jobs/IDEA-011.md) — pages cannot opt into the sign-in that the
editor already has. DEC-02 makes every page public, which is right for the shop window but leaves no
place for a draft, a personal dashboard or anything else that is only for the owner.

The driver is not "add authentication". It is that **a page should not have to implement its own**.
The site already has local accounts, a cookie, a validated principal and a login form. What a page
needs is a way to say "not for anonymous visitors" in the same language as the rest of the page, and
to be able to trust the answer.

The shape this plan settles on is ordinary page code:

```html
<% if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; } %>
```

After that line the page can greet `user.Name`, write its content and accept a form. Before it, the
page is public. There is no flag, directive, column or policy to remember.

## Status

Proposed, 2026-11-07. Not implemented. The sketch in [IDEA-011](../jobs/IDEA-011.md) recommended a
template directive or a metadata column; this plan deliberately chooses neither, for the reasons in
[D1](#d1--the-opt-in-is-ordinary-page-code). The sketch's alternatives stay in the history of the
idea; the plan is the source of truth from here.

## The short version

1. `IContext.User` is `PageUser?`, populated by the shell from the cookie principal that
   `UseAuthentication` has already validated. `null` means anonymous; `PageUser` carries `Name` and
   nothing else.
2. `IContext.RedirectToLogin()` sends the visitor to `/login?returnUrl=<path and raw query>`. It is a
   `302` for `GET`/`HEAD` and a `303` for `POST`. The shell builds the target, so the page's canonical
   key and its query survive exactly.
3. The guard is code: `if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; }`. `return;`
   ends the generated handler, and the buffered body is discarded because the response is a redirect.
4. **Pages stay public by default.** There is no "protected page" state the host enforces. A page
   without the guard is public. DEC-02 becomes "pages are public unless the page's own code checks
   `ctx.User`".
5. The host validates antiforgery on any `POST` whose principal is authenticated, **before** page code
   runs. `IContext.AntiforgeryField` renders the hidden input every form on a private page needs.
6. An authenticated page response is `Cache-Control: no-store` and carries no `ETag`. Every page
   redirect is `no-store`. A page cannot weaken either.
7. No schema, parser, editor or route change. The SDK's module version id already invalidates stored
   images, so every page recompiles once when the contract changes.
8. The page-author assistant learns the guard, the field and the public-by-default rule; its prompt
   version bumps.
9. Tests cover the contract, the redirect, the CSRF refusal, the cache headers and a full sign-in
   round trip back to a private page.

## The developer experience

### A private page

```html
<% if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; } %>
<h1>Draft</h1>
<p>Signed in as <%= user.Name %>.</p>
```

The pattern form is the recommended one because it binds the non-null value to `user`. Page source
compiles with nullable analysis off, so `ctx.User.Name` after the `is null` check also compiles; the
local makes the intent obvious to the next reader and survives nullable being switched on later.

The equivalent, if a reader prefers it:

```html
<% var user = ctx.User; if (user is null) { ctx.RedirectToLogin(); return; } %>
```

### A public page that knows the owner

`ctx.User` is available everywhere, not only on private pages. A public page can use it to change a
link or a greeting; nothing about it requires a guard:

```html
<% if (ctx.User is { } user) { %>
<p>Welcome back, <%= user.Name %>. <a href="/edit/">Edit</a></p>
<% } else { %>
<p><a href="/login">Sign in</a></p>
<% } %>
```

### A form on a private page

The host refuses an authenticated `POST` without a valid antiforgery pair. The page writes the field
the context gives it, raw:

```html
<% if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; } %>
<form method="post" class="form">
    <%== ctx.AntiforgeryField %>
    <dl>
        <dt><label for="note">Note</label></dt>
        <dd><input id="note" name="note"></dd>
    </dl>
    <button type="submit">Save</button>
</form>
<% if (ctx.Request.Form.TryGetValue("note", out string note)) { %>
<p>Saved: <%= note %></p>
<% } %>
```

`AntiforgeryField` is the same `<input type="hidden">` the shell's own forms carry, built by the
existing `Html/AntiforgeryField.Hidden`. It must be written with `<%==` (or `ctx.WriteRaw`), because
it is HTML rather than text.

### Signing out

`/logout` is a `POST` with the same token, so a private page offers a small form rather than a link
that would fail:

```html
<form method="post" action="/logout">
    <%== ctx.AntiforgeryField %>
    <button type="submit">Sign out</button>
</form>
```

### What a page must not do

A page must not build a login form, read a password, compare a cookie, or invent its own identity.
The only identity a page sees is `ctx.User`; the only sign-in is `/login`; the only sign-out is
`/logout`. A page that needs different words on the sign-in screen is a separate product decision.

## Goals and non-goals

**Goals**

- One guarded line, identical in every private page, and no per-page authentication machinery.
- The identity is supplied and validated by the shell; a page cannot forge it with a header, cookie
  or form field.
- A signed-out visitor goes to the existing sign-in form and comes back to the exact path and query
  they asked for.
- A private page's form is not a CSRF target just because the owner is signed in.
- The page-author assistant can write a private page deliberately, without believing every page is
  private or every page is public.
- No new package, no migration, no editor UI, no new route.

**Non-goals**

- Roles, claims, groups, ownership per page, invitations, registration or a second identity provider.
  Any local account is the same owner (DEC-03).
- Per-user storage. `Storage` stays per page and shared; using `ctx.User.Name` inside a key is the
  page's own arrangement, not an isolation guarantee. Per-user storage is a later job with its own
  migration and decision.
- A route policy, middleware filter or `[Authorize]` for stored pages. A stored page is data, not an
  endpoint declaration.
- Hiding a private page's existence. The redirect reveals that the page exists to a signed-out
  visitor; a page that prefers `404` can set the status and return instead.
- API clients. This is the browser's sign-in with a cookie; there is no bearer token, JSON challenge
  or `401` contract for pages.
- Making page code trustworthy. A page still runs in-process with the site's identity (DEC-40);
  authentication changes who may reach the guard, not what a page author can do.

## The contract

### Today

```csharp
public interface IContext {
    PageRequest Request { get; }
    string Path { get; }
    CancellationToken Aborted { get; }
    void Log(string message);
    void LogWarning(string message);
    void LogError(string message);
    void WriteRaw(string message);
    void Write(string message);
    void SetStatus(int status);
    void SetContentType(string contentType);
    void SetHeader(string name, string value);
    void Redirect(string location, int status = 302);
    IPageStorage Storage { get; }
    Task<PageFetchResponse> FetchAsync(PageFetchRequest request);
}
```

### After this plan

```csharp
public interface IContext {
    // ... every existing member unchanged ...

    /// <summary>The signed-in local account, or null for an anonymous visitor.</summary>
    public PageUser? User { get; }

    /// <summary>Redirects to the sign-in form and returns to this page afterwards.</summary>
    public void RedirectToLogin();

    /// <summary>The hidden input that carries the antiforgery pair; write it raw.</summary>
    public string AntiforgeryField { get; }
}
```

`PageUser` is a new file in `Sdk`, beside `PageRequest` and `PageValue`:

```csharp
namespace uk.osric.asp.Sdk;

/// <summary>The local account a page is running for, as a plain value.</summary>
/// <param name="Name">The normalised user name from the sign-in cookie.</param>
public sealed record PageUser(string Name);
```

Three members, one new type, no reference from `Sdk` to anything else. `PageUser` is deliberately
smaller than `LocalUser`: the page sees who is signed in, not the password hash, the security stamp
or the database row. A page cannot construct a `PageUser` that the shell will believe; the value only
comes from `IContext.User`.

### `RedirectToLogin`

The method takes no arguments. Its target is built once by the shell from the canonical page key and
the **raw** query string:

```text
/login?returnUrl=<percent-encoded "/canonical/path?raw=query">
```

It must not be built from `ctx.Request.Query`, because that dictionary has already collapsed
repeated parameters to one value per name. A page at `/search?q=a&q=b` must return to both `q`
values.

The status is `302 Found` for `GET` and `HEAD`, and `303 See Other` for `POST`: a form post that turns
into a sign-in should not be replayed as a post when the visitor comes back.

`LocalRedirect.Safe` remains the only thing that turns the `returnUrl` back into a location. The
login endpoint's existing validation already refuses an absolute or protocol-relative target, so a
private page cannot become an open redirect. The target is built from the page's own canonical key
and its own query string; no other value from the request becomes a destination.

A fragment is not preserved: the browser never sends `#...` to the server, so the shell has nothing
to carry across the sign-in. A page that needs to restore a fragment does it in the browser after the
return, which is outside this plan. [IDEA-011](../jobs/IDEA-011.md) item 5 asked for both; only the
query is server-side possible.

### `AntiforgeryField`

The property returns the same hidden input the shell's forms use:

```html
<input type="hidden" name="__RequestVerificationToken" value="...">
```

It is generated lazily, on first access, and cached for the render: the token and its cookie are one
pair, and asking twice must not mint two. Generation happens under the context's existing body lock,
so an abandoned render cannot race the response that has already been sent; after abandonment the
property returns an empty string, which fails closed at the next `POST`.

Generation calls `IAntiforgery.GetAndStoreTokens`, which sets the antiforgery cookie. That is safe
because the page body is still buffered and the response has not started.

## How a request flows

1. `UseAuthentication` has already run and validated any auth cookie, including the existing
   `OnValidatePrincipal` check against the account's security stamp. A revoked or deleted account is
   anonymous by the time the page endpoint sees it.
2. `PageEndpoint` normalises the key, redirects a non-canonical URL, refuses a reserved key, buffers
   the form and takes the `PageRequest` snapshot.
3. The endpoint maps the principal: an authenticated identity with a non-empty
   `ClaimTypes.NameIdentifier` becomes `PageUser(name)`; everything else becomes `null`.
4. If the principal is authenticated and the method is `POST`, the endpoint validates antiforgery
   before page code runs. A failure is a `400` with no page execution.
5. The endpoint builds the login target from the canonical key and the raw query, and constructs
   `SimpleContext` with the user, the target and a lazy antiforgery-field factory.
6. The page runs. `ctx.User` is null or a value. A guard calls `ctx.RedirectToLogin()` and `return;`;
   the rest of the handler does not run.
7. The renderer returns. If the response carried a `Location`, the endpoint sends the redirect and
   drops the body; otherwise it sends status, headers and body.
8. An authenticated response is sent `Cache-Control: no-store` and without an `ETag`; every page
   redirect is `no-store`.

## Decisions

### D1 — The opt-in is ordinary page code

Use `ctx.User` plus `ctx.RedirectToLogin()`, not a template directive, a page metadata column or a
middleware marker.

A directive or column would make the host know before the page runs, but it would also add a parser
or schema change, an editor control, an assistant tool concept and a second source of truth about
what the page does. It would still have to teach the page the same two statements to say what "signed
in" means. The code form:

- travels with the page through import/export and the assistant;
- is visible in the same place as the rest of the page's logic;
- needs no migration and no editor UI;
- works in the editor's save/compile path without a new validation rule;
- keeps the host's page contract small.

The cost is that the host cannot enforce the guard: a page that forgets it is public. That is the
correct default for this site, and the same property every other page rule has — a page that forgets
to escape a value writes unsafe HTML. The assistant and the docs teach the line; no analyzer is
proposed.

**Rejected:** a marker comment scanned by the host. It is a directive with worse syntax checking.
**Rejected:** `ctx.RequireAuthentication()` that redirects and returns a user. It hides the redirect
inside a call and makes the control flow harder to see; the explicit `if` is the API.

### D2 — Identity is a shell-supplied plain value

`PageUser` is created only in `PageEndpoint`, from the validated `ClaimsPrincipal`. It is not part of
`PageRequest`: that record is the raw request snapshot, and putting identity in it would invite a page
to read a cookie or header as if it were the user. `PageUser` carries only `Name`, because roles and
claims are exactly what the one-owner model avoids (DEC-03).

An authenticated identity with no `NameIdentifier` maps to `null`. A malformed or half-built
principal must fail closed for the page, even if the authentication handler considers it
authenticated. The CSRF check uses `Identity.IsAuthenticated`, not the non-null `PageUser`, so a
principal in that edge state still gets the protection.

### D3 — Redirect to the existing sign-in, with a validated local return

The page endpoint builds `/login?returnUrl=...` once, from the canonical key and the raw query. The
existing login endpoint already:

- renders the form and posts to itself;
- validates the return target with `LocalRedirect.Safe`;
- refuses an absolute, protocol-relative or backslash target;
- answers `303` after a successful post.

The only change is that a page can now be the reason a browser arrives there. `/login` and `/logout`
are already reserved page keys, so a page cannot shadow either.

`302`/`303` rather than `401`: this is a browser flow, and the visitor needs a form. A page that
prefers not to reveal its existence can set `404` and return instead; that is a page decision, not a
second helper.

### D4 — Authenticated page POSTs are CSRF-checked by the host

The page endpoint calls `IAntiforgery.IsRequestValidAsync` when the principal is authenticated and the
method is `POST`, after the form has been buffered and before `RenderAsync`. A failure is `400` with
`Cache-Control: no-store` and no page execution.

This is deliberately not opt-in. A private page's form is exactly where the sign-in cookie turns a
cross-site post into an action by the owner, and "opted into authentication" must not quietly opt out
of request forgery protection. Leaving the check to the page author is fail-open.

`AntiforgeryField` is the other half: the page has a one-line way to satisfy the check. A private page
with no form needs nothing.

**Behaviour change:** an authenticated `POST` to a public page now needs the field too. No existing
page relies on the old behaviour, because before this change an authenticated page request was just an
anonymous page request with a cookie the page could not use. The new rule is the safe default. An
anonymous `POST` is unchanged: it has no cookie-backed identity to forge.

### D5 — An authenticated response is never cacheable

`WritePageAsync` sends `Cache-Control: no-store` and omits the `ETag` for a response whose principal is
authenticated. It does this after copying the page's headers, so a page cannot weaken it with
`ctx.SetHeader("Cache-Control", ...)`. Every page redirect is `no-store` as well.

The body is already buffered and per-request; the risk is a shared cache or a conditional request
turning one owner's rendered page into another visitor's response. `no-store` plus no validator makes
that impossible without changing the behaviour of public pages, which keep their strong `ETag` and
revalidation.

A public page may render differently for the owner — a greeting, an edit link — but the authenticated
variant is `no-store`, and the anonymous variant contains only public content. A shared cache that
keeps the anonymous variant cannot leak a private body; it can at worst show the wrong greeting to
the owner. `Vary: Cookie` is deliberately not added: it would fragment every public page on a value
that changes with each sign-in and buys nothing the cache-safety rule does not already provide.

### D6 — Pages remain public by default; DEC-02 is amended, not replaced

The existing rule is right: the site is a shop window. The amendment is one sentence:

> Pages are public by default; a page becomes private when its own code checks `ctx.User` and calls
> `ctx.RedirectToLogin()`. Only the editor is behind a route policy. There are still no roles,
> ownership or registration.

DEC-03 (one owner, any account is the authorisation) and DEC-40 (no sandbox) are unchanged.

### D7 — The assistant learns the guard, but is not made paranoid

`PageAuthorPrompt` gains a short section and one worked example. The rule it states is:

- pages are public unless the page's own code checks `ctx.User`;
- add the guard when the owner asks for a private page, a draft or something only they should see;
- do not add it to every page by default;
- a form on a private page includes `<%== ctx.AntiforgeryField %>`;
- never write a login form, password handling or cookie logic; `/login` and `/logout` already exist.

The prompt version becomes `page-author-v4`. The contract section is generated from `IContext` by
reflection, so the three new members appear automatically; the prose and the example are what teach
the model to use them.

### D8 — The shell owns the URL and the token; the page owns the decision

`SimpleContext` gains three plain values: a `PageUser?`, the login target string and a lazy field
factory. It stays free of ASP.NET types in its public surface, as it is today. `PageEndpoint` builds
all three from the live `HttpContext`; `SimpleContext` only stores them and exposes the SDK shape.

`PageEnvironment` gains the `IAntiforgery` singleton. It already exists so the endpoint's signature
stays readable as the page contract grows a capability; this is exactly that case. Tests already have
`TestAntiforgery`, so the fakes do not grow a new concept.

### D9 — No version bump beyond the SDK's own identity

`PageCompiler.RuntimeId` already includes both module version ids. Adding members to `IContext` and a
type to `Sdk` changes the SDK module version id, so every stored image is recompiled once and no page
is ever invoked against the old protocol. `PolicyVersion` does not change: the page API policy admits
the whole `Sdk` namespace already, and no denied API moves.

## What does not change

- No database migration, no page column, no editor checkbox, no listing filter.
- No new route. `/login`, `/logout` and the rest of `ReservedPaths` stay exactly as they are.
- No new package. `IAntiforgery` and the cookie handler are in the shared framework.
- No change to `PageRequest`, the tokenizer, the generator or `PageApiPolicy`.
- No roles, claims, ownership, invitation or registration.
- No change to the editor's `Editor` policy or the assistant's route policy.
- No per-user storage. `ctx.Storage` is still bound to the page key and shared by every visitor.
- No promise that a page author is trustworthy: DEC-40 still applies.

## Implementation phases

Test-first throughout, one commit per phase, starting from green `make check` on `trunk`. Every new
file carries its SPDX header; `make license-check` is part of the gate.

### Phase 1 — The SDK contract

1. Write failing tests first:
   - `Web.Tests/PageScopeTests`: a template using `ctx.User`, `ctx.RedirectToLogin()` and
     `ctx.AntiforgeryField` compiles through the real compiler; `System.Security.Claims` is still
     `PAGE001`; a page can bind `PageUser` to a local and read `Name`.
2. Add `src/uk.osric.asp.Sdk/PageUser.cs` with XML documentation.
3. Add the three members to `IContext` with XML documentation. `RedirectToLogin` documents the
   `302`/`303` rule and the missing fragment; `AntiforgeryField` documents the raw-write rule and the
   empty-after-abandonment rule.
4. No generator change: `return;` already exits the generated `HandleAsync` because code blocks are
   emitted inside it. Add a generator test that proves a guard with `return;` still generates.
5. Do not bump `PolicyVersion` or `FormatVersion`; the SDK module version id covers this change.

### Phase 2 — Identity and the login redirect in the shell

1. Add `src/uk.osric.asp.Web/Authentication/LoginRedirect.cs`, a small pure helper beside
   `LocalRedirect`:

   ```csharp
   public static string For(PagePath path, QueryString query);
   ```

   It builds `/login` plus `QueryString.Create("returnUrl", path.Key + query.ToUriComponent())`.
   Tests cover a plain path, a query, a repeated parameter, an encoded value and the root page.
2. `SimpleContext` gains `PageUser? User`, `void RedirectToLogin()` and the login target. The
   constructor gains trailing optional parameters (`PageUser? user`, `string loginRedirect`,
   `Func<string>? antiforgeryField`) so the two test helpers and `PageHarness` stay readable; the
   production call site passes all three explicitly. With no user the identity is anonymous; with no
   factory `AntiforgeryField` is empty, which is fail-closed rather than a silent token.
3. `PageEndpoint` maps the principal once, builds the login target and passes them to the context.
   Mapping uses `ClaimTypes.NameIdentifier`; an authenticated identity with no name is anonymous.
4. Tests:
   - `SimpleContextTests`: null by default; a supplied user is returned; `RedirectToLogin` records
     `302` for `GET`/`HEAD` and `303` for `POST`, and the expected `Location`.
   - `PageEndpointTests`: an anonymous request to a guarded page is a redirect to `/login` with the
     path and query; output written before the guard is not sent; a signed-in request renders and sees
     the name; a public page is unchanged; a forged cookie or header without a principal is anonymous.
   - `PageHarness.RenderAsync` gains an optional `PageUser?` so a render test can see one without
     going through HTTP.

### Phase 3 — CSRF and caching

1. Add `IAntiforgery` to `PageEnvironment` and resolve it in `Program.cs`; update the two test
   helpers that build a `PageEnvironment` (`PageEndpointTests.EndpointEnvironment` and
   `ShellViews`) to pass `TestAntiforgery`.
2. `PageEndpoint` validates an authenticated `POST` before `RenderAsync` and answers `400` on
   failure. Extend `TestAntiforgery` with a validity switch (or a second fake) so the refusal is
   testable; the existing token behaviour stays.
3. `SimpleContext.AntiforgeryField` generates once, under the body lock, through the factory; after
   `Abandon()` it returns an empty string. Tests cover one generation per render and the abandoned
   case.
4. `WritePageAsync` sends `no-store` and skips the `ETag` for an authenticated response; page
   redirects send `no-store`. Tests assert the header and the absent validator, and assert that a
   public anonymous response still gets its `ETag`.
5. Tests:
   - `PageEndpointTests`: authenticated `POST` without a token is `400` and the page does not run
     (use a page that writes storage); with the field it renders; an anonymous `POST` is unchanged; a
     guarded page's form renders the field and the follow-up post succeeds.

### Phase 4 — The real round trip

Add `test/uk.osric.asp.Web.IntegrationTests/PageAuthenticationTests.cs` using the existing
`CreateSignedInClientAsync`, `PostEditorAsync`, `GetAntiforgeryTokenAsync` and `Form` helpers:

- save a guarded page through the editor, request it anonymously and follow the redirect to `/login`;
- sign in with `returnUrl` set to the page's path and query, and land back on it;
- confirm the page sees the account name;
- post its form with and without `ctx.AntiforgeryField`;
- sign out and confirm the same URL redirects to `/login`;
- change the account's password and confirm an existing cookie is anonymous for a page, not only for
  the editor;
- request `/login?returnUrl=//evil.example` and confirm the existing `LocalRedirect` fallback still
  refuses it;
- confirm a public page still renders anonymously and still carries its `ETag`.

### Phase 5 — Assistant, docs and bookkeeping

1. `PageAuthorPrompt`: add the public-by-default rule, the guard, the field, the "never implement
   sign-in" rule and the `private-page` example; bump `Version` to `page-author-v4`. Tests:
   `TheAuthenticationSectionTeachesTheGuard`, the example compiles through the real compiler, and the
   version appears.
2. `docs/architecture.md`: update the page-contract member list; add a "Page authentication"
   subsection under "Authentication and the editor"; update the caching/`ETag` paragraph and the
   request-flow note about CSRF. Keep "pages are public" as the default and say how a page opts out.
3. `docs/decisions.md`: amend DEC-02 to the text in [D6](#d6--pages-remain-public-by-default-dec-02-is-amended-not-replaced);
   add the new decisions under "Page contract and threat model" (the next free numbers), linking this
   plan. Update the DEC-125 open-policy note: the identity hook now exists; per-user storage is still
   a later job.
4. `docs/testing.md`: add the new Web and integration coverage to the suite descriptions.
5. `docs/security/auth-cookie-review.md`: re-check the existing rows against page responses; the new
   surface is cacheability and CSRF, not a new cookie or a new secret.
6. Update [IDEA-011](../jobs/IDEA-011.md) and [the job index](../jobs/README.md) with the plan link
   now; fold the idea into the closed table when the work lands (see below).
7. Move this plan to `docs/plans/archive/page-authentication.md` on completion and point the closed
   IDEA-011 row at it.

## Test matrix

| Area | Test | Suite |
| --- | --- | --- |
| Contract | `ctx.User`, `RedirectToLogin()` and `AntiforgeryField` compile through the real compiler | `Web.Tests` (`PageScopeTests`) |
| Contract | A guard with `return;` still generates a valid handler | `Core.Tests` (`TemplateSourceGeneratorTests`) |
| Contract | `System.Security.Claims` and `System.Net` stay `PAGE001` | `Web.Tests` (`PageScopeTests`) |
| Identity | No principal means `ctx.User` is null | `Web.Tests` (`SimpleContextTests`) |
| Identity | A supplied `PageUser` is visible with its name | `Web.Tests` (`SimpleContextTests`, `PageEndpointTests`) |
| Identity | An auth cookie without a validated principal, or a forged header, is anonymous | `Web.Tests`, `Web.IntegrationTests` |
| Redirect | A guarded anonymous `GET` is `302` to `/login?returnUrl=…` | `Web.Tests`, `Web.IntegrationTests` |
| Redirect | A guarded anonymous `POST` is `303` | `Web.Tests` |
| Redirect | The return target preserves the canonical path and repeated query values | `Web.Tests` (`LoginRedirectTests`) |
| Redirect | Output written before the guard is not sent | `Web.Tests` (`PageEndpointTests`) |
| Redirect | `returnUrl` is local; an external target falls back | `Web.Tests` (`LocalRedirectTests`), `Web.IntegrationTests` |
| CSRF | An authenticated `POST` without a token is `400` and the page does not run | `Web.Tests`, `Web.IntegrationTests` |
| CSRF | An authenticated `POST` with `AntiforgeryField` renders | `Web.Tests`, `Web.IntegrationTests` |
| CSRF | An anonymous `POST` is not rejected for the missing token | `Web.Tests` |
| Cache | An authenticated response is `no-store` and has no `ETag` | `Web.Tests` |
| Cache | A page redirect is `no-store` | `Web.Tests` |
| Cache | A public anonymous response still has its `ETag` | `Web.Tests` |
| Abandonment | `AntiforgeryField` is empty after abandonment and mints no token | `Web.Tests` (`SimpleContextTests`) |
| Assistant | The prompt names the guard, the field and the public default; the example compiles | `Web.Tests` (`PageAuthorPromptTests`) |
| Regression | Every existing page, editor, login and render test is unchanged in substance | all four non-browser suites |

## Risks and open questions

- **A forgotten guard is a public page.** This is the model, not a bug. The mitigation is one idiom,
  the assistant prompt and the docs. An analyzer that detects "a private page without a guard" has no
  reliable definition of private; it is out of scope.
- **Authenticated `POST`s now need a token.** This is a real behaviour change for any page that
  assumed it could post while signed in. It is the safe default, and no current page relies on it.
  If it turns out to break a legitimate page, the answer is to add the field, not to weaken the rule.
- **Nullable analysis.** Pages compile with nullable disabled, so the guard followed by
  `ctx.User.Name` is warning-free today. If page compilation enables nullable later, the pattern-bound
  local is the documented form; the plan does not need a code change for that.
- **Fragments.** The server cannot preserve `#...` because it never receives it. The plan corrects
  the sketch's promise; a page that cares must restore the fragment in the browser.
- **Long return targets.** A page with a very long query produces a long `Location` header and a long
  form field. The path already arrived in the request, so this adds only the encoding overhead; no cap
  is proposed unless a real page reaches a header limit.
- **The auth cookie is visible in `ctx.Request.Cookies`.** A page can read its own session cookie.
  Page code is trusted, in-process code (DEC-40), so hiding it would be theatre; the docs should not
  pretend otherwise.
- **A page's own `Cache-Control` is overridden for authenticated responses.** That is the point, but
  it is a surprise worth stating in the architecture and the `SetHeader` documentation.
- **Sign-in on a `POST` loses the body.** A signed-out visitor who submits a private form is
  redirected and returns with a `GET`; the form data is gone. That is normal for a browser flow and
  is why the guard belongs at the top of the page, before reading or writing anything.

## Documentation and bookkeeping

- Architecture: page contract, the new authentication subsection, the cache paragraph and the
  request flow.
- Decisions: DEC-02 amended; new entries for the code opt-in, the supplied identity, the redirect,
  the CSRF rule, the cache rule and the assistant rule; DEC-125's open note updated.
- Testing: the new unit and integration coverage.
- IDEA-011 and the job index: link this plan now; close and delete the idea when the phases land,
  following the ARCH-10/IDEA-003 convention.
- Security review: revisit `docs/security/auth-cookie-review.md` for page cacheability and CSRF; no
  new secret, cookie or key is introduced.

## Folding IDEA-011

On completion:

1. Delete `docs/jobs/IDEA-011.md` into git history.
2. Add a row to the closed table in [the job index](../jobs/README.md#closed):

   > IDEA-011 — Pages opt into the existing sign-in with `ctx.User` and `ctx.RedirectToLogin()`.
   > A signed-out visitor returns to the canonical path and raw query; authenticated `POST`s carry
   > `ctx.AntiforgeryField`; authenticated responses are `no-store`. See
   > `docs/plans/archive/page-authentication.md`.

3. Remove "IDEA-011, page authentication" from the rough order, or replace it with the next job that
   moves up.

## Verification

Every phase ends with:

```bash
make check
```

which builds, runs the four non-browser suites (Core, Runtime, Web and Integration), checks formatting
and licences, runs the audit and publishes. The browser suite stays opt-in and out of the gate, as it
is today. No browser test is added here: everything in this plan is provable through the real host
with `WebApplicationFactory`, and the sign-in form itself is already covered by
`EditorAuthenticationTests`.
