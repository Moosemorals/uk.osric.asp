<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Auth cookie security review

Reviewed 2026-11-05, after the cookie moved from a data-protected ticket in the header to a
random key with the framework's protected ticket in `AuthTickets`. The review read the changed
code against the .NET 10 `CookieAuthenticationHandler` source and the deployment in `deploy/`.
Conclusion: no critical, high or medium finding; two narrow key-lifecycle races, both fixed, and
four accepted risks. Risk 5 was later fixed, as recorded in the table.

## The scheme under review

- Cookie authentication, scheme `Cookies`, cookie `.uk.osric.asp.auth`: `HttpOnly`, `SameSite=Lax`,
  `Secure` in production, 30-day sliding expiry.
- `StoredTicketDataFormat` wraps the framework's own `TicketDataFormat` and stores its output in
  `AuthTickets`; `Protect` returns a 128-bit random key, `Unprotect` loads the row.
- `OnValidatePrincipal` reloads the `Users` row and rejects unless the security stamp matches.
- Every mutating endpoint requires an antiforgery token; login is rate limited; `returnUrl` goes
  through `LocalRedirect`.
- Caddy terminates TLS on the host and the app listens on loopback only.

## Findings

| # | Severity | Issue | Resolution |
|---|---|---|---|
| 1 | Low | `Protect` could reuse the request's key on sign-in when the row belonged to the same account, so a cookie planted for the origin (sibling-subdomain cookie tossing) could be adopted by the next sign-in, including reviving a key after a stamp rotation. | Fixed: `LoginEndpoint` marks the request with `StoredTicketDataFormat.MarkSignIn`; a marked sign-in deletes any current row and always mints a new key. Renewal keeps its key. |
| 2 | Low | A sliding renewal deferred to response start could run after a sign-out deleted the row; `Protect` then inserted a fresh row and handed the requester a live key, so a stolen cookie could outlive a logout landing in that window. | Fixed: an unmarked `Protect` whose row is missing (or was updated by another account) returns the dead key instead of inserting, so the next request fails to unprotect and the browser is asked to sign in. |
| 3 | Low | `OnSigningOut` read `Request.Cookies` rather than the cookie manager, so a chunked cookie would not have its row deleted. Unreachable with a 22-character key. | Fixed: deletion uses `CookieManager.GetRequestCookie`. |
| 4 | Low | If the sign-out delete threw, the exception escaped before the handler deleted the browser cookie. | Fixed: the delete is logged and swallowed so the cookie is still removed; a warning records that the row survives until expiry. |
| 5 | Low | Ten unauthenticated `POST /login` a minute exhaust the single global rate-limit bucket; the limiter runs before antiforgery validation. | Fixed: the limiter partitions on a bounded bucket of `Connection.RemoteIpAddress`, filled from Caddy's `X-Forwarded-For` (Caddy replaces client values and the publish is loopback only), so an address can only exhaust its own counter. |
| 6 | Low | `AuthTickets` holds live bearer keys; database read access is session takeover, and restoring an old database plus the key-ring volume revives old sessions. | Accepted: inherent to server-side sessions. Mitigate operationally with file permissions, restricted backups and treating restore as session revival. |
| 7 | Low | The hourly sweep had no exception handling, so a SQLite error stopped the host (`BackgroundServiceExceptionBehavior.StopHost`). | Fixed: the sweep logs and retries on the next tick. |
| 8 | Low | A generated key collision fails the insert rather than retrying. | Accepted: about 2^-128 per insert, and it fails closed. |
| 9 | Low | The assistant defaults to `AlwaysApprove`, so model-driven page writes execute unapproved when the agent is enabled. | Accepted: the agent is off by default and owner-scoped; changing the default is a product decision. |

## How an attacker would try to slip by

Not through the front door. Expiry is enforced by the handler (`ReadCookieTicket`), the stamp is
rechecked on every request, every state-changing route demands antiforgery, all storage access is
parameterised, forging a cookie needs both the data-protection key ring and a `Key` row, and
spoofed `X-Forwarded-*` headers cannot reach a loopback-only listener from a remote client.

The footholds were both about a cookie the attacker already possessed:

1. **Key adoption at sign-in.** Get a cookie onto the victim's browser (cookie tossing from a
   sibling subdomain is the realistic primitive), and wait for the victim to sign in as the same
   account after a password change. The old code reused the key because the account matched, the
   new ticket was written under the attacker's key, and a session the attacker had stolen before
   the password change became valid again. Fixed by making a sign-in always mint a new key.
2. **Revocation race.** Hold a stolen cookie and fire a slow request inside the renewal window so
   the deferred `Protect` runs after the victim's sign-out deleted the row. The old code inserted
   a new row with a new key and returned it to the attacker. Fixed by returning the dead key on a
   renewal whose row is gone.

Neither was a privilege escalation — both needed a precondition (cookie injection, or a stolen
cookie) — but they were the places where "logout" and "same account" carried more weight than
they had.

## Deliberate choices that are not bugs

- The wrapper is installed in an `AddOptions<CookieAuthenticationOptions>.PostConfigure` registered
  after `AddCookie`, so the framework's `PostConfigureCookieAuthenticationOptions` has already
  built `TicketDataFormat`; serialisation and the protector purposes stay framework-owned.
- `AuthTicket.ExpiresAt` is only the sweep's garbage-collection criterion. The framework rejects an
  expired ticket before `Unprotect`'s caller can use it.
- `OnValidatePrincipal` fails closed: a missing user, or a storage error surfaced through the
  authentication wrapper, is an authentication failure, not an acceptance.
- `SameSite=Lax` is safe because every mutating endpoint uses
  `RequireAntiforgeryTokenAttribute(true)` and checks `IAntiforgeryValidationFeature`; the
  antiforgery and session cookies are `Strict`.
- `/metrics` is loopback-only and Caddy answers 404 for it; `/heath` is public but returns fixed
  strings. Reserved paths keep stored pages from shadowing either.

## Verification

`make test` (unit, runtime, web and integration suites), `make format-check` and
`make license-check`. The key lifecycle is pinned by
`test/uk.osric.asp.Web.Tests/StoredTicketDataFormatTests.cs` (sign-in replaces the key, renewal
keeps it, a revoked key cannot be renewed back to life) and
`test/uk.osric.asp.Web.IntegrationTests/EditorAuthenticationTests.cs` (the cookie is a short key,
signing in again replaces it, sign-out deletes the row, stamp rotation and account removal revoke).
