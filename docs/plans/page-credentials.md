<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page credentials

| | |
| --- | --- |
| Status | Planned, not started |
| Size | Small — one SDK property, one options parser, one injection path, and their tests |
| Decisions | DEC-212 (proposed; add the row when this is implemented) |

> **Update 2026-11-10:** DEC-216 removed the `Pages:Fetch:AllowedHosts` allow-list this plan assumed.
> A credential's `Host` is now its own scope, and a credential is not widened by a host list because
> there is none. The parts below that say a host "must appear in `AllowedHosts`" no longer apply.

## The short version

A stored page needs to make an authenticated request — the assistant page calling its provider is
the first case — without the token being pasted into the page source. `IPageStorage` could hold a
string, but it is the wrong place: page state is plaintext in SQLite, `/admin/export` copies it into
an archive, there is no shell UI to set or rotate it, and the page would be able to read and emit the
value.

Instead, the operator defines a named credential in the environment file, and the page names it:

```csharp
HttpFetchResponse response = await fetch.SendAsync(new HttpFetchRequest("https://api.deepseek.com/chat/completions") {
    Method = HttpFetchMethod.Post,
    Body = json,
    Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
    Credential = "assistant",
});
```

`Credential` is a name, not a value. The host adds the configured header, only to the credential's
host, only from the bound page, and only over HTTPS unless the host is loopback. The page never sees
the token, so it cannot render it, log it, store it in page state, send it to another host, or hand
it to the assistant.

## Goals

1. A stored page can call a third-party API that needs a credential, without the token appearing in
   the template, a revision, page state, an export archive, a URL, a log line or the assistant
   transcript.
2. The page cannot read the token, so a page bug, a compromised page, or the assistant editing a
   page under prompt injection cannot exfiltrate it.
3. The credential is bound to one page and one host. A credential configured for the assistant page
   cannot be spent by another page, and cannot be sent to another host by a redirect.
4. The operator manages the value in the same place as the rest of the deployment's secrets: a
   mode-0600 environment file, read once at startup.
5. No migration, no new table and no editor screen. The site stores no page secret.

## Non-goals

- **Converting `/assistant` into a stored page.** This plan is the credential facility that page will
  use; the page itself is a separate change.
- **A per-page secret store.** A `PageSecrets` table with encryption, an editor screen, rotation and
  delete integration would be a general-purpose secret manager. The provider token is the
  operator's, not the page's, and the page has no reason to read it.
- **Reusing `Assistant:ApiKey` implicitly.** The old host assistant keeps that key until the page
  replaces it. Coupling the page fetch layer to the assistant subsystem would leave two features
  sharing one setting after the subsystem goes away.
- **Encryption at rest inside the site.** Nothing is stored at rest by this design; the value lives
  in the environment file that already holds `Assistant__ApiKey`.
- **Import and export participation.** The archive carries page source and page state (DEC-139), and
  a credential is neither. The value never enters the archive because it never enters SQLite.

## What exists today

`IHttpFetch` is the page's only egress. `SendAsync(HttpFetchRequest)` is the general form, with
`GetAsync` and `PostAsync` as default interface members over it. `PageHttpFetch` binds the capability
to one layer and the render's fetch budget; `PageFetcher` is the host implementation that validates
the request, checks the URL shape before any socket is opened, re-checks every redirect hop, and
returns every failure as an `HttpFetchFailure` value rather than throwing.

`HttpFetchRequest` already carries `Headers`, and its allow-list in `PageFetchHeaders` already
permits `Authorization` and any `X-` header. The comment on the type says a page that needs a
credential puts it in one of those headers deliberately. The missing piece is a way for the
operator to supply the value without the page knowing it.

The fetch configuration is read once at startup by `PageFetchOptions.FromConfiguration`.
`PageFetchOptions` is an internal `Web` record, and `PageFetcher` already receives the page's key for
its metric and log tags, so it can enforce a page-bound credential without new plumbing.

## Design

### Configuration

The existing section grows one optional map:

```jsonc
// appsettings.json ships the empty map; the value comes from the mode-0600 environment file.
"Pages": {
  "Fetch": {
    "Credentials": {}
  }
}
```

An operator defines a credential with environment variables:

