<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Security review

Reviewed 2026-11-06 against `trunk` with a clean working tree, so the whole committed solution was
in scope rather than a diff: all four product projects, `deploy/` and the docs.

## Threat model

Commodity internet automation — scanners, mass exploit kits, credential stuffing, opportunistic
abuse — not a targeted or well-resourced adversary. Reachability from an unauthenticated request
was weighted heavily, as were bug classes that an automated attacker can find and repeat at scale:
injection, auth bypass, IDOR, XSS, SSRF, path traversal, unsafe code loading, missing authorization
on admin surfaces, secret leakage, transport and header weaknesses, and denial of service.

## Conclusion

No critical or high finding. There is no identified path for an unauthenticated attacker to get code
execution, read or write another account's data, or bypass authentication. The runtime page compiler
— the largest piece of attack surface here — held up under an adversarial reading: the policy is
default deny, `System.Object` and `System.Type` are unnameable so reflection has no entry point, the
reference set is trimmed, and the load context refuses both our assemblies and `Microsoft.*`.

The findings were availability problems, which is the right answer for this threat model: the
strongest attacks available to an automated client were lockouts and slow memory growth, not
compromise. All five have since been fixed; each section below records the fix.

## Findings

| # | Severity | Location | Issue | Confidence |
|---|---|---|---|---|
| 1 | Medium | ~~`src/uk.osric.asp.Web/Authentication/AuthenticationExtensions.cs:149`~~ | Global login throttle is a single bucket; ten unauthenticated requests a minute lock the owner out indefinitely. **Fixed** — see below. | 9/10 |
| 2 | Medium | ~~`src/uk.osric.asp.Web/Pages/SimpleContext.cs:68`~~ | Logger category derived from the raw request path; the logger cache never evicts, so unique URLs leak memory permanently. **Fixed** — see below. | 8/10 |
| 3 | Low | ~~`src/uk.osric.asp.Web/Pages/SimpleContext.cs:193`~~ | The `X-` header wildcard collides with the shell's own `X-` security headers, so page code can blank them. **Fixed** — see below. | 9/10 |
| 4 | Low | ~~`src/uk.osric.asp.Web/Pages/PageEndpoint.cs:54`~~ | The 1 MB form cap only reads `ContentLength`, so a chunked POST is buffered up to Kestrel's default instead. **Fixed** — see below. | 9/10 |
| 5 | Low | ~~`src/uk.osric.asp.Web/Health/HealthReportWriter.cs:32`~~ | Anonymous `/heath` serialises exception messages and `Data` from every health check. **Fixed** — see below. | 7/10 |

### 1. Login throttle denies the owner any sign-in — Medium — *fixed*

`AddPolicy` partitioned a fixed-window limiter on the constant `LoginRateLimitPolicy.Name`, so the
whole site shared one bucket of ten permits a minute. Because `UseRateLimiter()` (`Program.cs:202`)
runs before `UseAntiforgery()` (`Program.cs:205`), the requests that exhaust it need no antiforgery
token: ten bare `POST /login` a minute from one client held the bucket permanently empty and the
owner could not authenticate at all.

Denial of service only — no credential is exposed and no session changes hands.

This was recorded as accepted risk 5 in [the auth cookie review](./docs/security/auth-cookie-review.md),
whose reasoning was that per-client partitioning needs `X-Forwarded-For` trust. That reasoning was
half right and half wrong. Wrong: the proxy boundary was already widened for `X-Forwarded-Proto` and
`X-Forwarded-Host`, and the loopback-only publish is what bounds it, so `X-Forwarded-For` costs
nothing extra — Caddy replaces client-supplied `X-Forwarded-*` rather than passing them through.
Right: the fix needs care, because `PartitionedRateLimiter` never releases a partition — measured
with 50,000 distinct keys, all 50,000 were still resident minutes later — so keying on the raw
address would have been the same unbounded-growth bug as finding 2.

Fixed by enabling `XForwardedFor` and partitioning on a bucket of the client address:

```csharp
options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ...;
...
partitionKey: LoginRateLimitPolicy.PartitionFor(context.Connection.RemoteIpAddress),
```

`PartitionFor` folds the address into one of 1024 FNV-1a buckets, which bounds the limiter's
dictionary at a known size; two clients share a counter occasionally, which is the price of the
bound. A `rate_limit` in Caddy would have been the other option, but it is not a core directive in
Caddy 2.11 and would mean shipping a custom image for one rule.
`OneClientExhaustingItsBucketDoesNotLockOutAnotherAsync` proves the property against the real host.

### 2. Logger cache grows without bound from anonymous URLs — Medium — *fixed*

