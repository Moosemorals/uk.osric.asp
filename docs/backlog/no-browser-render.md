<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot render a page in a browser

| | |
| --- | --- |
| Difficulty | persistent-salmon |
| Utility | low |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Sdk, Web (pages, jobs), deploy, docs, security |
| Location | `src/uk.osric.asp.Sdk/` (a new capability and its values), `src/uk.osric.asp.Web/Pages/`, `src/uk.osric.asp.Web/Work/`, `deploy/` (a quadlet and an internal network), `deploy/README.md`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

The assistant keeps reaching for something it has no name for: it has written a page and wants to know
whether the page *works*. Three questions hide behind that, and only the third needs a browser.

| The question | What answers it | Available today? |
| --- | --- | --- |
| Did the server return the document I meant? | The rendered response, as text | **Yes** — the assistant's `fetch` tool and `IHttpFetch`, with no host allow-list (`pages-outbound-fetch`) |
| Do the page's scripts run, and do they fail? | A JavaScript engine, its console, its failed requests | No |
| Does it look right? | Pixels, and a request the host can send that carries an image | No, and no |

The third row is not worth building and should be said out loud, because it is where this idea keeps
being imagined:

- **The host sends text parts only.** The assistant's request is a chat completion of messages and
  tool definitions; there is no image part in the request builder, so a screenshot would be a file
  nothing reads.
- **The site has no image content type.** `Pages:ContentTypes:Extensions` carries `.html`, `.js`,
  `.mjs`, `.css`, `.json`, `.svg` and `.txt`; a PNG has nowhere to be served from. SVG works because it
  is text, which is why `/rocket.svg` is a page.
- **There is nowhere to keep one.** A page storage value is capped at 64 KiB and a page's whole state
  at 256 KiB; a screenshot of a real page is larger than both.

So the capability worth arguing about is **render and report** — text facts from a real engine.

The cost of not having it is worth stating: `JavaScriptValidator` checks a script's shape and nothing
runs it; the editor's line gutter, `/vim.js` and `/assistant.js` are the site's own unverified client
code; and the accessibility assertions in `AccessibilityTests` are string rules over markup, not the
accessibility tree the browser builds (DEC-110 in the archive says so and leaves the tree to the
browser work).

## What already exists, and what it cannot do

- **`fetch` already reaches the site.** The app listens on `127.0.0.1:8080` inside its own container
  (the health check uses it), `pages-outbound-fetch` allows any `http`/`https` host, and the tool is
  told to say so. A `PageAuthorPrompt` section naming that URL is free and answers the first row of the
  table above — the one about the server's output. A page can do the same through `IHttpFetch`, at the
  cost of one of its fetch allowance and a render slot while it waits.
- **Playwright is proven on this hardware, in the tests only.** `test/uk.osric.asp.Web.BrowserTests`
  drives Firefox through `Microsoft.Playwright.NUnit`, is opt-in and is deliberately outside
  `make check` (`code-browser-suite-is-opt-in`). What is unproven is a browser under the *product's*
  constraints.
- **A long run has a home.** `IPageJobs` and workers already outlive the request, take capabilities and
  publish events, and the page reads them back.

## The shape it would take

0. **First, the free part, on its own.** A `PageAuthorPrompt` section saying the site is reachable at
   `http://127.0.0.1:8080/<key>`, that the response is the rendered document with no JavaScript and no
   cookie, and that the page's own tools are the way to reach guarded content. It is a prompt change with
   a `Version` bump, it ships and can be backed out on its own, and it answers the first row of the table
   with no container at all. If it turns out to be enough, the rest of this job is unnecessary — which is
   the cheapest possible outcome and should be tried before anything below.
1. **A report, not a picture.** `RenderAsync(key)` answers text: the final status, every console error
   and warning, every failed request and its reason, the selector set the caller asked about (present,
   count, first text), the rendered HTML up to a cap, and optionally the accessibility violations.
2. **A key, not a URL.** The host mints the URL from the site's own origin. This is the single decision
   that keeps the capability honest: a browser given a URL is an unrestricted egress path with a
   JavaScript engine, a `file://` scheme and a timeout worth abusing; a browser given a key renders the
   site and nothing else.
