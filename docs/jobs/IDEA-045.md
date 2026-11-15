<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-045 — A page, a fetch and the assistant only ever see the anonymous view

| | |
| --- | --- |
| Size | M for a token that acts as an existing account; L if the assistant is to have an account of its own that is limited in any way, because `product-one-owner` says any account is an owner and there are no roles |
| Area | Auth, Web (assistant, pages), Sdk, deploy, docs |
| Location | `src/uk.osric.asp.Web/Authentication/` (a second scheme, or a token on the cookie scheme), `src/uk.osric.asp.Web/Authentication/AuthenticationExtensions.cs`, `src/uk.osric.asp.Web/Assistant/Tools/FetchTool.cs`, `src/uk.osric.asp.Web/Pages/OutboundFetcher.cs`, `src/uk.osric.asp.Web/Pages/PageCredential.cs`, `src/uk.osric.asp.Sdk/HttpFetchRequest.cs`, `src/uk.osric.asp.Web/appsettings.json`, `deploy/uk.osric.asp.container`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

Every way a page or the assistant can read the site over HTTP is anonymous, and every interesting view
is behind the sign-in guard. `pages-outbound-fetch` says a request carries **no cookies**, so:

- the assistant's `fetch` of its own site gets the login redirect for `/pages`, `/assistant`,
  `/assistant/memory` and `/status`;
- a page fetching a guarded page of its own sees the redirect it would show a stranger;
- a browser ([IDEA-044](IDEA-044.md)), if it ever lands, inherits exactly the same limit — and the
  editor, which is the page the assistant most wants to check, is the most guarded of all.

The assistant is not short of *authority*: its tools read and write the store as the signed-in owner
the run belongs to, and `IWorkerContext.User` is that owner for a job's whole life. What it lacks is a
**view** — the rendered result of a page that only renders for someone who is signed in. That gap
matters because "did the page I just wrote work" is a question about the page a visitor sees, and for a
guarded page the visitor is the owner.

The carrier exists and the verifier does not. `Pages:Fetch:Credentials` is an operator-configured
credential a page names but never reads: the host attaches the header, only to that credential's host,
only from the page the credential is bound to, and — alone in the whole fetch path — **over plaintext
when the host is loopback** (`PageCredential.AppliesTo`). So a page can already send
`Authorization: Bearer …` to `http://127.0.0.1:8080`; what the site does not have is anything that
would accept it. Authentication is one cookie scheme over local accounts (`auth-cookie-not-identity`),
with a server-side ticket store, a security stamp compared on every request, and an antiforgery token
required before page code runs on any authenticated `POST`
(`auth-a-state-changing-form-carries-a-token`).

## Sketch

1. **A token the site accepts for a named account.** Either a second authentication scheme
   (`Authorization: Bearer`) that resolves to the same `Users` row the cookie resolves to, or a cookie
   the host sets on its own outbound request. The bearer header is the cheaper of the two: it needs no
   cookie jar, it cannot be chunked, and its absence is unambiguous.

2. **Scope, before existence.** The interesting question is not "may it sign in" but "what may it
   reach". Read-only is the honest first version: `GET` and `HEAD` on the site's own origin, and no
   `POST` — because an authenticated `POST` is antiforgery-checked before any page code runs, and a
   token that satisfies that check is a token that can change the site. Where the scope lives (a path
   prefix, a page key, a claim on the ticket) is the first open question.

3. **Only the site's own origin, only over loopback.** The token is presented to `127.0.0.1:8080` (or
   the app's name on an internal network) and to nothing else. That is the same rule
   `PageCredential.AppliesTo` already encodes, and it is what stops a credential meant for the site
   being spent on the model provider.

4. **It is not the owner's cookie.** The cookie is a server-side session with a security stamp and a
   30-day slide (`auth-sign-in-is-long-lived-and-revocable`); borrowing one would make the assistant's
   view indistinguishable from the owner's browser, revocable only by `users passwd`, and impossible to
   tell apart in a log. A token is its own row (or its own claim), issued deliberately and revocable on
   its own.

5. **Whose identity it is.** Two answers, and the cost differs sharply.
   - **The owner's account.** Cheap, and arguably no new authority at all: the run already acts as that
     account through its tools. It buys the rendered view, and nothing else.
   - **An account for the assistant.** The safe story — attribution, a separate revocation, a session
     that can be deleted without touching the owner — and nearly free of *protection*, because
     `product-one-owner` says any account is an owner and there are no roles. A second account with the
     same powers is an audit trail, not least privilege. Making it genuinely narrower is a role model,
     which is a much larger change and touches `pages-public-by-default` and every page's own guard.

6. **The no-token alternative, which solves half of it.** The host can render a page **in process** for
   the caller's identity and hand back text — no HTTP, no token, no loopback round trip. That answers
   "what does the server produce for this account" for every page, cheaply, and it does nothing for a
   browser, which needs a URL and a cookie. It is worth costing beside the token because it removes the
   credential from the common case and leaves the token for the browser only.

7. **A token is a secret in the deployment.** It belongs in configuration, never in the image, never in
   a page, never in the prompt — the same rule `Pages:Fetch:Credentials` follows — and its use is
   worth a log line with the account and the path, so "the assistant read the editor" is visible.

## Open questions

1. **Read-only, or writes too.** Reads need nothing new beyond the scheme. A write needs either the
   antiforgery pair (so the caller must first read the form, which is a scraping exercise) or an
   exemption for token-authenticated requests, which is a decision about CSRF and should be refused
   unless a page genuinely needs it.
2. **Owner's identity or the assistant's own.** See the sketch; the second is an account, and an
   account is an owner.
3. **A second scheme, or a token minted into the existing cookie scheme.** A second scheme is more code
   and makes "who authenticated this request" unambiguous; reusing the cookie scheme is less code and
   one place to get the stamp check wrong.
4. **Where the token is stored and how it is rotated.** Configuration and the image's env, like the
   provider key; or the data-protection key ring; or a row with a hash, revocable by a CLI command the
   way `users passwd` revokes a session.
5. **Whether a page may name it.** `Pages:Fetch:Credentials` is page-bound. If the token is offered as
   a credential, a page the assistant wrote can read the guarded site as the owner — which it can
   already do through `IPageCatalog`, so the question is whether the *rendered* view should be that
   easy to obtain.
6. **What it does to the browser.** If [IDEA-044](IDEA-044.md) ever gets a token, the rendered page's
   own JavaScript runs as that account. That is the whole risk of the idea, concentrated: a page the
   assistant wrote, signed in as the owner, executing script in a browser.

## Not in scope

- **Registration, password reset, a user UI.** None of it exists and none of it is wanted
  (`product-one-owner`).
- **Roles and permissions.** If they are ever wanted they are their own job; this one should not smuggle
  them in through a token's scope.
- **OIDC or an external identity provider.** `auth-cookie-not-identity` chose local accounts and cookie
  authentication deliberately.
- **A session for a page's visitors.** `IPageSession` already is that, and it is not an identity.
- **The browser itself.** [IDEA-044](IDEA-044.md).

**Origin:** the owner's note of 2026-11-15, raised alongside the browser idea: giving the assistant a way
to see the site would mean giving it something to sign in with, because the parts worth seeing are
guarded. The observation that the assistant already holds the owner's authority through its tools — and
so needs a view rather than a permission — is this job's; so is the note that the carrier
(`PageCredential`) already allows loopback plaintext.
