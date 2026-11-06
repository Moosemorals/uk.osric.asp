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

The findings that remain are availability problems, which is the right answer for this threat model:
the strongest attacks available to an automated client are lockouts and slow memory growth, not
compromise.

## Findings

| # | Severity | Location | Issue | Confidence |
|---|---|---|---|---|
| 1 | Medium | `src/uk.osric.asp.Web/Authentication/AuthenticationExtensions.cs:149` | Global login throttle is a single bucket; ten unauthenticated requests a minute lock the owner out indefinitely. | 9/10 |
| 2 | Medium | `src/uk.osric.asp.Web/Pages/SimpleContext.cs:68` | Logger category derived from the raw request path; the logger cache never evicts, so unique URLs leak memory permanently. | 8/10 |
| 3 | Low | `src/uk.osric.asp.Web/Pages/SimpleContext.cs:193` | The `X-` header wildcard collides with the shell's own `X-` security headers, so page code can blank them. | 9/10 |
| 4 | Low | `src/uk.osric.asp.Web/Pages/PageEndpoint.cs:54` | The 1 MB form cap only reads `ContentLength`, so a chunked POST is buffered up to Kestrel's default instead. | 9/10 |
| 5 | Low | `src/uk.osric.asp.Web/Health/HealthReportWriter.cs:32` | Anonymous `/heath` serialises exception messages and `Data` from every health check. | 7/10 |

### 1. Login throttle denies the owner any sign-in — Medium

`AddPolicy` partitions a fixed-window limiter on the constant `LoginRateLimitPolicy.Name`, so the
whole site shares one bucket of ten permits a minute. Because `UseRateLimiter()` (`Program.cs:202`)
runs before `UseAntiforgery()` (`Program.cs:205`), the requests that exhaust it need no antiforgery
token: ten bare `POST /login` a minute from one client hold the bucket permanently empty and the
owner cannot authenticate at all.

Denial of service only — no credential is exposed and no session changes hands.

This is already recorded as accepted risk 5 in [the auth cookie review](./docs/security/auth-cookie-review.md),
whose reasoning stands: partitioning on `X-Forwarded-For` in the app would widen the proxy trust
boundary for very little gain. The cheaper fix is outside the app — a `rate_limit` on `/login`
per client address in [`deploy/Caddyfile`](./deploy/Caddyfile), where the real peer address is
still visible. Caddy is already the only rootful unit and already the TLS terminator.

### 2. Logger cache grows without bound from anonymous URLs — Medium

`SimpleContext` builds its logger as `PageCategoryPrefix + request.Path` using the caller-supplied
path, and `PageEndpoint.Handle` constructs a context for every canonical, non-reserved URL
(`PageEndpoint.cs:60`) *before* the page lookup — so `GET /<random>` from an anonymous client
creates a cache entry even though no page exists.

`LoggerFactory` keeps a `MessageLogger` per distinct category for the life of the process with no
eviction. Measured on .NET 10: 200,000 distinct categories retained 44 MB, with all 200,000 still
resident afterwards — about 221 bytes each. Reaching roughly 1 GB in the 2 GB container therefore
needs on the order of 4.5 million unique URLs. That is a slow burn rather than a one-shot kill, but
it never recovers, so the process survives only until it is restarted.

The metrics path already solves this correctly by bucketing unknown pages under `(not-found)`. The
minimal fix is to log under `path.Key`, or a fixed `uk.osric.asp.Pages.not-found` category, once the
path is known not to be a page. Note the doc comment on `SimpleContext` describes the category
format as operator-facing, so changing it should be a deliberate, documented change.

### 3. Page code can blank the shell's `X-` security headers — Low

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

Fix: exclude the shell's header names from the `X-` branch, or move pages onto a distinct prefix
such as `X-Page-`, and reject empty values early.

### 4. The form size cap is bypassable — Low

`MaxFormBytes` is checked against `Request.ContentLength` (`PageEndpoint.cs:54`). A chunked request
has no `ContentLength`, so it passes, and `PageRequestSnapshot` (`PageRequestSnapshot.cs:24`) then
buffers `Request.Form` — synchronously on the request thread — up to Kestrel's default 30 MB body
limit. The comment above the constant states the intent: the body is buffered, so an oversize form
should be refused rather than held in memory.

Fix: also enforce the cap when `ContentLength` is null, and read the form with the async API so a
slow upload does not occupy a thread pool thread.

### 5. Anonymous health endpoint can disclose check detail — Low

`/heath` is anonymous by design and `HealthReportWriter` serialises `Exception?.Message` and `Data`
for every entry. Both current checks return fixed strings, so nothing leaks today; the risk is that
a future check which puts a path or a connection string in `Data` publishes it to anyone. Worth
either restricting the detail to authenticated callers or documenting the constraint on the writer.

## Known risks, unchanged

Neither of these is new; both are already reasoned about and accepted in
[the auth cookie review](./docs/security/auth-cookie-review.md):

- Accepted risk 5 — the login throttle above.
- Accepted risk 9 — `AgentConversation.ApprovalMode` defaults to `AlwaysApprove`, so model-driven
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
  `default-src 'none'`, widened to `script-src 'self'` on the two script-bearing views only.
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
- `SetContentType` accepts unvalidated text and `SetHeader` accepts empty values; Kestrel rejects a
  CR/LF at write time, so the worst case is a 500, but validating early gives a better diagnostic.
- The app container is not read-only, and Caddy runs rootful with host networking. Both are
  reasonable under rootless podman; they are the remaining container-hardening steps.

## Repeating this review

The threat model is the interesting part, so keep it written down. Start from what an anonymous
client can reach — `/login`, `/heath`, and the `{**path}` page catch-all — and follow the request to
its sink. Anything behind the editor policy is a trusted-author boundary, not an anonymous one, so
weigh it as defence in depth rather than as an exploit path.
