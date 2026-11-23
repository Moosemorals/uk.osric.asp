<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spike — how small can the keel be?

| | |
| --- | --- |
| Question | Which routes must the shell answer, which are application views that happen to live in the shell, and what does each one need before it can be a page? |
| Answered | 2026-11-22 |
| Sources | The route table ([Program.cs](../../src/uk.osric.asp.Web/Program.cs)), the shell's own list ([ShellCatalogue](../../src/uk.osric.asp.Web/Shell/ShellCatalogue.cs)), the reserved-key rule ([ReservedPaths](../../src/uk.osric.asp.Web/Pages/ReservedPaths.cs)), the capability registry ([PageChain](../../src/uk.osric.asp.Web/Pages/PageChain.cs)), the page API allow-list ([PageApiPolicy](../../src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs)), the shipped configuration ([appsettings.json](../../src/uk.osric.asp.Web/appsettings.json)), and the page and shell documents. Sizes measured with cloc on 2026-11-22 |
| Verdict | The audit's exception has one word, *bootstrap*, where it needs three — **runtime**, **repair** and **authority** — and once they are split the keel is small: the catch-all, the compiler and loader, the store, the static files, the error responses, `/health` and `/metrics`; the seed applier with `/admin/seeds` and `/admin/scrub`; and `/login` with `/logout`. Every other route is an application view with a named blocker, not a mystery. The blockers are three things a page still cannot do — take bytes in, give bytes out, reach outside its own store — and one it cannot choose: its own budget. Two of the limits are ours, and the one blocking the dashboard is attached to the wrong object: `IHttpFetch`'s request count bounds *page-chosen* egress, while a metrics read is the *host's* egress. The size the site is felt to have is not in its views — the whole administration view surface is under 1,000 code lines — but in the machines behind them. Page-first buys editability, not smallness; deleting features buys smallness. The far end is reachable without a page holding a process: a page asks a host-side actuator, through a credential it cannot read, to promote an image the CI account already built |
| Recorded | No decision yet. This spike proposes the definition and the programme; the questions below come before either |

## The answer

The site currently answers forty-one route mappings, one of which is the page catch-all. Three of them
are the wire — the catch-all, `/health` and `/metrics` — and the static files and the reserved icon are
the runtime too. Two pairs are the way back when a page is broken, and two pairs are the way a visitor
becomes someone and stops being one. The rest are views — and a view is exactly what the platform
exists to host.