```sh
Pages__Fetch__Credentials__assistant__Host=api.deepseek.com
Pages__Fetch__Credentials__assistant__Header=Authorization
Pages__Fetch__Credentials__assistant__Scheme=Bearer
Pages__Fetch__Credentials__assistant__Value=sk-...
Pages__Fetch__Credentials__assistant__Page=/assistant
```

Every rule is checked by `PageFetchOptions.FromConfiguration`, which throws
`InvalidOperationException` for a bad entry so the app fails at startup rather than at the first
request:

- **Name** — the configuration key. Non-empty, at most 64 characters, no control characters.
- **`Host`** — required, a bare host (no scheme, path, query or port). It is the credential's own
  scope: the header is attached only to this host, whatever else the page may fetch.
- **`Page`** — required. Canonicalised with `PagePath.Normalize` and required to be addressable. The
  credential is usable only by that exact canonical key, so a page created or edited by the
  assistant cannot spend the operator's token by naming it.
- **`Header`** — defaults to `Authorization`; must be `Authorization` or an `X-` header. The general
  request allow-list is wider than a credential needs, so a credential cannot be attached to
  `Content-Type`, `Range` or a conditional header.
- **`Scheme`** — optional. When set, it is prepended with one space: `Bearer sk-...`. Empty means the
  value is sent raw, for an `X-Api-Key` style header. It must have no CR, LF or surrounding
  whitespace.
- **`Value`** — required, non-empty, and free of CR and LF. It is never included in an exception
  message, a log line or a metric tag.

The parsed credential is an internal `Web` class, `PageCredential`: name, host, header, optional
scheme, value and bound `PagePath`. It is deliberately not a record, and its `ToString` is
overridden to print only name, host, header and page, so an accidental log line cannot contain the
value. `HeaderValue` returns the scheme-prefixed value for `PageFetcher`; nothing else reads the
value.

### The page contract

`HttpFetchRequest` gains one optional property:

```csharp
/// <summary>The name of an operator-configured credential the host attaches to this request.</summary>
public string? Credential { get; init; }
```

The doc comment says all of the following: it is a name, never a value; the page never sees the
value; the host sends it only to the credential's configured host and only from the page it is
bound to; and a page that does not know a credential name asks the operator rather than inventing
one. `null` means no credential.

This is an `Sdk` public API addition, so `PublicAPI.Unshipped.txt` gains the getter and the setter.
The `Sdk` module id changes, which invalidates every stored image through the existing `RuntimeId`
rule; no special version bump is needed.

### Host behaviour

`PageFetcher.SendAsync` resolves the credential before any socket is opened:

- Unknown name, a blank name, or a page-supplied header with the same name as the credential's
  header → `HttpFetchFailure.InvalidRequest`, and no request is made. A page that supplies its own
  `Authorization` while naming a credential with `Header=Authorization` is ambiguous, so it is
  refused rather than silently overridden.
- The credential is bound to another page, the request host is not the credential's host, or the
  target is plaintext and not loopback → `HttpFetchFailure.InvalidRequest`, and no request is made.

