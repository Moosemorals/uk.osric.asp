<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# SEC-02 — Editor POST has no antiforgery protection and its flash cookie is unhardened

| | |
| --- | --- |
| Severity | High |
| Area | Web, security |
| Location | `src/uk.osric.asp.Web/EditorEndpoint.cs:14-45` |

## Problem

The editor form posts without an antiforgery token, and the endpoint does not validate one. Once
authentication exists (SEC-01), any page the owner visits can submit a form to `/edit/<path>` and
silently replace a page with attacker-chosen C#. Without authentication the endpoint is already open,
so a forged request is not even necessary — but the missing token will become the next hole the
moment SEC-01 is fixed.

The flash message cookie had default options. It now sets `HttpOnly`, `SameSite = Strict`, `Path = /`
and a 60-second `MaxAge`, plus `Secure` in production only so local HTTP work still receives it
(`EditorEndpoint.CreateMessageCookie`, fixed 2026-10-30), so the remaining gap is the token. Two tabs
saving at once still overwrite each other's message.

## Fix outline

1. Render an antiforgery token into the form and validate it on `POST`; with endpoint handlers, call
   the antiforgery service (`IAntiforgery.GetAndStoreTokens` / `ValidateRequestAsync`) directly.
   Return 400 on failure.
2. Add `RequireAntiforgery()`-style validation to the editor route (see ASP.NET Core 8+ endpoint
   antiforgery support) rather than hand-rolling a hidden field.
3. Replace the cookie with server-side state. A `POST` → `303` → `GET` flow can carry the message in
   the redirect's query string, or store it per session; the cookie adds no value for a single user
   and leaks state to the client.
4. Add tests for: missing token rejected, valid token accepted, message shown once.