The exception in [where a view lives](../architecture.md#where-a-view-lives) is real, but it is
carrying three unrelated arguments under one word:

- **Runtime** — a page must be compiled and stored before it can run, so the compiler, the loader, the
  store and the route that reads them cannot be pages. This is a fact of a compiled language, and it
  is the keel proper.
- **Repair** — a self-hosted view can be broken by its own edit. The way back cannot be another
  instance of the thing that is broken, so the seed applier and the two recovery views stay.
- **Authority** — a page must not be able to mint identity. This is why `/login` is not a page, and it
  is a stronger reason than the one recorded today.

Everything else is **application**. Splitting the word is the whole first step, because each of the
three has a different cost and a different exit: runtime never leaves, repair leaves when the store can
repair itself, authority never leaves unless the threat model changes.

The blockers that remain are few:

1. **A page cannot take bytes in.** `PageRequest` carries query, form, headers and cookies as text, and
   `PageEndpoint` buffers a form to a 1 MiB constant before the page runs.
2. **A page cannot give bytes out.** `IPageResponse` writes strings, so an archive cannot be served as
   an archive; base64 would have to fit the 4 MiB render body.
3. **A page cannot reach outside its own store.** No file, no process, no `Microsoft.*`, so it cannot
   open a SQLite archive or read a file the image carries — by design.
4. **A page cannot choose its own budget.** Egress, body, time, embeds and storage are one size for
   every page, sized for a visitor-facing document rather than an owner's tool.

Points 1 to 3 are capability gaps with a known shape: a capability that owns the bytes, the archive or
the file. Point 4 is the interesting one, because the dashboard is blocked by it and the obvious fix —
bigger numbers — is the wrong one. A limit protects the host from **any** stored page, and the
assistant writes stored pages. What the dashboard actually needs is for the read to stop being the
page's egress and become the host's: then the bound belongs to the host read, where the host knows the
destination and can size the allowance to the work.

The far end — the keel restarting itself — does not need a page to hold a process, either. It needs a
small actuator on the host, outside the app, that a page can ask through a credential it cannot read.
That actuator is a second keel, and it belongs in `deploy/`.

## What the shell's size actually is

cloc, 2026-11-22, code lines only (blanks and comments excluded):

| Code | Lines | What it is |
| --- | --- | --- |
| `src/uk.osric.asp.Web` | 33,578 | the whole shell |
| — of which `Pages/` | 10,375 | the page runtime, store and capabilities (7,580) and the seeded pack (2,795) |
| — `Pages/Seeds` | 2,795 | the seeded pack — page sources, their sheets and scripts: `/pages`, `/help`, `/about`, `/privacy`, `/_layout` |
| — `Assistant/` | 8,071 | a product: runs, tools, streaming, memory, the shell applets |
| — `Storage/` | 6,347 | EF and its migrations |
| — `Admin/` | 2,394 | 1,410 of it the archive machine; 984 the views |
| — `Metrics/` | 904 | the dashboard: sources, catalogue, charts and view |
| `src/uk.osric.asp.Core` | 6,448 | of which `Charting` is 1,005 |
| `src/uk.osric.asp.Sdk` | 1,649 | 76 files, mostly documentation of the contract |
| `src/uk.osric.asp.Runtime` | 1,100 | the compiler and loader |
| **all product code** | **42,775** | |
| `test/` | 46,858 | more than the product |
| `docs/` | 43,290 | as much as the product |
| `tools/` | 3,491 | |

Three conclusions, and only the third is comfortable:

- The shell's **views** are small. `/admin` plus the seeds, scrub, changes and copyright views is 984
  code lines; the dashboard's own code is 677, and its drawings another 1,005 in `Core`. Porting every
  one of them to pages relocates at most about 1,700 lines, and each port needs a capability behind it,
  so the total is likely to rise.
- The shell's **machines** are big, and they are products: the assistant, the archive, the reference
  renderer, the metrics dashboard, EF's migrations. Page-first moves the view of a machine; it never
  moves the machine.
- The **record** is as large as the code. 43,290 lines of markdown against 42,775 lines of product.
  Half of what makes the site feel big is the writing about it, and the audit is part of that writing:
  a rule with four reasons and a table of exemptions generates a paragraph everywhere it is applied.

## The audit, re-read

### `/admin` — a page today

`AdminIndexEndpoint` draws exactly the values `IPageCatalog.ShellLinks()` hands a page: the one
`ShellCatalogue` table feeds the reserved keys, the assistant's prompt, the shell's menus and the page
capability. The recorded reason that "a page cannot draw the shell's own link list"
([`shell-admin-is-a-shell-route`](../decisions.md)) expired when `ShellLinks()` landed; only
availability keeps the index in the shell, and its own copy says the tools it lists "are the ones a
page cannot do for itself". That list is about to get shorter, which makes the index a page with a
short list, or a paragraph in the manual.

### `/admin/status` and `/admin/status/chart` — not keel

The dashboard is 677 code lines and 1,005 of drawings, and it is in the bootstrap row because
"diagnosing why a page did not load starts here". It does not: the error document names the compile
failure, the editor shows its diagnostics, the journal has the exception, `/health` has the probe, and
the dashboard needs both the app and Prometheus up to answer at all — when the app is down, it is
down too. What it actually is is operations, and it is the clearest case in the record of a
convenience wearing the keel's coat.

Its blockers are all budgets: 47 panels against ten fetches; a 5 s render against 2 s per query; no
process-wide cache (page storage is 256 KiB a page and 64 KiB a value, with no expiry); a pinned
4.15 MB page against a 4 MiB body; 47 panels against 32 embeds; and `Core.Charting` outside the page
API. If it is kept, the honest shape is one host-owned metrics read — the host fans out, bounds and
caches its own egress — and the page draws. If it is not read, deletion is cheaper than any port.

### `/admin/import` and `/admin/export` — transactions were the semantic half

The archive machine is 1,410 code lines, and its write half now has the shape the page contract
offers: every page is compiled before anything is written, then the writes land together
([`pages-writes-are-transactions`](../decisions.md)). What remains is bytes and SQLite:

- `PageRequest` has no file part, and `MaxFormBytes` is a **constant** of 1 MiB in `PageEndpoint`
  against `Pages:Archive:MaxUploadBytes` at 16 MiB.
- `IPageResponse` writes strings, so a SQLite database cannot be served as bytes, and base64 hits the
  render body limit.
- Opening an archive means SQLite, and no page may name `Microsoft.*` ([`pages-the-api-policy`](../decisions.md)).

One capability — accept these bytes, answer those bytes, open this archive read-only — and the view
is a page. The import already compiles outside the transaction, so the capability is a reader and the
page is the decision about what to commit; the queue, the staging and the progress route are a worker
and a page.

### `/admin/changes`, `/admin/copyright`, `/admin/reference` — build facts

They exist because a page cannot read a file the image carries. Two of them are small enough to stop
being read at all: the build already copies `Pages/Seeds` into the image, so the changelist and the
notice text can be emitted **as page source** and installed like any other seed. The reference is a
machine (684 code lines plus the XML documentation the build emits), so it is either a capability that
answers for the corpus or it is deleted.

### `/admin/seeds` and `/admin/scrub` — repair, keep

The startup seeder installs a missing seed and the applier updates one that moved, so a fresh store is
seeded without a request. The view is the owner's way back when a page will not compile, and the
editor already exposes the same writes — install a seed, adopt it, read its source — to a page that
does compile. The scrub is the one irreversible delete, so its confirmation stays an explicit owner
action. Both are repair, and repair stays.

### `/login` and `/logout` — authority, not markup

The form could be a page. The credential check and the cookie mint cannot: `PageUser` exists only so
the shell can hand a page an identity it already trusted, and a capability that signs a visitor in
would hand every stored page — including one the assistant wrote — the power to impersonate. The
recorded reason ("you cannot reach a page you must sign in for") describes the symptom; authority is
the cause, and it is the reason that survives the next refactor.

### `/assistant` — already a job

8,071 code lines, already in the backlog ([IDEA-025](../backlog/conversation-is-not-a-page.md)),
blocked by the per-account store ([`no-account-store`](../backlog/no-account-store.md)), which waits on
the storage model. Nothing to decide here until those land; the assistant is the proof that the page
contract is close to enough, because it is the largest thing left.

### The runtime — never leaves

`{**path}`, the compiler, the loader, the store, the static files, the error responses, `/health`,
`/metrics` and the icon. A page cannot be the thing that runs pages.

## The limits are the question

The blockers, with what each limit protects and whether it is the right shape:

| Limit | Shipped | What it protects | What it blocks | Right shape? |
| --- | --- | --- | --- | --- |
| `Pages:Fetch:MaxRequestsPerRender` | 10 | the host from a stored page's egress | status (47 queries) | no — it bounds the page, and the read is the host's |
| `Pages:Render:TimeoutSeconds` | 5 | the response's deadline | status on a slow Prometheus | yes, if the read is host-owned and bounded |
| `Pages:Render:Slots` | 2 | concurrent renders | a slow tool page holds one | yes — but it is why a tool page matters |
| `MaxFormBytes` | 1 MiB | a form held in memory | import (16 MiB archive) | no — it is a constant, not even configuration |
| `Pages:Render:MaxBodyChars` | 4 MiB | buffered body per render | status at its pinned worst case | yes; widening it to 8 MiB is cheap at two slots |
| `Pages:Render:MaxEmbeds` | 32 | fan-out under one slot | 47 panels as pages | yes — the design has to fit it |
| `Metrics:QueryTimeoutSeconds` | 2 | a slow upstream | — | yes |
| `Pages:Storage` | 64 KiB a value, 256 KiB a page | shared page state | a window cache (no TTL) | no — a cache is a different capability |
| `Pages:Compile:Slots`, `TimeoutSeconds` | 1, 5 s | the shared compiler | a page compiling a batch | yes — import compiles one page at a time |

Four ways to answer a limit, in order of how much they widen the blast radius:

1. **Raise the number.** Right for the body limit, wrong for egress: it makes every stored page — and
   every page the assistant writes — as heavy as the heaviest tool.
2. **A budget the page declares and the owner grants.** A page's state already holds a seed hash and
   its own keys; a declared allowance ("this page may make sixty requests") is a general mechanism,
   and it is the most machinery of the four.
3. **A capability that owns the bound.** The host knows the destination, the fan-out and the cache, so
   the limit sits where the work is. This is the right shape for metrics and for the archive, and it
   is the same test as [`pages-capabilities-are-one-seam`](../decisions.md): the host decides what a
   page may do, not the page.
4. **Split the view to fit.** Six to eight linked pages of at most ten queries each would fit every
   limit today and would stop being the dashboard: no overview, no jump bar, no accordion, and a cache
   needed to survive navigation.

The principle under all four: **a limit exists so a mistake cannot switch a guard off, not to ration a
legitimate deployment.** The dashboard is not a mistake. Either the guard is in the wrong place, or
the view is asking the wrong component.

## The far end: the keel restarts itself

A page cannot build, pull, write a file or run a process, so "push a new container build and restart"
cannot be page code. It can be a page's *request*, which is the useful half: the owner presses a
button, the site asks the host, the host does the work, the site comes back and says what happened.

The shape that keeps the page API honest:

1. **The page** shows the running build (`IBuildInfo`), the host's CI status (`/ci/status.json`, served
   by Caddy and readable with `IHttpFetch`), and one action.