The header is added per hop, not once for the whole redirect chain. It is present only when that
hop's `Uri.IdnHost` equals the credential's host and the hop is `https` (or `http` to a loopback
host, matching the assistant's existing local-provider exception). A redirect to another host, or
to plaintext, is still followed under the existing redirect rules, but it is sent without the
credential. A same-host redirect keeps it. This closes the obvious redirect
exfiltration path without changing how redirects work today.

Nothing else changes in the fetch path: the render's request budget, timeout, response cap, metrics
and refusal values all apply exactly as they do now. The refusal log still names the host and the
failure, never a header or a value; a credential-specific refusal may name the credential and the
page, because neither is the secret.

### What the page author is told

The seeded `/help` page gains a short paragraph and example beside the existing `IHttpFetch` section:

> A request may name an operator-configured credential with `Credential = "name"`. The host adds
> the header; the page never sees the value. Use the name the operator gives you, not a token. An
> unknown name is an `InvalidRequest`.

`PageAuthorPrompt` gains the same rule in its "Outbound fetch" section and moves to
`page-author-v14`: never ask for or invent a credential value, never put a secret in a URL or page
source, and use a credential name only when the owner supplies it.

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/HttpFetchRequest.cs` | Add `Credential` with the "name, never value" docs |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | Add `HttpFetchRequest.Credential.get/init` |
| `src/uk.osric.asp.Web/Pages/PageCredential.cs` (new) | The credential value object, redacted `ToString`, `HeaderValue` |
| `src/uk.osric.asp.Web/Pages/PageFetchOptions.cs` | `Credentials` property, `FromConfiguration` parsing and validation |
| `src/uk.osric.asp.Web/Pages/PageFetcher.cs` | Resolve the credential, refuse bad or misbound targets, inject per hop only on the matching host and secure scheme |
| `src/uk.osric.asp.Web/appsettings.json` | Empty `Pages:Fetch:Credentials` object as a discoverable placeholder; no secret |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | One paragraph in "Outbound fetch"; `page-author-v14` |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | Teach `Credential` in the fetch section |
| `docs/pages.md` | `IHttpFetch` bullet: operator config, host-injected, page never reads the value |
| `docs/decisions.md` | DEC-212 records the choice and the rejected alternatives |
| `deploy/README.md` | Operator example for `Pages__Fetch__Credentials__*` |
| `test/uk.osric.asp.Web.Tests/PageFetcherTests.cs` | Injection, binding, refusal, redirect, plaintext, config validation, redaction |
| `test/uk.osric.asp.Web.Tests/PageFetchRenderTests.cs` | A compiled page sets `Credential` and the fetcher sees it |
| `test/uk.osric.asp.Web.Tests/PageAuthorPromptTests.cs` | Prompt mentions the rule; version assertions move to `page-author-v14` |

No database migration. `Program.cs` does not change: it already builds `PageFetchOptions` from
configuration once at startup and registers it as a singleton.

## Tests

`PageFetcherTests`:

- A credential injects `Authorization: Bearer <value>` on its host.
- The handler never sees a request for an unknown credential, a wrong page, a wrong host, a
  page-supplied `Authorization` header, or a plaintext non-loopback URL.
- A same-host redirect keeps the header; a cross-host redirect does not.
- A loopback `http` target is allowed; a non-loopback `http` target is refused.
- `PageFetchOptions.FromConfiguration` parses a full entry; missing `Host`, missing `Page`, missing
  `Value`, a bad header, a bad scheme and CR/LF in the value all throw. No exception message contains
  the value.
- `PageCredential.ToString()` does not contain the value.

`PageFetchRenderTests`:

- A page compiled and rendered through the real compiler sets `Credential = "assistant"`, and
  `FakePageFetcher` receives that name.

`PageAuthorPromptTests`:

- The prompt says the credential is a name and the page never sees the value.
- The version assertions become `page-author-v14`.

## Verification

1. `make build` — public API analyzer and nullable compile.
2. `make test-web` — fetcher, render and prompt tests.
3. `make format-check` and `make license-check` — formatting and the new file header.
4. `make check` before commit.

## Rejected options

| Option | Why not |
| --- | --- |
| Store the token in `IPageStorage` | Plaintext in SQLite, copied by `/admin/export`, no shell UI to set or rotate it, and the page reads the value and can emit it. It answers "not in the source" but not "not in the archive, the database, the page or the transcript". |
| A per-page `PageSecrets` table with an editor screen | A migration, a new store, a new capability, an editor view, encryption-key lifecycle and delete/scrub integration — for a token the operator owns. It also makes the value page-readable, which is the property this design exists to avoid. |
| Read `Assistant:ApiKey` directly from the page fetch layer | Couples page egress to the assistant subsystem, and leaves the credential unusable once the host assistant is replaced unless the configuration is renamed anyway. |
| Put the token in a header from page code | Requires the page to read the token, so any page bug or injected edit can leak it; the host cannot tell a legitimate use from an exfiltration. |
| A generic outbound proxy that adds credentials to any request | A page could ask the proxy for the credential on any URL; the host pin and page pin are what keep the token useful to one page and one provider. |

## Proposed decision row

Add to `docs/decisions.md` when implementing:

> **DEC-212** — A page names an operator-configured credential, never its value. `HttpFetchRequest.Credential` is a key into `Pages:Fetch:Credentials`, whose entries hold the host, header, optional scheme and value, and are bound to one canonical page key. The host attaches the header only to that host, only from that page, only over HTTPS unless loopback, and drops it on a redirect to another host or to plaintext. The value is never in page source, page state, a revision, an export archive or the assistant transcript, and a page that names an unknown credential gets an `InvalidRequest` rather than an exception. | [pages](pages.md#page-contract), [PageFetcher](../src/uk.osric.asp.Web/Pages/PageFetcher.cs) |