`SimpleContext` built its logger as `PageCategoryPrefix + request.Path` using the caller-supplied
path, and `PageEndpoint.HandleAsync` constructs a context for every canonical, non-reserved URL
(`PageEndpoint.cs:60`) *before* the page lookup — so `GET /<random>` from an anonymous client
created a cache entry even though no page exists.

`LoggerFactory` keeps a `MessageLogger` per distinct category for the life of the process with no
eviction. Measured on .NET 10: 200,000 distinct categories retained 44 MB, with all 200,000 still
resident afterwards — about 221 bytes each. Reaching roughly 1 GB in the 2 GB container therefore
needs on the order of 4.5 million unique URLs. That is a slow burn rather than a one-shot kill, but
it never recovers, so the process survived only until it was restarted.

Fixed by making the category static — every page logs under `uk.osric.asp.Pages`, and the page's
key moved to a `Path` scope property on each entry:

```csharp
_logger = loggerFactory.CreateLogger(PageCategory);

private void LogAt(LogLevel level, string message) {
    using IDisposable? scope = _logger.BeginScope("Page {Path}", Path);
    _logger.Log(level, "{Message}", message);
}
```

The cache now holds exactly one category however many URLs are requested. The scope keeps the page
filterable — the JSON formatter writes it as a `Path` field, so the same per-page alerting works —
while making the operator-facing category a constant and removing the request path from anything
that is cached. `TheCategoryDoesNotVaryWithThePath` in `SimpleContextTests` pins the property.
Recorded as DEC-24 in [the decisions](./docs/decisions.md) and updated in
[pages](./docs/pages.md#page-contract); `IContext`'s doc comment says the same.

### 3. Page code can blank the shell's `X-` security headers — Low — *fixed*

`s_allowedHeaders` deliberately permits a small set plus any name beginning with `X-`, so pages can
add their own `X-` headers. But the shell's own defaults are `X-Content-Type-Options`,
`X-Frame-Options` and `X-XSS-Protection` (`Http/SecurityHeaders.cs:18`, `:20`, `:26`), and
`WriteIfAbsent` (`Http/SecurityHeaders.cs:58`) respects any value the response already carries. A
page is copied into the live response before `OnStarting` runs (`PageEndpoint.cs:96`), so
`ctx.SetHeader("X-Content-Type-Options", "")` wins and drops `nosniff`.

`SetHeader` only rejects line breaks (`SimpleContext.cs:174`), so an empty value passes.
`Content-Security-Policy` is not overridable — it is not `X-` prefixed — so `frame-ancestors 'none'`
still blocks framing, which confines the practical impact to sniffing on clients that ignore the
declared type. The actor must already be able to author a page, so this is a footgun for a trusted
editor rather than an escalation.

Fixed by asking the shell whether it owns the name before the wildcard is applied, so the reserved
set cannot drift from the headers actually written, and by rejecting an empty value at `SetHeader`.
`TheShellsOwnXHeadersAreRefusedThroughTheWildcard` and `EmptyHeaderValuesAreRefused` pin both.

### 4. The form size cap is bypassable — Low — *fixed*

`MaxFormBytes` is checked against `Request.ContentLength` (`PageEndpoint.cs:54`). A chunked request
has no `ContentLength`, so it passes, and `PageRequestSnapshot` (`PageRequestSnapshot.cs:24`) then
buffers `Request.Form` — synchronously on the request thread — up to Kestrel's default 30 MB body
limit. The comment above the constant states the intent: the body is buffered, so an oversize form
should be refused rather than held in memory.

Fixed by reading a chunked form's body here, bounded, and handing it back as a seekable stream for
the form to parse — the buffering the snapshot did anyway, one cap's worth smaller, and independent
of the server. The first attempt used Kestrel's `IHttpMaxRequestBodySizeFeature`, which is the
tidiest production answer but is not enforced by the test host, so it could not be verified; the
explicit read is. `AChunkedFormPastTheCapIsRejected` covers both the unit and the real host.

### 5. Anonymous health endpoint can disclose check detail — Low — *fixed*

`/heath` is anonymous by design and `HealthReportWriter` serialised `Exception?.Message` and `Data`
for every entry. Both current checks returned fixed strings, so nothing leaked; the risk was that a
future check which puts a path or a connection string in `Data` publishes it to anyone.

Fixed by making the detail depend on who is asking: the status, check names, descriptions and
durations stay public, because the container health check needs them, and the exception message and
`Data` are written only for a signed-in caller.

## Known risks, unchanged

Both are already reasoned about and accepted in
[the auth cookie review](./docs/security/auth-cookie-review.md):

- Accepted risk 9 — `Conversation.ApprovalMode` defaults to `AlwaysApprove`, so model-driven
  page writes execute without approval. The agent is off by default and owner scoped. Defaulting new
  conversations to `RequireApproval` remains cheap defence in depth.
- `Pages:Render` abandons work by design (SEC-03): an authenticated page author can wedge render
  slots, and the container health check is the only backstop. Fine while there is one account;
  revisit before a second is created.

## Checked and found sound

Recording these so the next review knows what has already been read, not as a claim that they
cannot regress.

- **Authorization.** Every non-page route is either explicitly `RequireAuthorization(EditorPolicy)`
  or intentionally anonymous. The page catch-all cannot shadow a reserved path; `ReservedPaths` is
  enforced on both the serving and the saving side.
- **IDOR.** Assistant reads and writes are scoped by owner in the `WHERE` clause or behind an
  ownership check, including the approval update.
- **Session and cookie.** `HttpOnly`, `Secure` in production, `SameSite=Lax`, antiforgery `Strict`;
  128-bit random stored-ticket keys with the framework ticket server-side; sign-in always mints a new
  key, so no fixation; security stamp revalidated per request; logout deletes the row. PBKDF2-HMAC-
  SHA256 at 600,000 iterations, timing-equalised dummy verification for unknown users.
  `returnUrl` confined by `LocalRedirect`.
- **CSRF.** Every state-changing endpoint carries the antiforgery attribute with the pipeline
  `UseAntiforgery` behind it, and handlers fail closed when validation did not run.
- **SQL.** All access is EF LINQ, `ExecuteSqlInterpolated`/`FromSqlInterpolated`, or fixed
  `PRAGMA`/UDF text. No concatenation, no identifiers or ordering from input.
- **Output encoding.** Shell views encode with `WebUtility.HtmlEncode`; the single raw sink is
  app-generated SVG with escaped labels; the assistant script uses `textContent` only. CSP is
  `default-src 'none'`, widened to `script-src 'self'` only on a response that loads a module: the editor,
  the assistant, both admin views and a page that calls `ctx.AllowScripts()`.
- **Page compiler.** Default-deny `PageApiPolicy`, a semantic walk over every identifier, creation,
  cast, attribute and `typeof`, a shape rule confining code to the handler, banned `dynamic`,
  `unsafe`, `stackalloc` and pointers, `#line` mapping, trimmed references, and a load context that
  throws for `uk.osric.asp.*` and `Microsoft.*`. Failed images are cached, so compile errors cannot
  be used to force recompilation.
- **Injection elsewhere.** No process launching. The provider client is HTTPS-enforced with
  auto-redirect disabled and typed `JsonDocument` parsing. No `BinaryFormatter`, no polymorphic
  deserialisation from untrusted input.
- **Path handling.** Page keys are database keys only; the only filesystem read is the asset seeder
  over a fixed directory with single-segment, `GetFileName`-guarded keys. No static file middleware.
- **Secrets.** Nothing in the repository; `cloud-init.yaml` holds public keys only; provider key read
  from a 0600 env file; error pages hide exception detail from anonymous callers.
- **Transport and deployment.** App binds loopback only; Caddy terminates TLS, strips `Server` and
  `Via`, overwrites client `X-Forwarded-*`, and answers 404 for `/metrics` so the scrape endpoint is
  not public. `UseForwardedHeaders` is registered before HSTS and the security headers.

## Hardening, not vulnerabilities

- `PageRequest.Cookies` gives page code the live session cookie. `HttpOnly` stops script, not page
  C#; a page that dumps request state publishes a working key. Consider omitting the auth cookie
  from the snapshot, or documenting it alongside `WriteRaw` as author-trusted.
- `appsettings.json` has `AllowedHosts` set to `*`. Nothing builds absolute URLs from `Host`, so
  there is no host-header surface, but the canonical host is known and filtering it is free.
- Caddy `encode` applies to token-bearing HTML on `/login` and `/edit/*`. That is the BREACH
  precondition; it needs a size oracle to matter, and excluding those paths from compression is a
  one-line change.
- `SetContentType` accepts unvalidated text; Kestrel rejects a CR/LF at write time, so the worst case
  is a 500, but validating early gives a better diagnostic.
- The app container is not read-only, and Caddy runs rootful with host networking. Both are
  reasonable under rootless podman; they are the remaining container-hardening steps.

Also fixed while working on finding 4, because it sits in the same request path: a form the parser
refuses (a key or value past `FormOptions`' limits) threw `InvalidDataException` and surfaced as a
500. An anonymous client can trigger that with one request, so it is now a 400.

## Repeating this review

The threat model is the interesting part, so keep it written down. Start from what an anonymous
client can reach — `/login`, `/heath`, and the `{**path}` page catch-all — and follow the request to
its sink. Anything behind the editor policy is a trusted-author boundary, not an anonymous one, so
weigh it as defence in depth rather than as an exploit path.