2. **The request** goes to a small actuator on the host over loopback, carrying a credential from
   `Pages:Fetch:Credentials` bound to that page's key and that host. The page never sees the value,
   and the credential is useless from any other page ([`pages-outbound-fetch`](../decisions.md)).
3. **The actuator** is a systemd socket or unit in `deploy/`, running the same `deploy/deploy-host` a
   person runs: it proves the image belongs to the commit the status file names, promotes it, and
   restarts the unit.
4. **The page** polls the status file and says what happened; the request that asked completes before
   the restart, because the restart is the actuator's, not the render's.

Deliberately, this is a **promotion**, not a build. The CI account already builds and publishes, and
[`ship-the-tested-image`](../backlog/ship-the-tested-image.md) says the image promoted should be the
one the browser suite drove rather than one built again afterwards. A page asking for a build would
undo that. Until that item lands, the actuator would call the same rebuild a person's script calls
today: the difference between the two is the item, not the actuator. If a build is what is wanted, the
same channel can ask for one — the cost is that the image promoted is no longer the image that was
tested.

What it costs, and what must be decided:

- **The actuator is a second keel.** It must be deployable by a person on the host, never by the site:
  a site that can replace its own deployer has no floor left. `deploy/` stays its source of truth, and
  `make deploy` still deploys it.
