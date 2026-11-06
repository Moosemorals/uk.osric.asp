<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Assistant browser tests

A Playwright suite for the shell pages and the assistant, written to answer one question — why does
an answer stop mid-sentence when the model thinks for a while — and kept because it found three
defects the four NUnit suites could not.

## Shape

`test/uk.osric.asp.Web.BrowserTests`: NUnit and `Microsoft.Playwright.NUnit`, pinned in
`Directory.Packages.props`, driven in Firefox. Opt-in, with its own make targets, and deliberately not
part of `make check` (DEC-120).

- **The real host, not a test server.** `BrowserHost` runs the built app as a child process on a
  loopback port with a throwaway SQLite file, seeds one account through the real `users` command, and
  waits for `/heath`. `WebApplicationFactory` cannot serve a socket, and a browser needs one.
- **`Staging`, not `Production` and not `Development`.** Outside `Production` the cookie policy is
  `SameAsRequest`, so auth and antiforgery cookies work over plain HTTP; outside `Development` a
  failure is answered by the same error page the public site shows.
- **A scripted provider.** `StubProvider` is an `HttpListener` that answers chat-completions requests
  with server-sent events. A test scripts words per delta, a delay between them, a tool call, a
  token-limit finish, or a stall longer than a deadline, and can read back the request bodies.
- **One sign-in per run.** The suite signs in through the real form once and saves the browser state;
  each test starts from it in its own context. Ten tests signing in ten times would spend the sign-in
  throttle's allowance on the suite itself.
- **A clean console.** Every test fails on a console error or page error, which is how the icon was
  caught rendering as a black silhouette.

Fixtures that need different assistant limits start their own host; `AssistantCutOffTests` runs with a
three-second silence deadline so a stall is provable in seconds rather than minutes.

## What it covers

| Fixture | Behaviour |
| --- | --- |
| `EditorFlowTests` | Create a page, save twice, read history, open a revision, delete, recreate, scrub with the typed confirmation, refuse a wrong confirmation, see generated C#, restore a bundled seed, and a failing compile reported with its diagnostics |
| `AssistantChatTests` | Stream an answer, take the title from the first message, collapse thinking, save options, stop a run, delete a conversation |
| `AssistantStreamTests` | An answer slower than a heartbeat arrives whole, a reload mid-run picks it up, a provider that goes quiet finishes, a tool call and its answer both appear |
| `AssistantApprovalTests` | `RequireApproval` blocks a write until it is approved, a denial leaves the page alone, a pending approval survives a reload |
| `AssistantCutOffTests` | A think that keeps streaming is not cut off; a step that goes silent stops and says why (BUG-17) |
| `RocketTests` | The seeded icon keeps its colours under the page content security policy |
| `AssistantLiveProviderTests`, `AssistantLiveTruncationTests` | The same, against the configured provider: `[Explicit]`, token-gated, `make test-browser-live` |

## What it found

1. **BUG-17** — `Assistant:RequestTimeoutSeconds` was a total deadline, so a healthy long think was
   cut off mid-answer, and neither the page nor the transcript said why. The conversation page now
   renders the run's own reason after the reload, a stall is `provider_timeout`, and a message that
   stops at the token limit is stored as `truncated` (DEC-119).
2. **The seeded icon was a black silhouette.** `/rocket.svg` is a stored page served under
   `style-src 'self'`, and Firefox refused its inline `<style>` element and `style=` attributes. The
   colours are presentation attributes now, and `RocketTests` pins the computed fill. Nothing in the
   four suites could have seen this: the markup was correct, only the browser disagreed.
3. **A malformed provider payload was an internal error.** A `delta` that was not an object threw
   from a property read; it is `provider_invalid_response` now.

## Verification

```bash
make browsers            # once: download the Firefox build the suite drives
make build
make test-browser        # 28 tests, about a minute
make test-browser-live   # 2 tests against the provider in deploy/deepseek.token
```

What the suite still does not cover, and belongs to a hand pass: focus visibility, zoom and reflow,
contrast, and how the charts are read aloud (DEC-110). `HEADFUL=1` runs the browser with a window for
that kind of looking.