3. **A job, not a render.** A navigation is seconds; `Pages:Render:TimeoutSeconds` is 5 and
   `Pages:Render:Slots` is 2. The page starts a job, the worker drives the browser and publishes
   events, and the page long-polls for them — the shape `/job-toy` already demonstrates. It needs a
   concurrency cap of its own, because `Pages:Jobs:MaxConcurrent` is 2 and the assistant's own runs
   share it.
4. **A sidecar, not the app image.** The app publishes `linux-musl-arm64`; Playwright's browser builds
   are glibc, and Playwright does not support musl. The deploy story is "the image contains the publish
   output and nothing else", one pinned digest, `SOURCE_DATE_EPOCH`. So the browser is a second
   rootless quadlet with its own pinned image, `DropCapability=all`, `NoNewPrivileges=true`, no
   published port, and **an internal podman network shared only with the app** — a container network
   with no default route, so the browser can reach the site and nothing else. That is what bounds the
   address space a rendered page's script can probe.
5. **Anonymous, always.** The context carries no cookie and no session. A rendered page is the public
   visitor's view, which is the honest view for a public site. The assistant's own view of the pages
   behind a guard is [the run's view](../plans/archive/assistant-view.md), which is a token rather than a
   browser, and it is merged.
6. **CDP over `ClientWebSocket`, or Playwright as a dependency.** The host may use `ClientWebSocket`;
   Playwright in a product assembly is a new third-party dependency with a browser-binary install and
   an update treadmill. Writing a small CDP client is the choice the dependency policy points at, and
   the spike should price it.
7. **Configuration, not literals.** `Pages:Browser:MaxConcurrent`, `NavigationTimeoutSeconds`,
   `MaxResultChars`, `MaxContexts`, all required, all ranged, refused at startup with the key and the
   range named (`code-policy-values-are-configuration`).

## Costs, and where the numbers come from

| Cost | Rough size | Certainty |
| --- | --- | --- |
| A second image, pinned, plus a browser CVE cadence | hundreds of MB, and a monthly update that is a deploy | certain |
| Memory | 200–400 MB per context; two contexts is most of the app's own 2 GiB | **estimate** — the spike measures it |
| CPU at startup | ~0.3–1.5 s cold on arm64, then rendering | **estimate** — the AOT spike measured spawn costs the same way |
| The protocol | a few hundred lines of CDP, or a package and its binaries | **estimate** — the spike settles it |
| The browser's own sandbox | probably `--no-sandbox`, because the container drops every capability | **likely** — the AOT spike found `bwrap` fails under exactly that hardening |

The honest summary: a browser would be the largest and least trusted thing this project runs — bigger
than the app, and running the page's own scripts.

## Risks

- **Egress, escalated.** `pages-outbound-fetch` is bounded but unbounded in *destination*, text only,
  no cookies, no redirects to another scheme. A browser adds `file://`, `view-source:`, a JS engine that
  can probe the network it sits on, and much longer requests. The internal network is the mitigation;
  rendering a key rather than a URL is the other.
- **Identity must never creep in.** Handing a browser context the owner's session cookie would make
  every rendered page — including one the assistant wrote — signed in as the owner, with
  `AllowScripts()` on. If identity is ever wanted, it is a decision of its own, and it should be a
  token whose scope says what it may see, not a cookie borrowed from a person — which is the shape
  [the run's view](../plans/archive/assistant-view.md) took, and that token is memory-only, per run, dead
  when the run ends, and unable to become a sign-in.
- **A browser without its sandbox.** `DropCapability=all` and `NoNewPrivileges=true` are right for the
  app and almost certainly force `--no-sandbox` in the browser, which means the container *is* the
  boundary. That is acceptable — it is the same claim `pages-there-is-no-sandbox` already makes about
  page code — but it has to be written down, not discovered.
- **Rendered content is untrusted input.** The prompt already says fetched content is data, not
  instructions. A rendered page carries its own text, and the same sentence must cover it, or the
  browser becomes a large prompt-injection surface aimed at a model that can write pages.
- **Starvation.** A browse held in a render starves the slot pool; a browse queued behind
  `Pages:Jobs:MaxConcurrent` starves the assistant's runs. It needs its own queue and its own ceiling.
- **Drift.** Browser and driver move together, the digest is pinned, and the test suite's Firefox may
  diverge from the product's browser. One version, one upgrade path, or two browsers to keep current.

## Policy implications

1. **`pages-outbound-fetch`** — "one outbound path, bounded not allow-listed" gains a second path or a
   paragraph. Rendering only the site's own keys is the version that adds the least.
2. **`pages-the-api-policy`** — unchanged, and it is why the capability must be host-mediated: a page
   gets a report and never a socket.
3. **`pages-there-is-no-sandbox`** — must gain the sentence that a browser is not a boundary for page
   code; it is a second process, and a second place to attack.
4. **`sec-page-runs-in-process`** — the process boundary it leaves unbuilt is for *page code*. A browser is not that
   boundary, and no reader should be able to mistake one for the other.
5. **`code-browser-suite-is-opt-in`** — a product browser is not the test browser, but the precedent
   stands: browsers here are opt-in, outside the gate, and heavy.
6. **`product-one-owner`** — unbounded egress is justified because the only author is the owner. A
   browser borrows that argument at a much larger size.
7. **Deploy** — a new quadlet, a new pinned digest, a new internal network, and an update path that
   keeps `make image-check`'s reproducibility promise.
8. **`code-policy-values-are-configuration`** — every limit above is a required, ranged setting.

## Open questions

Each is a decision for the plan this job wants.

1. **What the report carries**, and whether the caller names the selectors or the host provides a fixed
   set. A named set is more useful and is a second query language in the contract.
2. **Accessibility violations in or out.** axe-core is a JavaScript bundle the page would have to load
   into the browser process; out of the first version is the cheap answer, and the accessibility tree is
   the one thing string tests cannot see (DEC-110).
3. **One warm context or one per job.** A warm context is fast and leaks state between renders; a fresh
   one is clean and costs a second per browse.
4. **Which origin the browser uses.** `http://uk.osric.asp:8080` by name on the internal network, or the
   loopback. The name is what makes a second, non-internal attachment impossible by accident.
5. **Whether a page or only a worker may ask.** A render has 5 s and should not hold it; a worker has a
   ceiling of nine hours and should have its own.
6. **The spike's threshold.** If a cold browse costs more than a second of the box's four cores, or more
   than a tenth of its RAM, the answer may be that the test suite is where browsers belong.

## Not in scope

- **Screenshots and anything that consumes an image.** Blocked three ways, above.
- **A browsing agent** that clicks, fills and navigates a flow. That is a different capability with a
  different risk: an agent driving a browser is a general-purpose egress with a session, and it is not
  needed to answer "does my page work".
- **Rendering third-party pages.** A key names a page in this store, and that is the whole address
  space.
- **Replacing the Playwright test suite.** The suite checks the site the release shipped; this is a page
  checking the site the owner just saved.
- **Identity.** A token a run mints for itself, which is [the run's view](../plans/archive/assistant-view.md):
  memory-only, dead with the run, read-only, and unavailable to a browser.

## Considered and rejected

- **`apk add firefox` in the app image.** The image is musl and Playwright's browsers are glibc;
  Playwright does not support musl; and "the publish output and nothing else" is the promise the image
  check proves.
- **Playwright as a product dependency.** A package plus a browser binary install plus an update
  treadmill, for something the app could ask a sidecar for over a socket.
- **Rendering inside the request that saved the page.** A 5 s deadline, 2 slots and a navigation that
  can take longer than both.
- **A screenshot store.** No image content type, and no image in the request the model receives.
- **Doing it now, because the assistant is curious.** The first row of the table is free and untried;
  it answers most of the question, and nothing measured says a browser is needed to answer the rest.

**Origin:** the owner's note of 2026-11-15, raised while reading the IDE plan: the assistant does not ask
for this in so many words, but keeps reaching for it. The comparison of what the contract already offers
— `fetch`, `IHttpFetch`, `IPageJobs` — with what a browser would add is this job's. The measurements that
would decide it are the kind the [AOT page binary spike](../spikes/aot-page-binary-spike.md) took for
the process boundary, on the same host.