- **A restart is denial of service.** The assistant writes pages; a page that can ask for a restart can
  stop the site. The evidence that the request is sane — a green status for a named commit, a matching
  image id, one request at a time — belongs to the actuator, not the page.
- **Authority is the owner's session** plus the page-bound credential, which is the narrowest pair the
  contract has. There is no role in `PageUser`, so "the owner" means "signed in", and the site is
  single-owner today.
- **Rollback is part of it.** A promotion that cannot name the image it replaced is not a release
  command.

## What this rules out

- **"The shell is big because views are hard in C#."** The views are under 1,000 code lines. The shell
  is big because it contains products.
- **"The runtime could be a page."** A page is compiled to run; the compiler is the regress that ends
  the argument.
- **"Login is a form problem."** It is an authority problem, and that is why it survives every
  simplification of the page contract.
- **"Raise every limit."** The fetch count and the form constant are the wrong shape rather than too
  low, and raising them widens what the assistant can make the site do.
- **"Porting the views shrinks the site."** It relocates them, and each port adds a capability.
- **"Status is keel because it diagnoses a broken page."** It needs the app and Prometheus to answer,
  and the error document, the journal and the editor already say what broke.

## The questions this puts to the owner

1. **Is the three-way definition the one to record** — runtime, repair, authority — with application as
   the default, and does the administration index count as repair or as application?
2. **Status:** delete it, shrink it, or keep it and give it a host-owned metrics read? If it is not read,
   deletion is the cheapest way to remove 1,682 code lines, and the presentation decisions that exist
   only for it.
3. **Limits:** are capabilities enough (metrics, archive), or is a declared per-page budget worth
   building for the general case?
4. **Import and export:** a capability that takes bytes, answers bytes and opens an archive read-only,
   with both views becoming pages?
5. **The build's facts:** does the build emit the changelist and the notices as page source, and does
   the reference become a capability or be deleted?
6. **The far end:** may a page ask the host to promote and restart? If yes, the actuator is a second
   keel owned by `deploy/`, and the first question is who may deploy the deployer.

## If it is adopted

The programme, each step its own plan and its own yes
([`product-steps-wait-for-the-owner`](../decisions.md)):

1. **The definition**: split the exception in the decision and the audit, and give every non-keel route
   a job. The name this proposes for the record is *shell-the-keel-is-what-a-page-cannot-be*, taken when
   the work merges.
2. **The limit shape**: a decision on capability-owned bounds, with the dashboard as the first customer
   and the archive as the second.
3. **The capability ports**: metrics, then the archive bytes.
4. **The view ports**: the index, import and export, the build facts, then status — or status deleted.
5. **The actuator**, once there is a page that needs it.

The order is deliberate. Steps 1 and 2 decide what is allowed; every later step is cheaper once they
exist, and no later step needs to reopen them.
