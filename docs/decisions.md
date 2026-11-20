<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Decisions

The choices that shape this site, in one place, each under a name. An entry is a rule, not a plan:
what is decided and why, with a link to the document that is authoritative for the detail. The words
the entries use — page, revision, run, step and the rest — are defined in [the glossary](glossary.md).

Most of these were settled in conversation and then recorded in a plan, a doc comment or a config
value. Where the only other record is a chat message or a code comment, the entry below is the record.

The record this one replaces is [archived](archive/decisions.md). It was numbered `DEC-01` to
`DEC-320`, with a table of superseded rows underneath, and a number is a poor name for a decision: two
branches on 2026-11-15 both took 184, because each took "the next number" alone and neither could see
the other, and one then had to move, every citation with it. A number also says nothing — `DEC-142`
tells a reader where to look in a file they have not opened, where `pages-capabilities-are-one-seam`
says what is decided and reads in a commit. The old file is frozen rather than deleted, so a `DEC-`
number written before this file keeps meaning what it meant then.

[Product](#product) · [Code](#code) · [Pages](#pages) · [Shell](#shell) · [Auth](#auth) ·
[Assistant](#assistant) · [Deploy](#deploy) · [Metrics](#metrics) · [Open questions](#open-questions)

## What a name is

`area-subject`: lowercase ASCII, hyphenated, two to six words, the area first.

**The areas are fixed.** A new one is an edit to this list in the change that needs it, because a
prefix is worth having only while there are few of them.

- `product` — what the site is for, and how the work on it is run
- `code` — where code goes, the shape it takes, and what the gate proves
- `pages` — the page contract, the page runtime, and everything a page can do
- `shell` — the routes, responses and markup the shell owns
- `auth` — who a visitor is
- `assistant` — the AI author
- `deploy` — the host, the image and the release
- `metrics` — instruments, logs and the status page

**The name states the rule, not the topic.** `pages-public-by-default`, not `pages-authentication`;
`pages-a-warning-never-stops-a-page`, not `pages-diagnostics`. A name that would fit a second decision
is too broad, and the second decision is what finds out.

**The name is the id.** Cite it in backticks — `pages-public-by-default` — in a commit, a plan, a
job, a doc comment or the seeded manual, with no prefix and no number. The heading is the anchor, so
the same thing can be a link: [pages-public-by-default](#pages-public-by-default).

## Taking a name

A name is taken when the work merges. A plan proposes the decision by the name it will carry, and the
merge writes the entry and points the plan at it; nothing is recorded here for work that has not
landed, and nothing is added for a change that decided nothing.

**The merge also closes the plan.** The same step that writes the entry sets the plan's Status to
merged and moves the plan to [`plans/archive/`](plans/archive), so the live plans are the ones still
being worked and every link into the plan is retargeted in that change. A plan left behind is a
finished step still asking to be read.

**A name is permanent.** Once it is on trunk it is never renamed and never reused, because a name is
cited from code, commits, plans and the manual, and a reader has to be able to find it. A poor name is
cheaper than a broken id.

**The check is mechanical.** `make decisions` reads this file and fails on a name that is not lowercase
and hyphenated, a name that does not begin with one of the areas above, a name used twice, an entry with
no "Recorded in" line, a retired name that is also an entry, and any document that cites a decision this
file does not have. `make check` runs it, so the names cannot drift from the record. A token that is
shaped like a name and is not one — a make target, a DOM id, the counter-examples below — is listed in
`tools/check-decisions/ignore.txt`, and a line there that nothing cites fails the check too.

**A clash is a signal, not a mistake.** Numbers collide because two people counted in private; names
collide because two decisions claimed one subject. The area bounds the space and the subject narrows
it, so a repeated heading means one of two things, and the merge decides which:

- **One decision.** Fold the second text into the entry that is already there — a merge is not the
  place for two paragraphs about one rule — and leave the plan citing it once.
- **Two decisions.** The one landing second takes a narrower name, and every citation in that branch
  is updated in the same merge. That is what makes a rename safe: it happens while nothing on trunk
  cites it yet.

A name is never disambiguated with a number. `pages-api-policy-2` is the old scheme wearing a name,
and a decision that needs a `2` needed a narrower name to begin with.

## Keeping it short

The file this replaces grew to 297 entries and 31 superseded rows, and most of that growth was a
second copy of something it already said. Three rules keep this one honest.

- **One rule, one entry.** A change that qualifies an existing entry edits it. An entry that stops
  being true is edited or deleted rather than kept beside its replacement: the reasoning lives in the
  [plan](plans) that proposed it and the history in git, so a superseded table is a third copy of a
  fact that has already changed twice.
- **Name the rule and the reason; leave the value where it is used.** A limit, timeout, quota or
  routing name is referred to by the configuration key or options type that holds it, with the
  shipped value stated only where the value *is* the decision. Two homes for a constant means one of
  them is wrong.
- **Keep the entry short.** What is decided, why in a sentence, and the alternative rejected where
  the choice would otherwise look arbitrary. The link at the end is where the detail lives.

## Product

### product-a-page-that-edits-itself

The product is a page that can edit itself: the 1999 textarea-and-save CGI experience, with
compilation, autocomplete and real error messages instead of Perl. Everything else is in service of
that.

*Recorded in [README](../README.md).*

### product-one-owner

One owner. The `Users` row **is** the authorisation: any account is an owner, and there are no roles,
no registration, no forgot-password and no user UI. Accounts are created and changed only from the
command line — `users add`, `passwd`, `list`, `remove`, wrapped by the `user-*` and `remote-user-*`
make targets — using the same binary, database and migrations as the site.

*Recorded in [editor authentication](plans/archive/editor-authentication.md#2-accounts-are-managed-by-a-cli-never-by-the-site).*

### product-no-contributions

No contributions. The repository is published to be read and adapted, not collaborated on, and the
GitHub Issues and Pull Requests tabs are off.

*Recorded in [CONTRIBUTING](../CONTRIBUTING.md).*

### product-work-is-jobs

Work is tracked in the backlog: one file per item in `docs/backlog`, named by a slug of two to five
words that state the gap. A defect's slug begins with its kind, `bug`, `sec`, `ops` or `arch`; an
idea's carries none, because an idea is what an item is when its slug names no defect, and its branch
carries the word the slug does not. An item's title is its H1, its header carries Difficulty on the
animal scale, Utility, Status, Blocked by and Plan, and the one order table is the priority — small,
high-utility work first because it never gets scheduled otherwise. A job is the host's `PageJob`; the
record does not use the word for an item. Speculative product work is an item, not a licence to widen
the task in hand. The name is kept though the record moved on, because a name is permanent.

*Recorded in [the backlog](backlog/README.md), [the plan](plans/archive/backlog-record.md).*

### product-a-difficulty-is-not-a-duration

An item's difficulty is a level on the animal scale — sleepy hamster to tiger with a toothache — and
never a guess at the calendar. No document in the record estimates hours or days: a time estimate
ignores who is doing the work, what interrupts them and what they already know, and it reads as a
promise nobody can keep. Whether an item wants a plan is the `Plan` field's business, not difficulty's,
because a plan is about coordination rather than size.

*Recorded in [the backlog](backlog/README.md#difficulty-is-a-level-not-a-duration), [the plan](plans/archive/backlog-record.md).*

### product-a-merged-plan-is-archived

A plan is finished when the work merges, and it leaves `docs/plans/` for `docs/plans/archive/` in that
same merge: the merge that writes the decision also sets the plan's Status to merged and moves it,
retargeting every link into it. The live plans are then the ones still being worked. A finished plan
that stays live asks to be read and is not — eight had merged for days, three of them still saying
proposed or waiting — and the stale Status line is what a reader trusts.

*Recorded in [CONTRIBUTING](../CONTRIBUTING.md#working-on-it), [the record](#taking-a-name), [the plan](plans/archive/backlog-record.md).*

### product-plan-before-building

A plan is written before a large change and kept as the record of the reasoning, including the options
that were rejected. Plans are not rewritten into marketing copy afterwards.

*Recorded in [plans](plans).*

### product-steps-wait-for-the-owner

A step of the programme is proposed and the owner answers, in order: the questions an item or a plan
puts to them, before anything is planned or built from it, because an answer usually changes the shape
of the work rather than qualifying it at the end; the plan, for a step that wants one, before anything
is built; and the merge, for every step, once its gate is run. The branch waits on each answer. A
green gate, a finished plan, the next step being ready and silence are evidence or expectation, never
permission: an agent does not merge its own work and does not start the step after it.

*Recorded in [the programme](backlog/README.md#a-step-stops-and-the-owner-answers), [how to work](../CONTRIBUTING.md#working-on-it).*

### product-manual-is-a-page

The author manual is the seeded page `/help`, linked from the editor and the page list. It is a page,
not a shell view, so the owner can edit and restore it like any other; a change to what a page author
can do updates it in the same change, and the plan says so.

*Recorded in [the help seed](../src/uk.osric.asp.Web/Pages/Seeds/help).*

### product-decisions-have-names

A decision has a name rather than a number, taken from its subject and its area, permanent once
merged, and cited as the name. Two branches that pick one name settle it at the merge — fold if it is
one rule, narrow the name if it is two — and a name is never disambiguated with a number. This entry
is the policy at the top of this file, and it replaces the numbering rule it was written under.

*Recorded in this file, and in [the instructions](../.github/copilot-instructions.md).*

### product-commits-are-local

Commits are local; push only when asked. A finished task is a commit, not a push.

*Recorded in conversation, 2026-11-01.*

## Code

### code-functional-core-imperative-shell

Functional core, imperative shell. `Sdk` holds the page contract and references nothing; `Core` holds
the host's pure functions and values and references nothing; `Runtime` holds Roslyn and assembly
loading; `Web` holds endpoints, hosting and storage. Data goes in and values come back — the core is
never handed a service, logger, database context or `HttpContext`.

*Recorded in [architecture](architecture.md#functional-core-imperative-shell).*

### code-nothing-public-without-a-caller

Nothing is `public` unless another product assembly binds to it, and the build enforces it: the public
API analyzers are pinned and each product assembly declares what it publishes, with `Web` declaring
nothing, so a public symbol there fails the build. Tests reach internals through `InternalsVisibleTo`,
EF-generated migrations are edited back to `internal`, and the narrowing loop is "make it `internal`,
build, and let the compiler name the hidden caller".

*Recorded in [architecture](architecture.md#visibility) and [packages](../Directory.Packages.props).*

### code-minimal-endpoints

Minimal endpoints only: no MVC controllers and no Razor views.

*Recorded in [the instructions](../.github/copilot-instructions.md).*

### code-shapes

`record` for immutable values, `class` for behaviour, `struct` only for small immutable values where
allocation or identity is measured to matter.

*Recorded in [architecture](architecture.md#shapes).*

### code-one-concern-one-collaborator

A type that implements a wide contract composes one collaborator per concern rather than collecting
members, and it is split by composition rather than by `partial`. `SimpleContext` is the case that
settled it: it keeps `IContext`, the commit gate, the layout rule and the page's logging, and hands
the body to `PageBody` and the response being assembled to `PageResponseState`. A partial would move
the members and leave one type, one constructor and one set of fields, which is a layout of a file
rather than a seam. The test is the point of it: a collaborator can be built and asserted without the
whole contract's dependencies, so a test that still needs the whole context is testing the context
and not the part.

*Recorded in [architecture](architecture.md#shapes).*

### code-n-is-small

`n` is small: prefer the obvious loop, the linear scan and the in-memory dictionary over clever
structures or caches, and optimise only what a measurement shows is slow.

*Recorded in [architecture](architecture.md#simplicity-first).*

### code-system-first

`System.*` and `Microsoft.*` first; write the rest ourselves. A third-party package needs a
justification and a pinned version in `Directory.Packages.props`, and NUnit with its adapter is the
sanctioned exception.

*Recorded in [architecture](architecture.md#simplicity-first) and [packages](../Directory.Packages.props).*

### code-no-speculative-code

Delete speculative code rather than carry it, and defer a known-but-unproven problem explicitly
instead of half-solving it. `MethodImplOptions.NoInlining` was removed rather than kept "just in
case", with a test pinning the behaviour it was assumed to provide; `ListFilesAsync` went, and route
shadowing is written down rather than worked around.

*Recorded in conversation, 2026-10-31, and [page-runtime-hardening](plans/archive/page-runtime-hardening.md#decisions-already-made).*

### code-vertical-slices

`src` is organised as vertical slices: a folder is a namespace, an endpoint sits with the types it
uses, and IDE0130 enforces the match. A test project keeps one namespace, because its fixtures reach
across slices by design.

*Recorded in [page-sdk-split](plans/archive/page-sdk-split.md#d7--web-is-vertical-slices-and-src-folders-are-namespaces).*

### code-policy-values-are-configuration

A policy value comes from configuration, never from a literal and never from a fallback in code. A
policy value is a limit, timeout, quota, retry count, cache or sweep interval, a feature switch, a
host or credential list, or a name that decides routing or authority. The shape is an options record
in `Web` with a `FromConfiguration` factory that reads every value through `ConfigurationReader`, and
`appsettings.json` is the shipped source of truth; a production override may be partial because
providers merge key by key. Every section's problems are collected into one report before it is
made, so an operator fixes a list rather than one section per restart: a `ConfigurationException`
names the key, the accepted range and the recommended value, and `Program.Main` writes it to standard
error and exits non-zero. A range is a safety net, not a straitjacket. An invariant stays in code — a
cryptographic or wire-format constant, a schema constraint, an algorithm work cap, or the floor and
ceiling that keep a configured value from switching a guard off — and a security policy may be
configured only in the direction that keeps or tightens the shipped guarantee. A number the product
fixes as its own rule is not a policy value at all, however much it looks like a limit: a password's
bounds are the rule, where a model timeout is a limit on how the site spends a resource. `Core` never
reads configuration: the shell turns a setting into a plain value and passes it in.

*Recorded in [configuration](architecture.md#configuration).*

### code-validation-at-the-boundary

An argument is validated where it enters the assembly — public methods and constructors of public
types, the `Sdk` implementations a page calls, and the endpoint handlers — and trusted everywhere
after that. `internal` and `private` members do not validate, and neither does a DI-injected
constructor, because the container is the only caller and does not pass null. A record's positional
parameters are a value the product passes around and are checked by the member that consumes them; a
record a page builds is checked on construction.

*Recorded in [architecture](architecture.md#validation), [SimpleContext](../src/uk.osric.asp.Web/Pages/SimpleContext.cs).*

### code-names-say-what-they-are

A type name does not repeat its namespace, at the start, in the middle or at the end. A namespace
names either where code lives or what code is about: a subsystem — `Assistant`, `Metrics`, `Storage`,
`Authentication`, `Health`, `Admin` — can lose its name, while a domain — `Pages`, `Users`, `Charting`,
`Text`, `Editor` — cannot, because there the qualifier is the subject. Where the bare noun collides or
identifies nothing, the qualifier stays or a domain word replaces the vertical (`RunEvent`,
`TokenUsage`, `PagePathPolicy`). Members follow the same idea: `Is*`/`Has*` for booleans, `Async` on
every `Task`, `HandleAsync` on a single-route endpoint, and no member repeating its type.

A file declares only the namespaces its own code adds. `ImplicitUsings` supplies the SDK's, a test
project takes `NUnit.Framework` as one project-wide `<Using>`, and nothing else is hoisted: a `using`
is how a reader sees what a file depends on.

*Recorded in [naming consistency](plans/archive/naming-consistency.md) and [Directory.Build.props](../Directory.Build.props).*

### code-one-gate

There is one gate: `make check`, run on demand, by `make deploy` and by `make ci` on the host after a
push to `trunk`; the `Makefile` is the only entry point — no parallel scripts and no second definition
of "green". Nothing installs a pre-push hook and there are no workflow files: the host runs the same
target from a `post-receive` hook, and the only secret is the mirror's deploy key, which lives on the
host. A machine with the repository, the SDK, `reuse` and podman can reproduce the gate. Warnings are
errors and code style is
enforced in the build rather than only in the gate, so what `make build` proves is what `make check`
proves and a cached stamp can never mean a looser build. That is why `make format-check` asks
`dotnet format` for layout and nothing else — the whitespace rules and the `IMPORTS` fixer that orders
using directives, which raises no diagnostic the compiler owns — instead of running the whole analyzer
pass a second time to hear what the build has already failed on.
`ContinuousIntegrationBuild` is on, so a PDB does not record where the checkout happened to live.

*Recorded in [Makefile](../Makefile), [Directory.Build.props](../Directory.Build.props).*

### code-analyzers-are-strict

The full `CA` rule set is on (`AnalysisMode=All`) and a warning is an error. A rule this codebase does
not follow is named in `.editorconfig` beside its reason, and the test tree has its own short list for
NUnit's fixture shape. Product assemblies emit their XML documentation, so CS1591 holds the public
surface to what is written down. The set stays strict because turning it on found real work — a leaked
cancellation source, culture-dependent generated source, calls that blocked inside async methods — not
noise to be muted.

*Recorded in [analyzers](plans/archive/analyzers.md), [testing](testing.md#the-gate).*

### code-fixtures-hold-no-deadline

A fixture's host and harness run with the widest budgets the configuration accepts, and a test that is
about a deadline passes its own value; the shipped one is never the one a fixture can lose. A fixture
that reaches the editor compiles a 31 KB page, and eight of them compile it at once, so on a loaded
machine that compile outlasts the five seconds the site ships: the fixture answered `PAGE004` and
failed for a budget nothing in it was asserting. The deadlines keep their own tests — a quarter of a
second for the compile budget, one second for the render — so a fixture's value is not a claim about
the site.

*Recorded in [testing](testing.md#nondeterminism-and-the-two-causes-it-had),
[AspApplication](../test/uk.osric.asp.Web.IntegrationTests/AspApplication.cs).*

### code-tests-are-written-first

Tests are NUnit: one behaviour per test, named for the behaviour, and written first. Anything a unit
test can prove does not belong in a browser test; the integration suite exists for routing,
middleware order, exception handling and startup migrations against the real host.

*Recorded in [testing](testing.md).*

### code-a-wedged-suite-fails-the-gate

A suite that wedges fails the gate rather than sitting in it: every suite runs under a whole-run
ceiling (`RunConfiguration.TestSessionTimeout`) and one that reaches it aborts the run instead of
passing what it managed. The ceiling is several times a healthy run — three minutes for the unit
suites, five for the integration suite, ten for the browser suite — because a ceiling that close aborts
a deploy for being slow. The unit ceiling was a minute until `Web.Tests` grew past it: a healthy run
beside a `dotnet format` took half as long again, was aborted for being slow, and reported the tests it
had run as passed before saying the run was aborted. It is not `--blame-hang`, whose collector warns on
every normal run that it wrote no sequence file.

*Recorded in [Makefile](../Makefile).*

### code-browser-suite-is-opt-in

Browser tests are a separate, opt-in project that starts the built app as a child process in Firefox,
rather than fixtures inside the integration suite: a browser engine is an external download with its
own version, and folding it in would make a fast suite slow and "the integration tests fail" mean two
different things. It stays out of `make check` — a suite that takes a minute and can flake is one
people stop running — and `make deploy` runs it before the image is built, because a deploy is rare
and is the moment it matters that the site serves a page in a browser at all. The provider is scripted
locally; the fixtures that use the real provider are `[Explicit]` and cost tokens.

*Recorded in [testing](testing.md#the-browser-suite), [assistant browser tests](plans/archive/assistant-browser-tests.md).*

### code-the-gate-checks-the-backlog

`make check` runs `make backlog`, a tool over `docs/backlog` in the shape of `check-decisions`: a slug
outside the shape, used twice or opening with a decision area, a missing or malformed field, a Status
the blockers and the plan do not say, an item missing from the order or listed twice, a row whose
difficulty, utility or status disagrees with the item, a planned row that links the wrong plan, an
order that puts an item above its blocker, a cycle, a plan that does not link back, a kinded citation
of an item that is not in the record, and a link into the record that names a file it does not have
all fail it. The lists it reads — the difficulty scale, the decision areas — are read out of their own
documents, so each has one home. A scheme nobody verifies is a scheme that drifts.

*Recorded in [the backlog](backlog/README.md#the-check), [the Makefile](../Makefile), [the plan](plans/archive/backlog-record.md).*

### code-licence-is-mit

Every file carries SPDX tags (`make license-check`), and files that cannot take a comment are listed
in `REUSE.toml`. This repository is MIT, which is a deliberate departure from the ISC default in the
shared C# instructions.

*Recorded in [REUSE.toml](../REUSE.toml), [LICENSE](../LICENSE).*

## Pages

### pages-the-contract-is-the-seam

A page is a runtime, not a renderer. `IContext` is the whole seam: every value crossing it is a plain
record in `uk.osric.asp.Sdk`, and no ASP.NET type reaches page code. `Sdk` references nothing, and it
is the only product assembly a compiled page may name, so "is this allowed in a page?" has a
structural answer. `Core` is host support that a page can neither reference nor load.

*Recorded in [pages](pages.md#page-contract), [page-sdk-split](plans/archive/page-sdk-split.md).*

### pages-names-follow-the-pipeline

Names follow the pipeline: a **page** is the HTTP resource, a **template** is the text the author edits
and saves, a **token** is a piece of it, the **generated source** is the C# generation produces, an
**image** is what compiling that source emits, and a request is **rendered** from an image.
`PageCompiler` compiles; `PageImageBuilder` generates and then compiles; `PageTemplate` is a template
with the image built from it. Nothing on the template side is called content, and nothing before the
image is called compile.

*Recorded in [pages](pages.md#compiled-pages).*

### pages-a-key-is-a-path

`PagePath` normalises and nothing else; reserved-name checks live in configuration and the editor. A
key never contains `?` or `#` — the query and fragment are not part of a page's identity — and a
request whose decoded route value contains one is answered `404`, because rewriting it would serve a
page the URL did not ask for. `PagePath` and `UserName` share one rule, `LowercaseText.Normalize`:
invariant lowercase, then compose to NFC, so the result is NFC by construction. NFC rather than NFKC,
because compatibility folding would turn `／` into a segment separator and `？` into a delimiter no key
may contain.

*Recorded in [PagePath](../src/uk.osric.asp.Core/Pages/PagePath.cs), [LowercaseText](../src/uk.osric.asp.Core/Text/LowercaseText.cs).*

### pages-root-is-a-page

The root page is a page like any other: the editor edits it, and nothing special-cases `/`.

*Recorded in conversation, 2026-10-31.*

### pages-compile-on-save

A page is compiled when it is saved, and a stored image is reused while its `TemplateHash` and
`RuntimeId` both match; anything else compiles lazily on its next request, and there is deliberately
no startup warm-up. `TemplateFingerprint` is an opaque lowercase SHA-256 — compared, never parsed —
and `RuntimeId` is `{format}:{policy}:{Sdk mvid}:{Core mvid}`, so a tighter page policy, a new contract
or a changed generator invalidates every stored image instead of running code it did not produce.

*Recorded in [pages](pages.md#compiled-pages), [TemplateFingerprint](../src/uk.osric.asp.Core/Compilation/TemplateFingerprint.cs), [PageCompiler](../src/uk.osric.asp.Runtime/PageCompiler.cs).*

### pages-what-a-diagnostic-is

A diagnostic is a severity, a position, a code and its text. The position is template space — the line
and column the author typed — generated-source coordinates never leave Roslyn, and an unknown position
is `null`, never `(0, 0)`. Codes (`TEMPLATE001`, `PAGE001` … `PAGE006`, `SCRIPT001`, `SCRIPT002`) are
allocated once and never reused, because the editor and the logs quote them, and a retired code stays
retired: `PAGE005` left with the capability alias and is not reissued.

*Recorded in [architecture](architecture.md#functional-core-imperative-shell), [compiler diagnostics](plans/archive/compiler-diagnostics.md#decisions).*

### pages-a-diagnostic-names-its-source

A diagnostic points at the thing, in the file it is in. Beside the severity, code and text of
`pages-what-a-diagnostic-is` it carries the **run** it is about and the **source** it is in, because a
start alone is half an answer: an editor cannot underline a token it has one end of, and a page that
names a library compiles diagnostics in a file the author is not looking at.

The run is two template positions — a start and an end one past the last character, as an editor's
selection is — and an absent end means the start, so a consumer that wants a line never meets a second
kind of unknown. The source is the key of the page or library the position refers to, or `null` for
the page being saved: a key is what a consumer needs to name *which* library, where a "this is a
library" flag would say only that it is one. Both survive every hop: `PageCompiler.MapDiagnostics`
maps the end Roslyn already had in hand and drops one that is not after the start, `PageDiagnostics`
copies the span and the key into the `Sdk` value as `EndLine`, `EndColumn` and `Source`, and the
editor, `/admin/errors`, `make check-pages` and the assistant's tool results each read them rather
than recomputing a range. The span is within mapped template lines: an end column is exact within one
line and approximate across a line break, because Roslyn's mapped span is per generated line.

*Recorded in [architecture](architecture.md#functional-core-imperative-shell), [pages](pages.md#compiled-pages), [MapDiagnostics](../src/uk.osric.asp.Runtime/PageCompiler.cs), [Sdk PageDiagnostic](../src/uk.osric.asp.Sdk/PageDiagnostic.cs).*

### pages-a-warning-never-stops-a-page

A warning never stops a page compiling or being served. `Success` is "no diagnostic is an error"; a
warning-only page is saved with its image, listed and served exactly as a clean one, and reported as
"Saved with warnings" by the editor, the history and the assistant, with no path treating it as a
failure.

*Recorded in [compiler diagnostics](plans/archive/compiler-diagnostics.md#decisions), [pages](pages.md#compiled-pages).*

### pages-a-compiler-fault-is-ours

A diagnostic Roslyn reports in *generated* code, with none explaining it in *author* code, is a
compiler fault: the compiler throws, the source is preserved and the host answers `500`. It is never
shown to the author as a page error.

*Recorded in [architecture](architecture.md#functional-core-imperative-shell).*

### pages-one-log-category

Every page logs under the one category `uk.osric.asp.Pages`, with the page's key on a `Path` scope
property. That spelling is operator-facing — filters and alerts are written against it. The category
cannot name the path: `ILoggerFactory` caches a logger per distinct category for the life of the
process and never evicts one, and a not-found request picks its own URL, so a category per path is
unbounded memory for an anonymous client.

*Recorded in [pages](pages.md#page-contract).*

### pages-a-page-is-nullable

A compiled page is nullable-enabled and its generated scaffold is not: `PageCompiler` compiles with
`NullableContextOptions.Enable`, so a page sees `ctx.User` as `PageUser?` exactly as the product's own
code does, and the generator writes `#nullable enable` outside each mapped region and `#nullable
disable` around every line it wrote itself, with the directive outside `#line` so the author's
positions do not move. The promise is held by a compiler test rather than by runtime policy, because
`MapDiagnostics` drops an unmapped warning and making one fatal would turn a new Roslyn release into a
`500` on a working page.

*Recorded in [pages](pages.md#compiled-pages), [ScaffoldTests](../test/uk.osric.asp.Runtime.Tests/ScaffoldTests.cs).*

### pages-a-page-has-a-state

A page has a state — `Not compiled`, `Stale`, `Compile failed`, `Compiled with warnings`, `Compiled` —
decided once by `PageImageStatus` from the head revision's image columns and the revision's own verdict
on whether it had anything to compile, judged against the running build. Every listing says the same
thing in the same words: the shell's page list, a page's `PageEntry.State` and the assistant's
`list_pages` cannot disagree, and a page with no code reads as "Stored as source" wherever it is read.
Staleness is judged on `RuntimeId` alone, because hashing every template to answer a listing is not
worth it, and a retained revision is history rather than stale. The CSS class a view marks a state with
stays in `Web`: the contract carries the fact, the view chooses how to present it.

*Recorded in [pages](pages.md#page-contract), [PageImageState](../src/uk.osric.asp.Web/Pages/PageImageState.cs).*

### pages-a-page-may-declare-types

A page declares `using` directives and types in `<%! %>` blocks, and the generator hoists each block
above the handler in template order; template order is also the using order, so a `using` after a type
is `CS1529` at the line it was written. The shape rule is "the generated handler, last, plus the page's
own type declarations": `stackalloc`, `dynamic`, pointers, function pointers and `unsafe` stay
rejected, and a page may not declare a namespace, an assembly attribute, a member outside a type or its
own handler, which keeps statics off the handler and the entry point and module initialiser out of
reach. The generated signature names the SDK fully qualified and the loader finds the handler by name,
so a declared type can neither shadow nor displace it.

*Recorded in [page declarations](plans/archive/page-declarations.md#decisions).*

### pages-the-shape-check

`TemplateShapeCheck` runs beside the compiler: `TEMPLATE003` reports a `%>` in literal text,
`TEMPLATE004` a `{` never closed across the page's code blocks, and `TEMPLATE005` a `}` with no opener,
ignoring strings, characters, comments and raw strings. Its warnings are stored with the compiler's
diagnostics, so a clean compile no longer hides them, but they never stop the page being served.

*Recorded in [assistant edit safety](plans/archive/assistant-edit-safety.md), [TemplateShapeCheck](../src/uk.osric.asp.Core/Compilation/TemplateShapeCheck.cs).*

### pages-the-api-policy

The page API is a default-deny allow-list held as data in `Core`: everything in `Sdk` is allowed and
every other product namespace is not. `System.Object` is deliberately absent, because `GetType()` would
hand a page reflection; `System.IO`, `System.Net`, reflection, threading beyond the cancellation token,
regex, `Microsoft.*` and our own assemblies are denied, and `System.*Exception` is allowed by suffix so
catching one needs no entry.

The compiler's reference set is the page SDK plus the base class library, so naming the shell, Roslyn
or `Microsoft.*` fails with CS02xx before the policy walk runs. A post-emit scan of the image's type
references was considered and dropped: it could only find what the semantic walk already reports, at
the price of false rejections for compiler-emitted helpers.

The exceptions are each deliberate. `System.Threading.Tasks.Task` is admitted as a type with ten
members denied by name — `Run`, `Factory`, `ContinueWith`, `Wait`, `WaitAll`, `WaitAny`, `Result`,
`GetAwaiter`, `Delay`, `Yield` — because each starts work the render deadline cannot stop or turns an
`await` back into a blocked thread, while the namespace stays denied so `TaskCompletionSource`,
`TaskScheduler`, `Parallel` and `ValueTask` remain `PAGE001`. The `System.Text.Json` types are
admitted, because a fetched body is text and most APIs return JSON, but the serialization attributes
are not and `System.Net` stays denied. `System.Text.Encoding`, the SHA-2 hashes, the matching HMACs,
`RandomNumberGenerator` and `CryptographicOperations` are admitted and nothing else in
`System.Security.Cryptography` is: a page may encode, hash and sign, but not manage a key or a
certificate. Widening the list moves the policy version, so every stored image recompiles on its next
request. How a type gets in, and what a refusal looks like, is `pages-admit-what-adds-no-capability`.

*Recorded in [pages](pages.md#page-api-policy), [PageApiPolicy](../src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs).*

### pages-admit-what-adds-no-capability

The list is read by one test rather than by whichever feature needed an entry last. A type is admitted
when its only effect is to compute over a value the page already holds or to shape a value the page
already returns; it is refused when it adds a way to reach a resource — a file, the network, a process,
the environment, an assembly, reflection or process-wide state. A type that bundles a reach with a
useful helper is admitted with the reaching members denied by name in `s_deniedApis`, which is what
`Task` does with `Run`, `Wait` and the rest. Additions are decided by that test, not by taste.

That admits `StringComparer`, `HashCode`, `Random`, `JsonNamingPolicy`, `JsonDocumentOptions` and the
`System.Text.Json.Nodes` namespace, and refuses `System.Environment`, whose `NewLine` would arrive
attached to `Exit`, `FailFast` and `GetEnvironmentVariable`. It refines the list rather than loosening
it: default deny stands, because the alternative was priced and rejected — a deny list over the 4,277
public types the page compiler's references expose fails open, and cannot state a rule the dangerous
type does not look like, since a page reading another page's state does it with `File`.

A refusal is one diagnostic per type. The checker reports a node's symbol and its container type, so a
member of a refused type was once refused twice; the member is kept, and it names the type beside it,
because the entry that would change the answer is the container and not the member.

*Recorded in [pages](pages.md#page-api-policy), [page api gaps](plans/archive/page-api-gaps.md), [PageApiPolicy](../src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs).*

### pages-load-isolation

`PageLoadContext` refuses `Core` and the shell's assemblies but allows `Sdk`, and **throws** rather than
returning `null`, because `null` means "ask the default context" — the exact escape the type exists to
close.

*Recorded in [pages](pages.md#load-isolation).*

### pages-there-is-no-sandbox

There is no sandbox claim. A page runs in-process with the site's identity and the whole BCL; isolation
is fencing, not a boundary, and the threat model is "avoid obvious footguns", not "resist a determined
adversary". The trims and the policy are worth their cost at that bar; the process boundary (`sec-page-runs-in-process`)
and an out-of-process worker are priced and stay unbuilt while the only page author is the owner.

*Recorded in [`sec-page-runs-in-process`](backlog/sec-page-runs-in-process.md), [page-runtime-hardening](plans/archive/page-runtime-hardening.md).*

### pages-the-execution-budget

The execution budget bounds the *response*, not the page: a bounded asynchronous wait, then `504` with
no body, `SimpleContext.Abandon` and a counted, logged abandoned thread. Slots are released at the
deadline because they bound concurrency, not work. `504` rather than `503`, because the request was
understood and the site failed to answer in time; `503` is left for "not ready", which this design has
no state to report. A render's deadline never cancels a compile — throwing the work away would leave
the page unservable — so the compile finishes and stores its image while the request answers `504`
without starting the page.

*Recorded in [pages](pages.md#page-execution-budget), [page-execution-budget](plans/archive/page-execution-budget.md#decisions).*

### pages-health-and-restarts

The health endpoint is `/health` — deliberately not `/healthz` — returning detailed JSON, pinging
storage and reporting abandoned renders, not CPU or memory, which belong to the container. It was
`/heath` until the misspelling was fixed, and the old path is not aliased: the probe, the deploy
check and the documentation are all in this repository and move together, so nothing outside it
calls the old one. The supervisor is the kill switch: the budget cannot reclaim a thread, so podman
restarts a wedged container and `StartLimitBurst` stops a crash loop. That is the whole "hard
boundary" in this design.

*Recorded in [deploy](../deploy/operations.md#resource-limits-and-restarts), [pages](pages.md#health-and-restarts).*

### pages-page-io-is-async

The page handler is asynchronous: `IHandler.HandleAsync` returns `Task`, the generated handler is
always `async`, and every I/O member `IContext` gains is `Task`-returning and takes no cancellation
token — the render owns `Aborted` and the deadline and links them per call. A synchronous twin is never
added.

*Recorded in [async page contract](plans/archive/async-page-contract.md), [IContext](../src/uk.osric.asp.Sdk/IContext.cs).*

### pages-the-response-is-one-object

What a page writes into is one object on the context: `IContext.Response`, of type `IPageResponse`.
It holds the body (`Write`, `WriteRaw`, `FlushAsync`), the settings applied to it (`SetStatus`,
`SetContentType`, `SetHeader`, `AllowScripts`), the two shapes that replace a body (`Redirect`,
`RedirectToLogin`) and the token a form carries (`AntiforgeryField`). It is contract rather than a
capability, because every page always has one; it is one member rather than ten so the buffering and
commit rule has a home, and so a helper can take an `IPageResponse` to get the output and nothing else.
The ten old members stay on `IContext` for one release as `[Obsolete]` warnings under `PAGE009` —
stored pages are compiled source and a change to `Sdk` invalidates every image — and go with that
alias (`pages-a-warning-never-stops-a-page`, `pages-what-a-diagnostic-is`). `Web`'s implementation is
`SimpleResponse`, which composes the response state and the body the [context
split](plans/archive/simple-context.md) made.

*Recorded in [pages](pages.md#page-contract), [glossary](glossary.md#response), [the plan](plans/archive/page-response.md).*

### pages-the-response-is-buffered-until-a-flush

The response body is buffered by default: status, content type and headers are applied after
`HandleAsync` completes, so a page that throws sends the error page rather than half a document. A page
opts into streaming at its first `IContext.FlushAsync`, whose first flush is the commit point; a page
that never flushes keeps the buffered all-or-nothing behaviour, the error page and the `504`. The
request's own body over 1 MiB is refused with `413` before it is read, and the buffered response is
capped by `Pages:Render:MaxBodyChars` so a loop fails quickly instead of growing the process. The cap
stops at the first flush: it still bounds what a page buffers before committing, and after the commit
the body is on the wire, bounded by the deadline and the container rather than by the cap.

*Recorded in [pages](pages.md#page-contract), [streaming](pages.md#streaming).*

### pages-answer-get-post-head

Pages answer `GET`, `POST` and `HEAD`; every other verb is `405` from routing.

*Recorded in [pages](pages.md#page-contract).*

### pages-headers-are-an-allow-list

Page headers are an allow-list: `Content-Language` and any `X-` name. `Set-Cookie`, `Location`,
`Content-Type` and framing headers are refused, so a page cannot clobber the editor's cookies;
redirects go through `Redirect` and content type through `SetContentType`. `Cache-Control`, `ETag`,
`Expires` and `Vary` left the list when a response could be per-visitor.

*Recorded in [pages](pages.md#page-contract).*

### pages-a-stored-page-is-never-cached

Every stored page answers `Cache-Control: no-store` with no validator: the endpoint drops the strong
`ETag` and the `If-None-Match` branch, and a page cannot set a caching header, because a body can be
per-visitor and neither a shared cache nor a conditional request has any business with one. A page
redirect and a signed-in response are `no-store` for the same reason, and `Vary: Cookie` is deliberately
not added: it would fragment every public page for no gain over never caching a personalised one. The
one exception is a versioned no-code asset, which is `immutable` and carries its revision hash as the
validator (`pages-a-versioned-url-promises-immutable`).

*Recorded in [page session](plans/archive/page-session.md#d6--pages-become-uncacheable), [the shell](shell.md#the-response-shapes).*

### pages-capabilities-are-one-seam

The seam that offers a page storage, fetch, the page store, a layout, jobs and the pure renderers is
called a **capability**: `IPageCapabilities`, reached through `IContext.Capabilities`. It is one
abstract member — `TryGet<T>(out T?)` — with `Get<T>` and `GetRequired<T>` as default interface members
over it. The host owns registration, which is out of the SDK entirely, and a capability is either
registered or absent, never `null`. Everything a page always has — the request, the response, logging
and identity — stays on `IContext`; the response stays there as `IContext.Response`, one member rather
than ten (`pages-the-response-is-one-object`). Something a render offers is asked for by name, so a page says
which capability it wants instead of finding it on the context.

A capability is not a *feature* — something the product has, or ASP.NET Core's request features — and
not an *extension*: a file extension, an extension method, or third-party extensibility, which the
page contract does not offer. The older spelling (`IPageFeatures`, `IContext.Features`) survived one
release as a `PAGE005` warning while stored pages were edited, and is gone: a page that still says
`ctx.Features` no longer compiles.

*Recorded in [pages](pages.md#page-contract), [capability naming](plans/archive/capability-naming.md), [glossary](glossary.md#capability).*

### pages-page-storage

`IPageStorage` is a per-page key/value store whose only handle is bound to the page's own key, so
isolation is structural rather than a check. Values are JSON round-tripped by the host, keys are ordinal
strings of 1–200 characters with no control characters, and writes are last-write-wins. The read
returns `PageValue<T>` — found-with-a-value, found-with-null, or not found — because an `out` parameter
is illegal on an asynchronous method and a separate has-key check would race.

The state lives in a `PageState` table, per path rather than per revision and with no foreign key to the
head: SQLite's enforcement depends on the connection and the deletes are explicit anyway. Editing,
restoring or reformatting a page keeps its state; deleting or scrubbing clears it, and the delete is
what stops a deleted page gaining state from a render that was already running. Each call takes its own
`StorageDbContext` from a factory, commits immediately and is cancelled by the render's deadline, with
no page-wide transaction, because the contract has no end-of-render hook and a buffered response is not
a transaction.

*Recorded in [fetch and storage](plans/archive/page-fetch-and-storage.md).*

### pages-outbound-fetch

Outbound fetch is bounded, not allow-listed: page code and the assistant's `fetch` tool may reach any
absolute `http` or `https` URL. Both use one outbound path with `GET`/`POST` only, text bodies, no
cookies and no automatic redirects; a redirect hop must still be `http`/`https`, so the scheme rule and
the redirect limit are the only destination guards. Every request carries `moosemorals.com (page)` or
`moosemorals.com (assistant)`, followed by the owner's address `fetchmoose@fluffypeople.com`, as its
`User-Agent`, set by the host and not replaceable by a page. The
page contract keeps every failure a value — status, content type, reason — so a page showing live data
can fall back without a `try`, and a 4xx or 5xx keeps its real status and is marked `HttpError` so it
is distinguishable from a success. Storage misuse is the opposite: a bad key, an oversized value, a
write on `HEAD` or to a deleted page throws when awaited, because it is a page bug and not an outcome.

*Recorded in [pages](pages.md#page-contract), [OutboundFetcher](../src/uk.osric.asp.Web/Pages/OutboundFetcher.cs).*

### pages-fetch-streams-lines

`IHttpFetch` grows one method and the outbound stream is line-oriented text:
`StreamAsync(HttpFetchRequest)` answers an `HttpFetchStream` with `Status`, `Headers`, `Failure`,
`Truncated`, `ReadLineAsync()` and `CloseAsync()`, where a null line is the end and the values say why.
It is the same egress as a fetch, so the URL, header, redirect and credential rules and the per-render
request count are the ones `SendAsync` applies. `IAsyncEnumerable` is rejected because the policy denies
it along with `ValueTask` and its disposal is easy to forget, and `Stream` or `byte[]` because they
would put `System.IO` back in a page's reach. The stream has clocks of its own, separate from the
request's: `Pages:Fetch:TimeoutSeconds` bounds the request and its headers, then the body is bounded by
`Pages:Fetch:StreamSilenceSeconds` between lines, `Pages:Fetch:StreamMaxSeconds` in total and
`Pages:Fetch:MaxStreamBytes`, and a body that hits the byte cap ends `Truncated` rather than failed. A
body that ends exactly at the cap is not truncated: the reader asks the source once more before saying
so.

*Recorded in [pages](pages.md#relaying-an-upstream-stream), [PageFetchOptions](../src/uk.osric.asp.Web/Pages/PageFetchOptions.cs).*

### pages-fetch-credentials

A page names an operator-configured credential, never its value. `HttpFetchRequest.Credential` is a key
into `Pages:Fetch:Credentials`, whose entries hold the host, header, optional scheme and value, and are
bound to one canonical page key — or to that key and every page under it when the entry's `Page` ends
in `/*`, so one token can serve a page family without being usable from anywhere. The host attaches
the header only to that host, only from a page inside the binding, only over HTTPS unless loopback, and
drops it on a redirect to another host or to plaintext. The value
is never in page source, page state, a revision, an export archive or the assistant transcript, and a
page that names an unknown credential gets an `InvalidRequest` rather than an exception.

*Recorded in [pages](pages.md#page-contract), [PageFetcher](../src/uk.osric.asp.Web/Pages/PageFetcher.cs).*

### pages-public-by-default

Pages are public by default, and a page becomes private when its own code checks `ctx.User` and calls
`ctx.RedirectToLogin()`. Only the editor and the admin tools are behind a route policy, and there are
still no roles, ownership or registration: the site is a shop window, not a members' area.

The guard is ordinary page code, not a template directive, a page column or a route policy — `if
(ctx.User is not { } user) { ctx.RedirectToLogin(); return; }`, where the `return;` already exits the
generated handler. That keeps import, export, the editor and the assistant on one source of truth and
needs no migration, at the cost that the host cannot enforce the guard: the assistant's prompt and the
manual teach it instead.

*Recorded in [the shell](shell.md#page-authentication), [page authentication](plans/archive/page-authentication.md#d1--the-opt-in-is-ordinary-page-code).*

### pages-identity-is-a-plain-value

The identity a page sees is a shell-supplied plain value: `IContext.User` is a `PageUser?` carrying only
`Name`, mapped from the principal `UseAuthentication` validated. It is not part of `PageRequest`, and a
page cannot construct one the shell believes; an authenticated identity without a `NameIdentifier` is
anonymous.

*Recorded in [page authentication](plans/archive/page-authentication.md#d2--identity-is-a-shell-supplied-plain-value).*

### pages-redirect-to-login

`RedirectToLogin` sends the visitor to `/login` with `returnUrl` built from the page's canonical key
and **raw** query, `302` for `GET`/`HEAD` and `303` for `POST`. `LocalRedirect` still decides what a
`returnUrl` may be, so a private page cannot become an open redirect, and a fragment is not preserved
because the browser never sends one.

*Recorded in [page authentication](plans/archive/page-authentication.md#d3--redirect-to-the-existing-sign-in-with-a-validated-local-return).*

### pages-content-types

A stored page's default response type is the media type its key's last extension names.
`Pages:ContentTypes:Extensions` maps an extension to a media type and `Pages:ContentTypes:Fallback`
covers every other key; both are ordinary configuration in `appsettings.json`, not a file of their own,
so a host changes the map in its own `appsettings.Production.json` and restarts rather than rebuilding
the image. The section is read once at startup and a malformed row is fatal, naming its key. The
extension is a default, not a constraint: `SetContentType` still wins, and every extension row names its
media type, so `.html` has an explicit row.

`.svg` is in the shipped map: an SVG page is the owner's own content, trusted exactly as an HTML page
is — same owner-only authoring, same buffered response, same document policy, with script refused until
a page asks for it. The seeded `/rocket.svg` keeps its explicit type.

*Recorded in [content types](plans/archive/page-content-types.md), [PageContentTypes](../src/uk.osric.asp.Core/Pages/PageContentTypes.cs).*

### pages-an-asset-is-a-page-with-no-code

An **asset** is a stored page with no C#, because the condition that makes a revision safe to pin is the
condition that makes its source its whole representation. A no-code page is never wrapped — no banner,
skip link, base sheet or title requirement, whatever the extension — so a no-code `.html` key is a
complete document its author wrote, and a page that wants the site's chrome adds a code block and
becomes a fragment. The verdict comes from generation, which already knows: `HasCode` is true when any
token other than `Text` or `Comment` appears, a token check rather than a text scan, and the builder
stops after generation when it is false, taking no compile slot and storing no image. The verdict lands
in the revision from every path that generates one and is a property of the source, so a recompiled
revision answers as it did before. A no-code revision's body is its source, less any comment it holds
(`pages-a-comment-is-not-code`), on every request: nothing is compiled, nothing is loaded and no render
slot is taken, and a test pins that the handler generated for a no-code template writes exactly its
source, so the shortcut and the long path cannot drift apart.
There is no backfill: `NULL` means *not an asset* until the revision is next saved, so an existing asset
becomes pinnable when the owner restores the seed or edits it.

*Recorded in [immutable assets](plans/archive/immutable-page-assets.md#2-an-asset-is-a-page-with-no-code), [the shell](shell.md#the-response-shapes).*

### pages-a-versioned-url-promises-immutable

`?v=<hash>` on a page's own URL serves one retained revision and never falls back: an unknown,
malformed, repeated or wrong-path hash is a `404` rather than the current page. The path stays the page,
so `ctx.Path`, relative URLs, the query, cookies, headers and the content type from the key's extension
all keep their meaning, and `v` stays in `ctx.Request.Query`; a method other than `GET` or `HEAD` on a
versioned URL is a `405` with `Allow: GET, HEAD`.

The hash's job is selection and the verdict's job is the promise. A versioned *asset* answers
`Cache-Control: public, max-age=31536000, immutable` with a strong `ETag` that is the revision hash, and
`If-None-Match` answers `304`; `public` because a C#-free page cannot read identity or write a session,
a year because that is what `immutable` is worth claiming. A `v` naming a revision that is not an asset
still serves it through the ordinary render path — executed, wrapped and `no-store` — so a historical
revision is as addressable as a stylesheet while promising nothing. A request without `v` stays
`no-store` for every page: tagging is what makes a URL cacheable, and a browser keeping an old copy
under a URL that no longer means it is the whole hazard. The token is the stored revision hash itself,
64 lowercase hexadecimal characters, matched by exact ordinal equality, because a shorter token would
have bought a few bytes at the price of a canonicalisation rule. Versioned links are written where the
shell owns the markup — the document builder's URL resolver, `ShellDocument.BeginAsync`, and
`ctx.AssetUrl(href)` for a live no-code page — because markup inside a body cannot be rewritten after
the fact; only a bare site-relative path is touched, so a fallback link keeps working when an asset is
missing. Deleting a page hides its public URL and keeps its versioned ones answering, because the hash
still names a row; a scrub removes the rows and those URLs `404`. Neither recalls a copy a browser or a
proxy already holds, so the manual and the operations notes say that rotating an exposed secret and
cleaning external copies are separate work. Compression stays Caddy's job and the app writes no
`Vary`.

*Recorded in [immutable assets](plans/archive/immutable-page-assets.md#1-v-addresses-a-retained-revision-and-never-falls-back), [RevisionHash](../src/uk.osric.asp.Core/Pages/RevisionHash.cs).*

### pages-one-base-sheet

The one base sheet is `/site.css`: the document's shape — the visually hidden utility, the skip link,
the listing and the form grid — and the site's look, in a single seeded sheet linked before the view's
own. The shape rules lead the file, a look rule of equal specificity can follow and override one, and a
view's sheet still comes last, so a view exception can override a structural rule without a more
specific selector. A page controls its own stylesheets — none, one or many — with `AddStylesheet(href)`,
and an href that is not site-relative is refused with an exception rather than left for
`style-src 'self'` to block silently. A wrapper folds the page's sheets beside its own by putting them
where its document links them; an embed's sheets are the caller's to fold; the seeded `/_layout` and the
`Site.Document` library keep the order — `/site.css`, the wrapper's own, then the page's — and link an
exact duplicate once (`pages-a-page-inside-a-page-is-a-capture`).

*Recorded in [site.css](../src/uk.osric.asp.Web/Pages/Seeds/site.css), [the shell](shell.md#the-document-shell).*

### pages-a-layout-is-a-capability

A page served inside another page reaches its slots through `IContext.Capabilities` as `IPageLayout` —
`Content`, `Title`, `Head`, `Stylesheets`, `AddHead` and `AddStylesheet` — not through new members on
`IContext`. It is a set of slots a render offers, which is what the capabilities seam is for, and
`IContext` and `PageResponse` are unchanged. `Title` is a `string?` that starts unset, and `Content`
is the layer inside this one when this page wraps another. The slots are the layer's own: the host
composes nothing behind the outer page's back (`pages-a-page-inside-a-page-is-a-capture`). The content
owns the response; a wrapper and an embedded page are markup (`pages-one-render-three-roles`).

*Recorded in [site navigation](plans/archive/site-navigation.md#d2--the-layout-is-a-capability-not-more-icontext).*

### pages-one-request-one-chain

A page declares the page or pages that wrap it, innermost first, and a page that declares none is a
fragment served exactly as it wrote itself (`pages-a-page-chooses-its-layout`). A redirect, a non-HTML
content type and a no-code page are never wrapped. A default wrapper that cannot be served is skipped
with a warning and a `page.layout.fallback` count; a wrapper the page *named* is an error naming the
key.

One request renders its whole chain under one render slot, one deadline, one fetch allowance, one body
allowance, one write and one antiforgery pair, with an ancestor stack and
`Pages:Render:MaxLayoutDepth`. Each layer keeps its own `ctx.Path` — and so its own storage handle —
and shares `ctx.Request`. The chain is entered by the page, so a guard's early return is still wrapped,
and the host runs it after the handler returns.

*Recorded in [site navigation](plans/archive/site-navigation.md#d3--default-opt-out-and-what-is-never-wrapped).*

### pages-layouts-are-markup

A layout is pure markup: it may not set the status, the content type or a response header, and an
attempt is ignored and logged. `AllowScripts` is the one response setting it shares, as a union with the
content's, because its script is part of its chrome. The page store's read is offered to every layer and
the writer is not, because a layout that saved itself would append a revision on every page it wraps.
An embedded page is the same: it owns no response, though a redirect from one is refused rather than
ignored, because the ordinary "if no user, redirect to login" guard must not vanish into an empty box.
A wrapper must have code: a no-code page has no way to read `Content`.

*Recorded in [site navigation](plans/archive/site-navigation.md#d4--the-chain-one-request-one-budget-several-renders).*

### pages-a-page-chooses-its-layout

A page declares the page or pages that wrap it, innermost first, with `IPageComposition.Wrap` — and the
host does not wrap by default. `Wrap()` names `Pages:Layout`, so a page does not hard-code a key the
owner can move; `Wrap(key, request)` names another page and the request it runs against; each call adds
a layer outside the last. It is a declaration rather than a handover because the host runs the chain
after `HandleAsync` returns, so a guard's early `return;` is still wrapped and the page can set its
status after writing its body. A page that declares nothing is served exactly as it wrote itself — a
fragment — and `IPageLayout.Layout` is gone.

*Recorded in [page composition](plans/archive/page-composition.md#d1--ipagecomposition-is-one-capability-with-four-calls), [layouts](shell.md#layouts).*

### pages-an-embed-is-a-render

An embedded page is a render of the whole page: its body, every wrapper it declares, its own key,
storage and session, the caller's identity, one budget and one slot, and no response of its own. There
is no embed-specific wrap suppression, and no per-call boundary: a page that should be a fragment
declares no wrapper. It is offered the capabilities a content page has, including the write and the
jobs, because the authority is the caller's identity; it may not redirect, because it has no response.
One render still writes once, however many layers it has.

*Recorded in [page composition](plans/archive/page-composition.md#d4--one-layer-renderer-and-the-wrap-is-the-policy-over-it), [composition](shell.md#composition).*

### pages-a-composed-page-gets-a-request

An embed, a capture and a wrap all run the `PageRequest` they are given, with `null` meaning the
caller's; `Path` is always the visitor's page, and the request's query is the page's input. There is no
separate options type. A copy-and-change helper is an extension method on the request —
`WithQuery`, `WithAnotherQuery`, `WithoutQuery` — so the sealed record keeps its shape and its own
surface stays the values it carries.

*Recorded in [page composition](plans/archive/page-composition.md#d2--the-input-is-a-pagerequest-not-an-options-type), [composition](shell.md#composition).*

### pages-a-capture-returns-the-body

An embed returns a `PageFrame` — title, head, sheets and script permission, never the body, which is
already written — and a capture returns a `PageCapture`: the frame beside the body. The captured page's
response stays withheld, so its status, headers and content type never reach the caller and a capture
that would redirect refuses. The result is a value rather than the live `IPageLayout`, because that is
the capability a layer runs with, not the material it produced.

*Recorded in [page composition](plans/archive/page-composition.md#d8--capture-is-the-same-render-returned), [composition](shell.md#composition).*

### pages-a-page-inside-a-page-is-a-capture

A page rendered inside another yields a value — a `PageCapture` for a wrapper, a `PageFrame` for an
embed — and the outer page folds what it wants, whether it asked for the inner page with `EmbedAsync`
or received it with `Wrap`. The host composes nothing behind the outer page's back: the title, head and
sheets of the layer inside are the caller's to fold, and the one thing the host still unions is the
script permission, so a document that carries a body cannot lose it.

*Recorded in [page composition](plans/archive/page-composition.md#d5--the-wrapper-folds-the-contents-capture-it-does-not-get-the-response), [layouts](shell.md#layouts).*

### pages-one-render-three-roles

Content, layout and embedded are one role on the response and on the capability offer, not a boolean
beside a special case. Content owns the response and may stream; layout and embedded are markup, so
their status, content type and header settings are ignored and logged, neither may stream, and an
embedded page's redirect is refused. The write, the jobs and a writable session are offered to content
and embedded and not to layout, because a layout runs once per page it wraps.

*Recorded in [page composition](plans/archive/page-composition.md#d3--the-layer-role-replaces-islayout), [layouts](shell.md#layouts).*

### pages-the-wrapper-owns-the-title

`IPageLayout.Title` is a `string?` that starts unset, and the wrapper decides what a missing title
means: the seeded `/_layout` uses the page's title when it has one and the site's own otherwise. The
host no longer refuses a chain whose page forgot, so the error is a wrapper's decision rather than a
host rule.

*Recorded in [page composition](plans/archive/page-composition.md#d6--ipagelayout-loses-layout-and-body-gains-content), [layouts](shell.md#layouts).*

### pages-the-document-is-the-shells

One document shell, and it is the shell's. `ShellDocument` owns the document's policy — the language,
charset, viewport, title, base sheets and their order, the icon, the skip link, the banner, the `main`
landmark and the close — because that is what makes one writer worth having and stops a seeded layout
and a shell view disagreeing. It owns no *response* policy: status, content type, security headers,
caching and the body cap stay with the endpoint. A view cannot forget the landmark, and the head and
the sheet order are written once instead of in eight places; `PageLayout` keeps only fragments — the
gutter, the diagnostics block, a nav.

The skip link and the banner are the builder's: `ShellDocument.Banner` writes the skip link and the
`<header>` around a nav, so "the skip link is the first focusable element and targets `#main`" is a
property of the builder rather than a habit of each caller. The shell's own document is deliberately
not the stored layout, because the error, health, sign-in and admin routes must answer when the page
pipeline is broken. A stored page is served in a document the seeded layout writes with a seeded
library: `/_lib/document` declares `Site.Document` for the layout to call, and a test compares the
document shape the two write.

*Recorded in [the shell](shell.md#the-document-shell), [page libraries](plans/archive/page-libraries.md#d11--the-seeded-layout-calls-a-library-document).*

### pages-markdown

Markdig is referenced by `Runtime` and no other product assembly; a page reaches it through
`IMarkdownRenderer` in `Sdk` and the capability seam, not by naming the package. The page profile is
CommonMark plus pipe tables and auto-links, and raw HTML passes through, because the author can already
write HTML in the template and the renderer is therefore not a sanitiser. Both profiles use
`AccessibleTableRenderer`, which writes `<th scope="col">` and no inline style, because the site's CSP
forbids inline styles and the shell's accessibility rules require a scope on every header cell.

Assistant replies are Markdown by default, with no `render_markdown` tool. The transcript uses a second,
restricted profile: no raw HTML, images, autolinks or unsafe URL schemes; site-relative or `http(s)`
links with `rel="nofollow noopener noreferrer"`; pipe tables; and every heading flattened to `h3`,
because the transcript's own heading is an `h2` and a model-chosen level could skip one. A step streams
as plain text and becomes formatted when `message.completed` arrives, through an authenticated content
route that renders the stored message with the same profile as the reload.

*Recorded in [markdown](plans/archive/markdown.md), [LinkPolicy](../src/uk.osric.asp.Core/Markdown/LinkPolicy.cs).*

### pages-markdown-options-are-per-call

A page chooses the renderer's features for one call: heading anchors, auto-links and pipe tables are
each a boolean on `MarkdownOptions`, and `ToHtml(markdown, options)` renders that document with them
while the shared capability stays immutable. Heading anchors are on by default, because a heading
without an `id` cannot be linked to, and a repeated heading gets a numbered suffix so both anchors
resolve. Markdig's auto-link option stays off, so `[Heading]` is not silently turned into a link. Raw
HTML has no switch: the template can already write it, and an option that looked like it removed HTML
would invite a page to trust the renderer with a visitor's text.

*Recorded in [pages](pages.md#page-contract), [MarkdownOptions](../src/uk.osric.asp.Sdk/MarkdownOptions.cs), [MarkdownRenderer](../src/uk.osric.asp.Runtime/MarkdownRenderer.cs).*

### pages-diff

The diff is the site's own line-based implementation: `LineDiff.Compare` trims the common prefix and
suffix, runs Myers' algorithm with three lines of context and a bounded edit distance, normalises line
endings, and past its work cap shows the middle as one removed block and one added block rather than
failing or calling a process; `UnifiedDiff` writes the same result as text. There is no package and no
external executable.

`IDiffRenderer` is a pure capability like `IMarkdownRenderer`, reached through the seam: it escapes
every source line and writes semantic, inline-style-free markup, so a page may pass visitor or fetched
text. The same renderer offers `ToUnifiedDiff` to the assistant's read-only `diff_page` tool, which
returns the text and the editor link; the transcript does not render a tool result as HTML. The editor
compares two retained revisions on a `GET` (`cmd=diff&from=<hash>&to=<hash>`): both hashes are validated
as 64 hex characters and must belong to the page, the view compiles and writes nothing, a malformed link
is a `400` and an unknown revision a `404`.

*Recorded in [page diff](plans/archive/page-diff.md), [LineDiff](../src/uk.osric.asp.Core/Diff/LineDiff.cs).*

### pages-the-store-is-read-through-one-seam

Everything that reads the page store reads it through one seam: `PageStoreReader` answers the tree, the
sources in a scope, one key, a page's history and one revision of one path, and the assistant's tools and
its virtual shell both ask it. It sits over `PageEditor` and `PagePathPolicy` and is constructed where
it is used, so no lifetime moves. Writes are not on the seam: `write_page`, `edit_page`, `delete_page`,
`restore_revision` and the editor keep `PageEditor.SaveAsync`, `SaveIfRevisionAsync` and `DeleteAsync`.

History is a seam question: `HistoryAsync` answers whether a head exists, whether it is a tombstone and
every revision newest first, and `RevisionAsync` answers one revision only when it belongs to that path,
so "this hash is not a revision of this page" has one implementation. The seam's rules are proved once,
in its own suite, rather than twice through a tool and an applet.

The seam reads through `IPageReadStore`, a read-only singleton over `IDbContextFactory<StorageDbContext>`,
because `IPageStore` is scoped and a page's task outlives the request; it answers with `Web` records and
never with a `PagePath`, and the mapping to `Sdk` values happens in the capability.

*Recorded in [page store reader](plans/archive/page-store-reader.md), [IPageReadStore](../src/uk.osric.asp.Web/Pages/IPageReadStore.cs).*

### pages-reading-the-store-is-public

Reading the page store is a capability, `IPageCatalog` — `ListAsync`, `ReadAsync`, `RevisionsAsync` and
`ReadRevisionAsync`, with `PageEntry`, `PageSource` and `PageRevisionInfo` as the values a page may name
— and it reads and never writes. The read is public by default: there is no shell gate, the capability
answers an anonymous render exactly as it answers a signed-in one, and a page that wants its listing or
its source private puts the ordinary `ctx.User` guard at the top of its own source. A refusal inside a
capability is rejected: it is an invisible second gate whose failure is a `500` rather than the redirect
the page's own guard gives.

*Recorded in [page store read](plans/archive/page-store-read.md#d2--public-by-default-like-the-page-that-calls-it), [IPageCatalog](../src/uk.osric.asp.Sdk/IPageCatalog.cs).*

### pages-a-listing-carries-facts

A listing returns every head the store holds, each marked `IsDeleted`, `IsReserved` and `IsHtml`, and a
prefix selects a subtree — the key itself and keys beneath it — never a string prefix; ordering is by
key. A node carries facts, not words: `PageNode` answers the key, whether it is a folder, a page, a
shell file or a tombstone, whether a save would be accepted and the page's image state, and the
permission column and the state are formatted by whoever shows them, with `PageMode` and
`PageImageStatus`, so `ls -l`, `stat`, `list_pages` and `read_page` cannot spell one fact two ways.

A listing is one slice of a store that may hold more, which is `pages-a-listing-resumes-from-a-key`:
`Pages:Read:MaxRowsPerList` bounds a slice rather than refusing past it.
`Pages:Read:MaxSourceCharsPerRender` is one `ReadBudget` per request, shared by every layer of the
chain, charged by the reads and refused rather than clamped.

*Recorded in [page store read](plans/archive/page-store-read.md#d3--everything-marked), [PageStoreReader](../src/uk.osric.asp.Web/Assistant/PageStoreReader.cs).*

### pages-a-page-names-its-libraries

A listing row carries the keys of the libraries its head was compiled against, transitively, so a view
can say what a save to one of those keys would recompile before the edit rather than after it. The fact
is the compiler's closure — already stored as the revision's `key@hash` fingerprint and used by the
staleness check — read back as keys where the fingerprint is written, so the two cannot spell one fact
twice. It is the closure that compiled rather than what the source spells, which is the right answer to
"what recompiles": a library that names another is in it, and a library the page has not recompiled
without stays in it, with the row's `State` saying what happened to the compile. The reverse question —
which pages name this library — is a listing filtered on the fact, not a second read.

*Recorded in [pages](pages.md), [PageEntry](../src/uk.osric.asp.Sdk/PageEntry.cs), [the plan](plans/ide-host.md).*

### pages-a-block-with-no-code-warns

A code or declaration block that holds no code — only whitespace, or only a comment — is a warning,
because it is decisive and invisible: it is what makes a template a page, or, as a declaration, a
library that is never served. The two kinds are two codes rather than two wordings of one, because the
fix differs — the note the author meant is `<%# … %>`, which leaves the template as it was — and each
says the consequence when it is the block that decided the kind. A block that ends inside a literal or
a comment is a warning too: the block ends at the first `%>`, so the author is told where the cause is,
at the literal, and what to write instead, rather than being shown a stray closer in the text that
follows and told the symptom is the cause. All of them are warnings in template space
(`pages-the-shape-check`) and none stops a page being served. A shape warning names its block by the
block's opener, so a block whose opener stands on another line from its content still points at the
line the author will look at.

*Recorded in [pages](pages.md), [TemplateShapeCheck](../src/uk.osric.asp.Core/Compilation/TemplateShapeCheck.cs), [the plan](plans/archive/block-with-no-code-warns.md).*

### pages-a-listing-resumes-from-a-key

A listing is one slice, asked for by a prefix, a cursor and a limit, and answered with its rows and
whether more stand after them. The cursor is the last key the caller was given rather than a count,
because a page saved between two calls would make an offset skip or repeat a row and a key cannot; it
is normalised as a page key, so a trailing slash resumes in the same place. The configured cap is the
largest limit a caller may ask for, not a wall — a listing that refuses past its cap makes the 501st
page an outage of the tree where a slice makes it the next request — and a limit above it is refused
with the setting's name rather than trimmed. A cursor past the last key is an empty answer, not an
error.

*Recorded in [pages](pages.md), [IPageCatalog](../src/uk.osric.asp.Sdk/IPageCatalog.cs), [the plan](plans/ide-host.md).*

### pages-the-catalog-knows-the-shell

`IPageCatalog` answers what the shell can do as well as what the store holds: `ShellLinks()` returns
the shell's links as `ShellLink` values, each with an href, a label, a section, an audience and the
match the shell uses to mark it current. It is synchronous because it is a value, not a read, and
refuses after the render ends as the other members do. The host returns facts; the page filters with
its own `ctx.User`, orders, relabels, marks the current item and draws. There is no rendered menu, no
`IsCurrent` and no new capability: the catalog is already the page's one read seam, and a footer is
the layout's chrome rather than a shell fact. A worker has no request or visitor, so its list is
empty.

*Recorded in [pages](pages.md#page-contract), [the shell](shell.md#layouts), [IPageCatalog](../src/uk.osric.asp.Sdk/IPageCatalog.cs) and [shell-navigation](plans/archive/shell-navigation.md#d2--the-page-catalog-knows-what-the-shell-can-do).*

### pages-the-store-is-a-tree

The store's tree rule is one statement in `Core`: `PageTree` is "below", "directly below" and "the folder
above", and `PageScope(Path, IncludeSelf, Recursive)` is the one answer to which keys a search may read.
`ls`, `find`, the shell's expansion, `grep`, `search_pages` and `list_pages` ask it, and
`PagePathPolicy`'s writable-prefix rule keeps its own name and asks the same scope. A search reads its
sources from the seam and scans them with one loop: `SourcesAsync` decides which keys are read and in
what order, and `PageSourceSearch.Find` walks them in key order, splits each with the caller's own line
rule and keeps the caller's own predicate, stopping one match past the limit so "there are more" is an
observation. Each front-end keeps its projection and its wording, and a tombstone is never a source.

*Recorded in [page store reader](plans/archive/page-store-reader.md), [PageTree](../src/uk.osric.asp.Core/Pages/PageTree.cs).*

### pages-a-page-may-save-itself

Saving a page's own source is the bound save of `IPageWrites`, reached through the seam, with
`PageSaveResult`, `PageDiagnostic` and `PageDiagnosticSeverity` as the values a page may name. It takes
no key — the handle saves `ctx.Path` — so a page repairs itself without naming anything, and it is
unconditional, so a page that wants the stale check uses the capability's named save and passes its
head hash (`pages-any-page-may-write-the-store`). The author-facing diagnostic is an `Sdk` value —
severity, a 1-based template line and column that are both `null` when the position is unknown, the code
and its text — and `Core.Compilation.PageDiagnostic` stays where it is, because moving it would make the
generator depend on the page contract.

Authority is asymmetric: the read is public and the write requires a signed-in visitor, because a
disclosure and a change are not the same act. The capability refuses an anonymous render before it
compiles or stores anything, with a `UserFacingException` whose message says to sign in; a `HEAD`, an
ended render and a second write in one render are page bugs and throw. The check is the capability's, not
the page's: a page's own `ctx.User` guard is only a nicer redirect in front of it.

A started save is never abandoned. An ended render, a `HEAD` or a missing identity refuses before the
save begins; once it has begun the compile keeps its own timeout and the revision is stored even when the
response has already answered `504`. The wait for a compile slot is still abandoned at the render's
deadline, because nothing has been compiled then — losing the author's edit is the loss the diagnostics
decisions already refuse.

There is still one writer: `PageSaver` is `PageEditor.SaveAsync`'s compile-then-store algorithm as a
singleton, `PageEditor` delegates to it, and `IPageRevisionStore` is a singleton that calls the same
`StoreRevisionAsync` every save and the import batch call, which is what lets a page's write land after
its request has gone. The conditional save is the same algorithm on the same seam, so the bound save and
the named save cannot drift.

A save belongs to the next request: the running image is not re-read, the response in hand is the old
page unless the page shows the result the save returned, the save is committed before the rest of the
render, and one render makes at most one write, whichever member it uses. The canonical pattern is a
`POST` that saves and answers with the diagnostics, or saves and redirects with `303`. A batch that
names its own policy is [`one-write-per-render`](backlog/one-write-per-render.md)'s work.

*Recorded in [page self-save](plans/archive/page-self-save.md), [page writes](plans/archive/page-writes.md), [IPageWrites](../src/uk.osric.asp.Sdk/IPageWrites.cs).*

### pages-a-check-stores-nothing

`IPageCheck.CheckAsync(language, source)` answers "would this source check" for text the caller already
holds, and writes nothing: no revision, head, image, closure fingerprint or seed record, and no
dependent is recompiled, so the history stays the history. A caller that wants a stored page checked
reads it with `IPageCatalog.ReadAsync` and passes the template; the source is the subject and there is
no key, so a template that has never been saved answers exactly as one that has. The answer is a
`PageCheckResult` — generation's `PageKind`, whether the source would be served, and the compiler's
diagnostics as `PageDiagnostic` values, a warning leaving it valid — because that is what a save would
have reported. A check spends a compile slot and no `PageWritesScope` action, and a wait past the
render's deadline or the compile timeout answers a `PAGE010` error rather than hanging: there is no
author's edit to protect, so an abandoned check loses nothing.

*Recorded in [pages](pages.md#page-contract), [PageCheckScope](../src/uk.osric.asp.Web/Pages/PageCheckScope.cs), [one check](plans/archive/one-check.md).*

### pages-a-check-names-its-language

A check's language is named by the caller and comes from a closed set declared once in `Sdk`
(`PageCheckLanguage`, with `PageCheckLanguages.Names` as the wire names), so the manual, the assistant's
tool schema and the host's refusal all render from one declaration. It is never inferred from a key's
extension, because an extension is a content type and not a validator: a `.html` page can hold a
template or an asset. `template` is the first value, and the set grows with the checker that answers for
a new one, so a name this build does not answer for is refused by name rather than guessed at.

*Recorded in [pages](pages.md#page-contract), [PageCheckLanguages](../src/uk.osric.asp.Sdk/PageCheckLanguages.cs), [one check](plans/archive/one-check.md).*

### pages-a-check-needs-a-visitor

The check is offered only to a render with a signed-in visitor, because a compile is a shared resource
and a check spends one: a check anyone could call is a denial of service with a friendly face. It takes
a slot from `Pages:Compile:Slots` and is bounded by `Pages:Compile:TimeoutSeconds`, unchanged, so its
cost is a save's cost; its wait and its compile also end at the render's deadline, and it answers
`PAGE010` when that happens rather than hanging. An anonymous render is simply not offered the
capability, so a page reads `ctx.User` only to decide what to show.

*Recorded in [pages](pages.md#page-contract), [PageChain](../src/uk.osric.asp.Web/Pages/PageChain.cs), [PageCheckScope](../src/uk.osric.asp.Web/Pages/PageCheckScope.cs), [one check](plans/archive/one-check.md).*

### pages-scratch-saves-itself

A page that saves itself guards its own view, reads its own source, saves it, answers the save in the
response it is already writing, and refuses a form whose revision hash has been overtaken. The manual's
**Writing the page store** section is the worked example; the seeded `/scratch` that demonstrated it
was retired on 2026-11-16, because the editor is the same save, and the integration suite writes the
page it drives rather than the site shipping one.

*Recorded in [the manual](../src/uk.osric.asp.Web/Pages/Seeds/help), [page self-save](plans/archive/page-self-save.md#d8--the-seeded-scratch-a-page-that-edits-itself).*

### pages-the-editor-is-a-page

The editor is the stored `/pages` page with `?edit=<key>`: one page is one key, the edited path is a
query value, `?page=` stays the read and history view, and the page validates the key through
`IPageWrites.Resolve`. A separate `/editor` seed was rejected because it duplicates the listing, and a
catch-all subtree because it changes the meaning of every key. The page answers in place — a save
re-renders with its diagnostics, hash and the submitted text; a stale save is `409` with both sources;
save-and-view redirects only when nothing failed — and it writes no page-session key, so the capability
needs no flash. `/edit` is retired rather than redirected: the mappings go, the key leaves the reserved
list, and the page catch-all answers it like any other missing key. The editor page is itself a seed like
`/help`, so the owner may edit it with itself, its source is in `Seeds/pages`, and its sheet and module
are seeds too.

*Recorded in [page editor](plans/archive/page-editor.md), [the editor page](../src/uk.osric.asp.Web/Pages/Seeds/pages).*

### pages-any-page-may-write-the-store

The authority to write the page store is the signed-in visitor's, not the page's. `IPageWrites` is
offered to every content layer and to no layout, every named call says which key it writes, and an
anonymous render is refused in the capability before anything is compiled or stored. There is no
registration gate: `Pages:Editor:Key` says where the editor is served — which is all `ctx.EditorPath`
and the shell's remedy links ever needed from it — and the editor's own key is an ordinary key. The
narrowing it replaces kept a handle that names a key away from every page; the owner removed it on
2026-11-15 because nothing about editing is special to one page, and a page that may not write the
store it is reading cannot maintain the site.

One capability has two saves. The bound one is a page's own source (`pages-a-page-may-save-itself`); the
named one is any key the shell does not route, conditional on the head hash the form was drawn from,
answering `PageEditResult` with Saved, Stale or Unchanged, so a mismatch writes nothing and the page
shows the author what changed. A `null` expectation means the form was drawn from a page with no live
source, so a create cannot overwrite one. `Resolve`, `MoveAsync`, `DeleteAsync`, `RestoreRevisionAsync`,
`AdoptSeedAsync`, `InstallSeedAsync` and `SeedSourceAsync` are the same authority on the same handle, and
they spend the same one action a save does.

What guards it: the signed-in visitor before anything is compiled, the reserved keys, `/admin/seeds`
for a deleted or broken editor, and `/admin/scrub` for the irreversible delete. The blast radius is the
owner's decision — a page the assistant writes can rewrite the site — and the assistant's own edit-safety
rules are what stop it from doing so by accident (`assistant-edit-page-is-safe`).

*Recorded in [page writes](plans/archive/page-writes.md), [IPageWrites](../src/uk.osric.asp.Sdk/IPageWrites.cs).*

### pages-a-key-moves-with-its-history

A key is a page's identity, and `MoveAsync(from, to, expectedHash)` is the one operation that changes
it. One transaction deletes the head, re-files every revision under the new key with the hash it was
saved with, re-keys the page state and inserts the head again with the anchor and the content hash it
already had; nothing is compiled, and one render moves once. **Nothing is left at the old key**: `/a`
answers `404` after it moves to `/b`, so no row remembers where the page went. That is the owner's
decision of 2026-11-16, and its one consequence is that a seed the release still ships at the old key
is a new seed there again, so a release that renames a seed moves the seed too.

The rules are the store's own. A reserved key, a library — whose key is in every dependent's closure —
and the page moving itself are page bugs and throw; the same key, a stale expectation, a missing
source, a live target and a key with pages below it are answers. A key with children is refused rather
than moved, because a folder is a batch of moves (`one-write-per-render`), and the expectation is spent
in the statement that deletes the head, so a move never applies to a page that changed underneath it.
The alternatives — a copied history, and a marker row at the old key — were declined with the marker.

*Recorded in [the move plan](plans/archive/page-move.md#the-design), [IPageWrites](../src/uk.osric.asp.Sdk/IPageWrites.cs).*

### pages-delete-install-and-restore

Delete is a tombstone, and restore and install are ordinary saves: a retained revision or a bundled seed
is written as a new revision under the running build, a seed that already matches is not written, a
tombstoned seed comes back with its history, and nothing is ever rewound or recompiled under an old
runtime.

The editor has two different "put it back" actions and two different words: **install** saves a bundled
seed into its page (`cmd=install`, from the page list) and **restore** saves a retained revision's
source as a new revision (`cmd=restore&hash=`, from the history table). Both are a `GET` that shows the
diff against the live source and a `POST` that writes, and both refuse the case that would add nothing —
a restore whose source is what the page already holds, an install whose seed the page already has — with
one `409` document naming the history and the editor rather than a silent write. An install lands on the
page list and a restore on the editor, where its diagnostics are legible; the seed action on a page row
is disabled when the seed matches and stays enabled for a deleted seeded page, and there is no
confirmation step and no restore-all action.

One seed rule serves both the page and the seed page: `PageSeedInstaller` owns "does this seed
differ" and "what happened", so `/admin/seeds` lists every row and writes the ticked ones while the
capability installs a single seed, and there is no second comparison and no batch store in either.

*Recorded in [page editor](plans/archive/page-editor.md), [revision restore](plans/archive/revision-restore.md), [PageSeedInstaller](../src/uk.osric.asp.Web/Pages/PageSeedInstaller.cs).*

### pages-a-seed-write-records-its-hash

A page's row carries the hash of the seed its content is anchored to (**R**), written in the same
transaction as the revision and by nothing else: an ordinary save, a restore, an import and the
assistant all leave it where they found it. With **S**, the hash this release ships at that key, and
**H**, the hash of the live source, the whole seed rule is one sentence — **an automatic write
requires H = R and S ≠ R** — so "the release moved this page" and "the author edited this page" stop
being the same observation. A write with no anchor of its own to keep is the only one that sets R: a
seed installed on create, an install, or a restore of a seed.

H is stored too, because a listing that had to hash sources to show a bit would spend the render's
source allowance on it. H is recoverable and a startup repair fills it in for every head written
before the column existed, from the revision that head points at, in one transaction and idempotently.
R is not recoverable and is never invented: a page from before the column stays *unknown* until
something establishes where it came from, and the two are not the same migration step. The repair is
deliberately not an EF migration, because SQLite has no SHA-256 and a migration gets no context, so
the alternative would be a frozen second copy of the algorithm.

*Recorded in [seed provenance](plans/archive/seed-provenance.md), [pages](pages.md), [SeedComparison](../src/uk.osric.asp.Core/Pages/SeedComparison.cs), [PageContentHashRepair](../src/uk.osric.asp.Web/Pages/PageContentHashRepair.cs).*

### pages-a-seed-verdict-is-the-hosts

What a seeded row is, is the host's answer: `Core` compares the three hashes to one verdict and the
shell puts it on `PageEntry.Seed` as a `PageSeedSummary` — the verdict's word, the sentence that says
why, and the two things the row may do — so a page renders one row's state without reading the seed
source at all. The list used to read two sources per seeded row to produce one bit and charge a third
of the render's source allowance for it; the read is now the row it was already reading.

The words are the host's because a page that restated the rule would state a second copy of it. The
seven comparisons — *in sync*, *release newer*, *edited*, *conflict*, *store newer*, *unknown*,
*retired* — are all `SeedVerdict`, and the two row states that are not comparisons, a key with no head
row and a tombstone, stay in `Web` with the words for them, because there is nothing to compare and
only the caller knows whether the key is shipped.

*Recorded in [seed provenance](plans/archive/seed-provenance.md), [pages](pages.md), [SeedComparison](../src/uk.osric.asp.Core/Pages/SeedComparison.cs), [SeedVerdictWords](../src/uk.osric.asp.Web/Pages/SeedVerdictWords.cs), [PageSeedSummary](../src/uk.osric.asp.Sdk/PageSeedSummary.cs).*

### pages-an-archive-carries-provenance

An archive's format version 2 puts the page's anchor in its `Pages` table beside the source, so an
export from one site and an import into another keep what the page came from. A version 1 archive is
still read, with no anchor on any key — the import carries R when the file has it and never invents
one — and a version 2 file is refused by a build that knows only version 1, which is the right way
round: a build that dropped the anchor silently would be worse than one that will not open the file.
The value is validated as a canonical hash on the way in, and a malformed one refuses the archive
rather than landing a row that no comparison can use.

*Recorded in [seed provenance](plans/archive/seed-provenance.md), [storage](storage.md), [PageArchiveFormat](../src/uk.osric.asp.Web/Admin/PageArchiveFormat.cs).*

### pages-scrubbing-is-not-editing

Scrubbing stays in the shell at `/admin/scrub/{**path}`, because permanently removing history is not
editing: the same typed-key confirmation, behind the editor policy and antiforgery, linked from a
deleted row rather than offered as a page action. Its `POST` answers with its own document and a link
back to the editor page instead of writing a flash no view reads.

The one other scrub is the repair for a row no page surface can reach: a stored path at a key the shell
answers cannot be deleted through the editor, so the seed page proves the key is reserved and removes
the head, revisions and state after a tick and the typed word. It is the same irreversible, shell-owned
act, with the route and the words in `shell-reserved-rows-are-scrubbed-not-served`.

*Recorded in [page editor](plans/archive/page-editor.md), D7, [one seed page](plans/archive/seed-page.md), [SeedsEndpoint](../src/uk.osric.asp.Web/Admin/SeedsEndpoint.cs).*

### pages-the-session

A page session is minted lazily by the shell, on the first write and never on a read: 128 random bits in
base64url travel in an `HttpOnly` cookie the page cannot set, written after the render on every path that
leaves the page pipeline. Two tables carry it — `PageSessions` and `PageSessionValues` — so a visitor
keeps a half-written form across a restart. A row whose account does not match the request's principal is
deleted and replaced, sign-in rotates the row, sign-out deletes it, and reading slides the idle timer
while a `HEAD` and a layout do not.

Ownership is structural rather than checked: the capability composes the path itself, so `SetAsync` stores
under the rendering page's path and a page cannot name a key outside its own space. Reads are addressed by
`(page, key)` and are open, because showing what another page left is what the read is for;
`AllKeysAsync` answers `PageSessionKey` pairs because a bare key cannot be read. Serialisation is page
storage's `JsonSerializerOptions`, a stored JSON `null` is a found value, and misuse throws as page
storage throws.

`Pages:Session` bounds the store per session rather than per page, with different limits anonymous and
signed in. A total live-session cap evicts the most idle **anonymous** session first and a signed-in one
only when no anonymous row is left, because the owner's session is the one nobody can mint again, and a
mint is refused only when nothing can be evicted. Expired rows are deleted before the count, eviction is
one bounded batch, and a five-minute sweep logs one line per sweep that removed anything.

*Recorded in [page session](plans/archive/page-session.md), [IPageSession](../src/uk.osric.asp.Sdk/IPageSession.cs).*

### pages-build-info

The commit a build was made from is a page capability, `IBuildInfo`, whose `Commit` is the full git hash
or `null` when the build did not come from a checkout. A page cannot read the assembly or the environment,
and the value is build data rather than a layout slot or a request field; the seeded `/about` page links
it to the repository. `Web` records the hash as assembly metadata read from `git rev-parse HEAD` at
build time.

*Recorded in [pages](pages.md#page-contract), [IBuildInfo](../src/uk.osric.asp.Sdk/IBuildInfo.cs).*

### pages-page-first

Anything a page can serve is a page. A route stays in the shell only because it bootstraps a broken site
— `/login` and `/logout`, and the admin routes the owner reads or uses when a page did not load or no
longer compiles — because it is the page runtime itself (`{**path}`, `/favicon.ico`, `/health`,
`/metrics`), or because a page cannot do it yet, and each of those has a job. The page column is the
default and the audit is expected to shrink.

Every write of the page store stays with the shell — save, delete, scrub, restore, import and export, and
the assistant's bounded tools — and a page gets a bounded read; there is no second writer.

*Recorded in [page first](plans/archive/page-first.md#the-rule).*

### pages-an-archive-is-a-database

A page archive is a SQLite database, not a zip: `Pages(Path, Source)` and `PageState(PagePath, Key,
Value)`, identified by `PRAGMA application_id` and `user_version`, built in memory and serialized, and
read by deserializing an upload read-only with `query_only` and `trusted_schema = OFF` before any query.
It carries source and state only — history, tombstones, users and configuration stay with the site that
made it. Import is additive and all or nothing: every row is validated before anything is staged, the
approved batch is compiled one page at a time with nothing written, then every page, head and state key
is written in one transaction, so a compile failure is a stored revision with diagnostics and a crash or
failed write leaves nothing applied.

*Recorded in [import and export](plans/archive/page-import-export.md).*

### pages-have-kinds

Every stored source has a kind, judged from its tokens by generation and stored on the revision as
`PageKind`, with the compiler refining the verdict where only compiling can tell a library from a
worker. The kind belongs to the source rather than to the key, so editing a page can change what it is
and there is no configuration for it.

- **text** is literal text: served as written, never compiled.
- **page** is a template with something to render: compiled to an image and served.
- **library** is declaration blocks only — at least one type in a namespace, no handler, no assembly
  attributes and no statements — compiled into whatever names it and never served.
- **worker** is declaration blocks that declare one `IWorker`, compiled to an entry point and run by a
  job.

This entry is the set. What each kind is and may do is the entry named for it, so a new kind adds an
entry beside them rather than a count changed here.

*Recorded in [page libraries](plans/archive/page-libraries.md#d1--the-verdict-is-one-kind-not-a-second-flag), [PageKind](../src/uk.osric.asp.Core/Pages/PageKind.cs).*

### pages-a-library-is-named-by-a-using

A library is named by an ordinary C# `using`: a page's own directives are matched against the namespaces
the live libraries declare, and their directives in turn, resolved to a fixed point. Each library is a
syntax tree of its own in the page's compilation rather than text spliced into it, so nothing can collide
and a library's types are its own. There is no import list, no directive and nothing new for an author to
learn.

The API policy trusts the page and its closure and checks every tree: a type the page or a library
declared is the page's own code, while every member it names is still checked at the tree it was written
in. A diagnostic therefore carries the key of the page or library it belongs to, and names that key and
line rather than the dependent that reached it.

A page's image records the closure it was compiled against as a fingerprint of `key@revisionHash`, and
only a library whose head has moved makes its dependents stale, with `page.cache.staleness` reason
`library-changed`. Nothing recompiles eagerly: the heads are read on a render and the sources only on a
compile. Nothing is shared at runtime either — a library's statics are one copy per dependent, which is
the honest cost of compiling it in. A library stores its source, its verdict and its diagnostics, and no
assembly: it is checked when it is saved and never run, so its key answers `404`, its revision is never
an asset, it is never pinned with `?v=` and no layout ever meets it. A shared library assembly was costed
and not built: content inclusion and a compiled-into-every-dependent tree are both cheap at this size,
and a process-wide assembly would be a channel between visitors. The trigger to revisit is a render that
spends more time compiling than running, or an image that is mostly library.

*Recorded in [page libraries](plans/archive/page-libraries.md), [PageImageBuilder](../src/uk.osric.asp.Web/Pages/PageImageBuilder.cs).*

### pages-a-worker-is-a-kind

A worker is a kind of its own. It is declaration-only like a library, but it declares exactly one public
type implementing `IWorker` with a parameterless constructor and is compiled to an entry-point image when
it is saved. Generation can only
see that a source has declarations and nothing to render, so it still answers library; the compiler
refines the verdict from the compiled types and reports two entry points, a non-public one or an
unconstructible one as `PAGE006`. A worker is never served, never wrapped and never an asset: one
predicate pair answers the endpoint, the renderer and the versioned URL, and its key is `404` with or
without `?v=`. A library that named a worker's namespace would make a program a module, so the closure a
page or a library sees is the live libraries alone; a worker's own closure is recorded like a page's, so
one whose library moved is compiled again before it runs. Its image is a page's mechanics without a
handler — source, closure, policy check, emit, fingerprint and runtime id stored on the revision — loaded
into a collectible context for one job and unloaded when it ends.

*Recorded in [pages](pages.md#assets-and-versioned-urls), [PageJobRunner](../src/uk.osric.asp.Web/Work/PageJobRunner.cs).*

### pages-jobs-run-a-worker-for-the-page

A job is one run of a worker, started by a page and owned by the visitor who started it.
`StartAsync(workerKey, firstMessage)` runs it as the signed-in owner; a missing key, a page, an asset, a
library, a reserved key, an anonymous render and a full queue are refused as values, because the kind is
known at save and the start needs no compile to say no. The job works *for the starting page*: the page is
the principal and the worker is the code, so its storage is the page's.

`IPageJobs` is six calls — `StartAsync`, `SendAsync`, `GetAsync`, `ListAsync`, `ReadEventsAsync` and
`CancelAsync` — offered to every page and never to a layout, because a layout that started work would
start it once per page it wraps; a `HEAD` is offered the reads and not the three that write. No call takes
an owner: the handle is bound to the render's visitor, so another account's job reads as an unknown one
rather than as a refusal that admits it exists.

A viewer waits, it does not sleep: `ReadEventsAsync(jobId, after, waitSeconds)` returns when there are
events, when the job ends or when the wait elapses, whichever is first, with `Events`, `Next`, `Reset`,
`State` and `Failure`, capped by `Pages:Jobs:MaxWaitSeconds` and by the render's own budget; a page
streams the batches as server-sent events and writes a comment when one is empty. Allowing `Task.Delay`
was rejected — a general fairness hole the page policy denies — as was a poll per reload, a whole
document per event.

*Recorded in [pages](pages.md#jobs), [PageJobs](../src/uk.osric.asp.Web/Work/PageJobs.cs), [IPageJobs](../src/uk.osric.asp.Sdk/IPageJobs.cs).*

### pages-a-job-is-not-durable

A job's journal is in memory, bounded and replayable, and the durable record is the page's. Events are
`(sequence, type, json)` in a ring bounded by `Pages:Jobs`, with live subscribers and a replay window; a
viewer whose sequence has been dropped gets the events the ring still holds and `Reset`, never a gap it
cannot fill. There is no jobs table: a worker does not survive a restart either, so its rows would have
to be tidied after every crash, and the starting page's storage is what a resumed job reads. A durable
journal, queue or message channel was rejected because a job's side effects are not idempotent, so
re-running one after a crash is a different feature.

A job has its own lifetime and its own slot. It takes one of `Pages:Jobs:MaxConcurrent` job slots rather
than a render slot, because it answers nobody. Its deadline is `Pages:Jobs:SilenceSeconds`, re-armed by
every publish and every returned receive, plus `Pages:Jobs:MaxDurationSeconds` from the start, because a
job that runs for a working day is the point. A wait in `ReceiveAsync` pauses the silence clock but not
the ceiling: a worker waiting for its owner has not gone quiet, and the ceiling is what bounds the wait; a
timed wait returns and the clock resumes, which is how a worker does periodic work without a sleep
primitive. A job that works but never publishes or receives is cut, and a worker that ignores its token
past a clock is reported as cut rather than completed.

*Recorded in [pages](pages.md#jobs), [PageJobJournal](../src/uk.osric.asp.Web/Work/PageJobJournal.cs), [JobLifetime](../src/uk.osric.asp.Web/Work/JobLifetime.cs).*

### pages-a-worker-gets-no-response

A job's capabilities split data from code, and there is no second route to them. `IWorkerContext` has no
`Write`, `FlushAsync`, `SetStatus`, `SetContentType`, `SetHeader`, `Redirect`, `AntiforgeryField` or
layout; its capabilities are the same seam a page reaches, offering storage bound to the **starting page**
— the data, which a job and its view share — and fetch, the catalog and the job handle bound to the
**worker**, so a credential bound to the worker stays the worker's. The markdown renderer and the writer
are not offered: a worker publishes text or JSON and the page renders it. Constructor injection into the
worker is refused, because it would be a second, silent route to the same four capabilities that
`check-pages` cannot see, and would raise a lifetime question the collectible context cannot answer.

*Recorded in [pages](pages.md#jobs), [IWorkerContext](../src/uk.osric.asp.Sdk/IWorkerContext.cs).*

### pages-the-first-flush-commits

**A flushing page is served alone.** The first flush is refused while the page would be wrapped, naming
the layout and the `Layout = null` remedy, because a layout needs the whole body before it can run; a
layout layer may never flush, and a committed page that names a layout afterwards is served alone with a
warning.

The first flush is the commit point. Status, content type, headers, the script policy and any already
minted session or antiforgery cookie are applied before the response starts; `SetStatus`, `SetContentType`,
`SetHeader`, `AllowScripts`, `Redirect`, `RedirectToLogin` and reading `AntiforgeryField` throw
afterwards, and a first session write after the commit is refused because its cookie can no longer be
sent — a write with a session the visitor already has is ordinary.

A failure after the commit is a truncation. An exception is logged at Error and the connection is
aborted; the render deadline is logged at Warning and aborted instead of attempting a `504`, because the
headers are already sent. Before the first flush the buffered error page and the `504` are unchanged. A
client that leaves mid-write is not an unhandled exception: `DisconnectGuard` treats a started response's
body-write failure or an aborted cancellation as a Debug-level end and aborts, and the page sink and the
assistant's event stream share the rule.

*Recorded in [pages](pages.md#streaming), [PageResponseWriter](../src/uk.osric.asp.Web/Pages/PageResponseWriter.cs), [PageChain](../src/uk.osric.asp.Web/Pages/PageChain.cs), [DisconnectGuard](../src/uk.osric.asp.Web/Http/DisconnectGuard.cs).*

### pages-a-stream-is-bounded

The page budget splits at the commit. Before the first flush `Pages:Render:TimeoutSeconds` is the whole
budget, queueing included, exactly as it was; from the commit the deadline is re-armed by every flush to
`Pages:Stream:SilenceSeconds`, with an absolute `Pages:Stream:MaxSeconds` measured from the commit. A
page that flushes and then goes quiet is truncated by silence; one that keeps flushing is stopped by the
ceiling. Raising the render timeout was rejected, because it would host every hung page for minutes, as
was a rule keyed on content type, when a page that merely answers `text/event-stream` is not necessarily
live.

A committed stream holds a stream slot and gives its render slot back: `Pages:Stream:Slots` is a second
pool because a stream is mostly waiting while a render is mostly CPU, so a stream costs the render pool
nothing and the site cannot be held open by more streams than the pool has. A refused stream is a
distinct exception, thrown before anything is committed: when every slot is taken the first flush throws
`PageStreamRefusedException` and the endpoint answers `503` with the site's problem document, and a page
may catch it and render its own explanation.

*Recorded in [pages](pages.md#streaming), [PageBudget](../src/uk.osric.asp.Web/Pages/PageBudget.cs), [PageStreamRefusedException](../src/uk.osric.asp.Sdk/PageStreamRefusedException.cs).*

### pages-a-stream-reports-its-own-end

A streamed render reports its own end. `page.render.duration` is recorded at the commit, where the
visitor stopped waiting, and `page.stream.duration` covers the lifetime after it, tagged with the page
path and an `outcome` — `completed`, `silence`, `ceiling`, `client-left` or `error`. The render
histogram's scale is sized for the render cap, so a ten-minute stream in it would make the panel
meaningless. A page that relays a slow upstream flushes its own response first: the render deadline
covers everything before the first flush, a fetch included, so the commit is what moves the visitor onto
the stream while the upstream body stays bounded by the fetch's own values, and the manual teaches the
order with the relay example.

*Recorded in [metrics](metrics.md), [PageMetrics](../src/uk.osric.asp.Web/Pages/PageMetrics.cs).*

### pages-a-comment-is-not-code

A comment is `<%# … %>`, and it is neither code nor text: it writes no output, is emitted into no
generated source, makes no no-code page a page and no library a page, and never reaches a visitor —
a text page's body is its source with every comment construct removed, and a page's body never held
it. Its braces are not the page's braces, so a note that mentions `{` is not an unbalanced one, and
the script skeleton blanks the whole construct so a JavaScript parser's positions hold. It ends at the
first `%>` like every other block, which is why `%>` cannot appear inside one, and `<% /* … */ %>` is a
different thing: C# inside a code block, which makes the template code.

A block that starts with `#` is a comment whether the author meant a directive or not, so
`<%#if DEBUG %>` is a note rather than a preprocessor line; a directive is written `<% #if DEBUG %>`,
with the space that keeps it a code block.

*Recorded in [architecture](architecture.md#functional-core-imperative-shell), [pages](pages.md#assets-and-versioned-urls), [TemplateComments](../src/uk.osric.asp.Core/Compilation/TemplateComments.cs), [TemplateTokenizer](../src/uk.osric.asp.Core/Compilation/TemplateTokenizer.cs).*

## Shell

### shell-redirect-is-one-helper

Redirects are one helper, not a family of ad-hoc status codes: `Redirect` takes the destination as a
single relative-or-absolute string and encodes its path, query and fragment separately, so a query is
never escaped into the path. `Permanent` (`308`) and `SeeOther` (`303`) are thin wrappers over it, and a
canonical redirect is `308` so the method and body survive.

*Recorded in [Redirect](../src/uk.osric.asp.Web/Http/Redirect.cs).*

### shell-nobody-is-handed-a-blank-failure

Nobody is handed a blank failure. The error responses write either the document shell with the code, a
plain-language explanation and recovery links, or an RFC 9457 `application/problem+json` document with
the same code as an extension member, chosen by `Accept`. `UseStatusCodePages` is the guarantee for every
status no endpoint described — routing 404 and 405, a rejected antiforgery token, the rate limiter — and
a refused form covers `ErrorMessage.FormExpired`. Recoverable failures are recovered rather than
reported: an expired sign-in form is re-rendered with a fresh token, and a "save and view" that does not
compile stays on the editor with its diagnostics.

*Recorded in [the shell](shell.md#error-responses).*

### shell-security-headers-are-one-set

Every response carries one security-header set, written by middleware when the response starts and only
where the header is still absent. `style-src 'self'` carries no `'unsafe-inline'`, so a page can use
neither an inline `style=` attribute nor a `<style>` block, and a page cannot write a script into its own
response: the editor and assistant views widen `Content-Security-Policy` from `script-src 'none'` to
`script-src 'self'` for their own modules, and a stored page asks with `ctx.AllowScripts()`, which
applies only to a successful buffered render and never to a redirect or an error. Inline scripts and
event-handler attributes stay blocked either way, so a script is a same-origin external module. Because a
page with no code cannot call `AllowScripts`, its extension row carries a `ScriptPolicy` of `none` or
`self` instead: it follows the key's extension, applies before the page runs, and a page with code may
widen `none` to `self` and never narrow `self`.

*Recorded in [editor-line-gutter](plans/archive/editor-line-gutter.md#7-one-middleware-sets-the-security-headers-only-the-editor-allows-script), [pages](pages.md#assets-and-versioned-urls).*

### shell-the-markup-is-semantic

Diagnostics are a `<ul class="diagnostics" role="list">` with the severity class on each `<li>`:
`role="list"` is the one place a role is not redundant, because the sheet strips the markers and Safari
drops list semantics for `list-style: none`; `<ul>` rather than `<ol>`, because diagnostics are a set
rather than steps. Form fields are a description list — a `<dt>` holding the label and its `<dd>` the
control, inside `form.form` — with a grid of `fit-content(40%) minmax(0, 1fr)`, so the label column takes
the widest label up to 40% of the form and then wraps while the control takes the rest; a textarea spans
both columns as a fieldset whose legend carries the label, and the editor's save form is exempt because
it is one full-window textarea with a gutter and no second column to line up with.

*Recorded in [site.css](../src/uk.osric.asp.Web/Pages/Seeds/site.css), [semantic-html-and-accessibility](plans/archive/semantic-html-and-accessibility.md#d6--diagnostics-become-a-list-and-keep-their-look).*

### shell-accessibility-is-asserted

Accessibility is asserted, not delegated: `AccessibilityTests` renders every shell view once and applies
twelve shared rules with a hand-written `MarkupReader`, plus per-view assertions. Browser-driven tooling
(axe-core, pa11y, Playwright) is the browser work's and the gate job's. What a string cannot show — the
accessibility tree, tab order, focus visibility, zoom, contrast, how a chart is read — is a written hand
pass whose findings are recorded with the plan.

*Recorded in [semantic-html-and-accessibility](plans/archive/semantic-html-and-accessibility.md#verification), [testing](testing.md#the-five-suites).*

### shell-the-menu-is-written-twice

The site menu is written twice on purpose: composed by `SiteNavigation` for the shell's own views, and
written in the seeded `/_layout` for stored pages. Both render one list of `ShellLink` values — the
shell's own copy and the administration menu filter it, and the layout reads the same values through
`IPageCatalog.ShellLinks()` — so a route cannot appear in one and not the other. Each document still
writes its own markup, through the shell's nav builder or the seeded library, and an integration test
compares the items, their destinations and the current item. The footer is the layout's own chrome and
is not in the list. The shell's document is deliberately not the stored layout, because the error,
health, sign-in and editor routes must answer when the page pipeline is broken.

*Recorded in [site navigation](plans/archive/site-navigation.md#d5--the-menu-lives-in-the-layout-and-in-the-shell-through-the-same-builder).*

### shell-the-shell-is-marked

A shell document carries `class="shell"` on its `body` and a visible **Site tools** label in its
banner; the stored layout carries neither. Both marks are text and structure rather than colour, so a
shell page says what it is with no sheet and to a screen reader. The label is the honest statement of
a property: a shell page cannot show stored pages in its menu because its menu is the site's tools,
and it must answer when the page store is broken, so it never reads the store to draw its chrome. A
page that wants a page-store menu reads `IPageCatalog.ListAsync` beside `ShellLinks()` and writes both.

*Recorded in [the shell](shell.md#the-document-shell), [site.css](../src/uk.osric.asp.Web/Pages/Seeds/site.css) and [shell-navigation](plans/archive/shell-navigation.md#d5--the-shell-is-marked-as-the-shell).*

### shell-admin-is-a-shell-route

`/admin` is the shell's own route, and it answers anyone: it draws the administration section of the
shell's route table, minus the tools this visitor may not reach, so an anonymous visitor sees the
public tools and a line saying a sign-in reaches the rest. It is a route because it must answer when
the page pipeline is broken — the same reason every other administration view is one — and because a
page cannot draw the shell's own link list. The key is reserved, so a stored page cannot take it. The
administrative index is the one view the earlier seeded page was right about in spirit: one list of
tools rather than a second one. The root holds every tool the audit keeps: the administration views
and the status dashboard, which answers at `/admin/status` with its chart at `/admin/status/chart`
(`shell-the-tools-live-under-admin`).

*Recorded in [the seed trim](plans/archive/seed-trimming.md#d3---admin-is-a-shell-route), [the shell](shell.md#the-administration-index), [AdminIndexEndpoint](../src/uk.osric.asp.Web/Admin/AdminIndexEndpoint.cs).*

### shell-the-tools-live-under-admin

Every shell **tool** the audit keeps lives under `/admin`: the administration views and the status
dashboard, whose chart is `/admin/status/chart`. The routes outside it are not tools — `/login` and
`/logout` bootstrap identity, the assistant is a page in waiting, `/health`, `/metrics` and
`/favicon.ico` are the wire, and `{**path}` is the page runtime — so the root is a statement about
what a tool is rather than a second namespace. `/status` was the only permanent tool outside it and
became `/admin/status` with no redirect: the dashboard is owner-only and the old name would be a
reserved key kept alive for a bookmark. The root is not configuration: it is the route identity and
the boundary the `Editor` policy is written against, so a value that could move it could let a page
claim a shell route.

*Recorded in [the shell](shell.md), [architecture](architecture.md#where-a-view-lives) and [shell-navigation](plans/archive/shell-navigation.md#d7--the-shells-tools-live-under-admin).*

### shell-admin-assets-are-static-files

The files only the shell's own views load — `admin.css`, `admin.js`, `status.css`, `reference.css` and
the icon at `/rocket.svg` — are in the project's `wwwroot`, served by the static file handler before
routing, and are not pages at all. A file that was a seeded page needed a restore after every release
that moved it, and a `?v=` link that named a revision of a file nobody could edit; the five keys are
reserved so a page cannot be shadowed silently. `/site.css` stays seeded because a stored page links
it and the owner may edit it (`pages-one-base-sheet`), and so do the sheets and scripts a stored page
loads.

*Recorded in [the seed trim](plans/archive/seed-trimming.md#d1---the-shells-own-files-are-files), [the shell](shell.md#the-shells-own-files), [operations](../deploy/operations.md).*

### shell-the-licences-are-read-not-restated

The copyright and licence page is `/admin/copyright`, a public shell route that renders the files the
image carries: `LICENSE`, `THIRD-PARTY-NOTICES.md`, every text in `LICENSES/` and `REUSE.toml`. A copy
of those texts on `/about` was a second home for a file that changes without the page noticing, and
the notice the image ships for its packages is the one a reader is entitled to see. A missing file is
named and the rest of the page still answers, so a build that did not come through the Makefile says
what it lacks instead of failing.

*Recorded in [the seed trim](plans/archive/seed-trimming.md#d4---about-keeps-the-credits-admincopyright-keeps-the-licences), [the shell](shell.md#the-copyright-page), [CopyrightEndpoint](../src/uk.osric.asp.Web/Admin/CopyrightEndpoint.cs).*

### shell-one-table-of-routes

The shell describes each of its own routes once, in `Web/Shell/ShellCatalogue.cs`: the key, the
methods, who may reach it, a one-line description and the menus it appears in. The reserved key set,
the record the assistant reads and both menus are derived from that table, so a route cannot be
described by one of them and forgotten by another — they were three hand-kept copies and the
assistant's prompt a fourth, and they had drifted. Two things do not collapse: a *page* link (Home,
the editor, the manual) is in the table but answers with no methods or description, and a *route* may
appear in no menu. A menu entry carries its own audience and order, because who may reach a route and
who a link is for are different facts — `/admin` answers anyone, the site menu offers it to the owner,
the administration menu to everyone — and the two menus do not agree on position.

*Recorded in [the route catalogue](plans/archive/shell-route-catalogue.md), [the shell](shell.md#the-administration-index), [ShellCatalogue](../src/uk.osric.asp.Web/Shell/ShellCatalogue.cs).*

### shell-reserved-keys-are-an-invariant

The keys a stored page may not claim are the shell's route paths, taken from the table, and are not
configuration: `Pages:ReservedPaths` is gone and a deployment that still sets it is refused at startup
with the reason. A route exists because the site's code answers it, so an operator who shortened the
list could only let a stored page shadow a route such as `/login`, and no legitimate deployment needs
that; a longer list would have to invent a route that does not exist. This is the exception
`code-policy-values-are-configuration` names — a security policy configuration may only keep or tighten
— and [configuration](architecture.md#configuration) lists it with the other invariants.

The list stops a new write. A row that predates the reservation, or one an import or an older build
wrote, is unreachable rather than served; `shell-reserved-rows-are-scrubbed-not-served` is the
maintenance page that removes it.

*Recorded in [the route catalogue](plans/archive/shell-route-catalogue.md#d4--reservedpaths-derives-its-keys), [ReservedPaths](../src/uk.osric.asp.Web/Pages/ReservedPaths.cs), [the shell](shell.md).*

### shell-reserved-rows-are-scrubbed-not-served

A stored row at a key the shell answers can never be served: `PageEndpoint` writes `404` for a
reserved key before it reads the store. It is what a seed that became a shell file leaves behind, or
what an older build or an import wrote at a key the shell had taken, and the editor cannot delete it
because its write path refuses a reserved key and `/admin/scrub` needs a tombstone.

The seed page's **Stored pages at shell paths** section lists every path in `PageHeads`,
`PageRevisions` or `PageState` that `ReservedPaths` answers for, with its head and revision count. A
tick selects the rows and the owner types `scrub`; the `POST` re-scans and refuses the whole batch if
any selected key is no longer reserved, so the repair can never be a second way to delete a normal
page. The scrub removes the head, every revision and the page state in one transaction, with the same
secure delete and WAL checkpoint the editor's scrub uses, and it has no tombstone precondition because
no page surface can delete a reserved row. A startup scan logs the keys and never writes.

*Recorded in [one seed page](plans/archive/seed-page.md), [SeedsEndpoint](../src/uk.osric.asp.Web/Admin/SeedsEndpoint.cs), [IPageStore](../src/uk.osric.asp.Web/Pages/IPageStore.cs), [SqlitePageStore](../src/uk.osric.asp.Web/Pages/SqlitePageStore.cs), [the shell](shell.md), [storage](storage.md).*

### shell-the-admin-tools

Three things stay in the shell because a page cannot do them or must not: **changes**, **seeds** and
**scrub**.

`/admin/changes` serves the deployed commit's history from a file `make changelist` writes and the image
packs, because the running container has no source tree and a page has no file access; each line carries
the full hash so the page can link the commit, the route is owner-only, and the file is reserved like the
other administration tools.

The seed page is one shell page, `/admin/seeds`: a `GET` lists every page the release ships with its
verdict and a tick per row, and one `POST` writes exactly the ticked rows. It is not a reset and not an
import — a seed that already matches is not written and a page the owner made is never touched unless
its own row is ticked. It draws its own document and carries no stylesheet or script, because the
shell's document links the base sheet and a single rule there would hide the page that can repair it.
The same page finds the stored rows at shell paths and scrubs the ticked ones. It is the recovery path
that lets the editor stop being shell.

*Recorded in [the shell](shell.md), [SeedsEndpoint](../src/uk.osric.asp.Web/Admin/SeedsEndpoint.cs), [ChangesEndpoint](../src/uk.osric.asp.Web/Admin/ChangesEndpoint.cs), [ScrubEndpoint](../src/uk.osric.asp.Web/Admin/ScrubEndpoint.cs).*

### shell-the-pack-comes-home

The pack is edited in production — that is what the site is for — and the repository has to be able to
follow it, so the app carries a `seeds` command that runs before the web host starts: `seeds list`
prints every seed and what the store holds for it, and `seeds pull` writes the stored source of every
key the store has moved into a tar on standard output, with everything said about the run on standard
error and `--all` and `--missing` to widen or to report what a pull cannot fix. `make seeds-pull`
extracts that into `Pages/Seeds`, so the change arrives as a diff: git is the merge tool and
`git diff` is the review, and nothing is deleted on the site's word.

It is a command and not a page for the reason the shell exists: it reads the store under the running
build's own schema, writes to a working tree, and has to be usable when the site is not answering.
The make target refuses a seed folder with uncommitted changes unless `FORCE=1`, because extracting
over an edit is the one thing the command can destroy. `make remote-seeds-list` and
`make remote-seeds-pull` run the same command in the container on `HOST`.

*Recorded in [seed provenance](plans/archive/seed-provenance.md), [the shell](shell.md), [SeedCommands](../src/uk.osric.asp.Web/Pages/SeedCommands.cs), [Makefile](../Makefile).*

### shell-seeds-are-chosen-row-by-row

The seed page writes the rows the owner ticks and nothing else. A `GET` lists every page the release
ships with its verdict and a tick per row; the safe rows — *new*, *release newer* and every adoptable
row — start ticked, and the rows that could replace the owner's source — *edited*, *conflict*,
*deleted* and differing *unknown* — are offered unticked. A tick is the authority for a write the
automatic seed gate refuses, and the write is an ordinary seed save, so the source it replaces stays
in the page's history.

A row that changed after the page loaded is refused by the four facts the form carries, and the other
ticked rows still run. A failure is reported and the rest still run: there is no stop at the first
drift, because an unticked drifted row is a deliberate omission, not a half-applied pack the operator
believes is applied. `?diff=<key>` renders one row's change in the same bare document the page uses,
so the diff works when the editor does not.

*Recorded in [one seed page](plans/archive/seed-page.md), [SeedsEndpoint](../src/uk.osric.asp.Web/Admin/SeedsEndpoint.cs), [PageSeedInstaller](../src/uk.osric.asp.Web/Pages/PageSeedInstaller.cs), [the shell](shell.md), [the manual](../src/uk.osric.asp.Web/Pages/Seeds/help).*

### shell-the-seed-apply-is-a-route

A release that moves a seed needs a way to write it that is not the recovery path for a broken page, so
the seed page at `/admin/seeds` is its own shell route. Its `GET` lists the pack in ordinal key order
with each row's verdict and a tick; its `POST` writes exactly the rows the owner ticks, as
`shell-seeds-are-chosen-row-by-row` says. The `?diff` view on the same route shows one row against the
bundled source.

The heading is historical: the route is the seed page now, not a separate apply page, and the name is
the id rather than the URL. The capability is still gated the same way: `InstallSeedAsync` refuses
unless the verdict says the release moved the seed and the page still holds the one it came from, and a
page that already holds the shipped bytes is adopted — the anchor recorded, no revision written —
rather than installed. A row the owner ticks can be *edited*, *conflict*, *deleted* or *unknown*; that
is the owner's explicit authority, and the write is the ordinary seed save.

*Recorded in [one seed page](plans/archive/seed-page.md), [the shell](shell.md), [SeedsEndpoint](../src/uk.osric.asp.Web/Admin/SeedsEndpoint.cs), [PageEditorScope](../src/uk.osric.asp.Web/Pages/PageEditorScope.cs).*

### shell-sign-out-is-a-post

Sign-out is a `POST` from a confirmation view reached by a `GET /logout`. A `GET` that changes state can
be taken by a prefetcher, and a post form in every document would put an antiforgery field into pages a
shared cache may store.

*Recorded in [site navigation](plans/archive/site-navigation.md#d7--sign-out-is-a-link-to-a-confirmation-view).*

### shell-the-cookie-names

The shell's cookies are one character each — one for the auth ticket, one for the antiforgery token and
one for the page session — so one deploy signs everyone out and invalidates any form in flight. The
mapping is written down in the shell document because a browser's cookie pane stops being
self-explanatory.

*Recorded in [the shell](shell.md#cookies), [page session](plans/archive/page-session.md#d8--the-cookie-names).*

### shell-the-reference

The XML documentation a build carries is served at `/admin/reference` for all four product assemblies,
and read by the assistant through one `reference` tool over the page contract only. The corpus is read
once per process from the XML beside the assembly, normalised once, and rendered by one embedded
stylesheet; anchors are stable slugs derived from the member id, and an `<inheritdoc/>` entry copies its
source's text down when the source is one of ours. The audit gains a fourth reason for the build's own
facts, and `/admin/changes` moves under it.

*Recorded in [the shell](shell.md#the-reference), [xml reference](plans/archive/xml-reference.md).*

### shell-the-site-knows-its-own-origin

`Site:Origin` is where the host reaches the site it serves, and it is the one place any credential is
offered: a required policy value, an origin with no path, query, fragment or user info, and plaintext
only to loopback. It is the assistant's view that needs it — a process that cannot say which URL is
*this* one cannot tell a token for itself from a token for somebody else — and the shipped value is the
container's own loopback, which is what a development host uses. A deployment names the public origin
instead, because a view of a page that mints an antiforgery token needs an HTTPS request, and a
production host left on a plaintext origin logs one warning at startup rather than failing a run later.

*Recorded in [the shell](shell.md#a-runs-view), [SiteOptions](../src/uk.osric.asp.Web/SiteOptions.cs).*

## Auth

### auth-cookie-not-identity

Cookie authentication with local accounts: not ASP.NET Core Identity — no user or role stores, no UI, no
EF schema — and not OIDC. `PasswordHasher<TUser>` comes from the shared framework, so the whole feature
adds no package.

*Recorded in [editor authentication](plans/archive/editor-authentication.md#1-local-accounts-and-cookie-authentication-not-the-full-identity-stack).*

### auth-passwords

Password policy is a 12-character minimum and a 1024-character maximum with no composition rules — a
passphrase is the point, and the maximum bounds the hasher's work. New hashes use 600 000
PBKDF2-HMAC-SHA256 iterations; the stored count is honoured and rehashed on sign-in. A password is never
accepted on the command line — echo-off prompt or standard input — so it reaches neither shell history
nor `ps`, and plaintext exists only in the prompt, the request and the hasher call. Usernames are
NFC-normalised, so the same name typed on different platforms is the same account, and an unknown
username still verifies against a fixed dummy hash with one message for every failure, so the response
does not reveal which accounts exist.

*Recorded in [editor authentication](plans/archive/editor-authentication.md#3-password-handling).*

### auth-sign-in-is-long-lived-and-revocable

Sign-in lasts: a 30-day sliding ticket, with **Keep me signed in** checked by default. Unchecked still
carries the 30-day ticket but dies with the browser, for a shared machine. The cookie is `Secure` only in
production, so local development stays plain HTTP. Revocation is a security stamp compared on every
authenticated request: `users passwd` and `users remove` revoke every session, while signing in again
does not, so several devices can hold a valid session at once.

*Recorded in [editor authentication](plans/archive/editor-authentication.md#4-long-term-sign-in-revocable-by-a-security-stamp).*

### auth-a-state-changing-form-carries-a-token

An antiforgery token is required for every form that changes state, and the host refuses `400` before
reading the form or touching storage: sign-in, sign-out, the editor's `POST`s, and an authenticated page
`POST`, which is checked before any page code runs and answered `no-store` on failure. The page half is
deliberately not opt-in — the sign-in cookie is what turns a cross-site post into an owner's action —
and `ctx.AntiforgeryField` is the one-line way to satisfy it, so an authenticated post to a public page
needs the field too, while an anonymous `POST` is unchanged. A successful save answers `303`, and the
message lives in the session rather than in a cookie.

*Recorded in [the shell](shell.md#authentication-and-the-editor), [page authentication](plans/archive/page-authentication.md#d4--authenticated-page-posts-are-csrf-checked-by-the-host).*

### auth-the-key-ring-is-persisted

The data-protection key ring is persisted on a volume, which is what lets a 30-day cookie and in-flight
antiforgery tokens survive a redeploy. Deleting that volume signs everyone out.

*Recorded in [the shell](shell.md#authentication-and-the-editor), [deploy](../deploy/README.md).*

### auth-sign-in-is-rate-limited-per-client

The sign-in limiter counts per client, keyed on a bucket of the connection's remote address rather than
on the address itself or on one global counter. A global counter lets one anonymous client lock the owner
out, and the address cannot be the key because the rate limiter never releases a partition, so an address
the client chooses is unbounded memory. A fixed number of buckets bounds it, and a collision only means
two clients share a counter.

*Recorded in [LoginRateLimitPolicy](../src/uk.osric.asp.Web/Authentication/LoginRateLimitPolicy.cs).*

### auth-a-view-token-names-its-run

A run's view of the site is carried by a token minted for that run, for the owner the run already acts
as, and dead when the run ends: 256 random bits held in `ViewTokenStore` and nowhere else — no table, no
file, no key ring, no configuration key — minted when the processor starts the run and forgotten in its
`finally`, so a finish, a failure, a cancel and an unexpected exception all revoke it. A configured
secret would need generating, placing and rotating, and an account of the assistant's own would be the
same authority under another name while any account is an owner (`product-one-owner`). Nothing stored
means nothing to leak, and the token is not a sign-in: it cannot become a session and no browser can
present one.

*Recorded in [the shell](shell.md#a-runs-view), [ViewTokenStore](../src/uk.osric.asp.Web/Authentication/ViewTokenStore.cs).*

### auth-a-view-token-reads-only

A view may use `GET` and `HEAD` and nothing else, and anything else is a `405` with `Allow: GET, HEAD`,
refused in one middleware after authentication and before authorisation and antiforgery. One middleware
rather than the page endpoint, because the editor, the admin tools, the assistant and the memory page
are shell routes with their own `POST`s, and a rule the next endpoint has to repeat is a rule one of
them will forget. The scope belongs to the credential rather than to the destination, so a `POST` to a
public page with a view token is refused too.

*Recorded in [the shell](shell.md#a-runs-view), [ViewGuard](../src/uk.osric.asp.Web/Authentication/ViewGuard.cs).*

### auth-a-view-that-does-not-verify-is-refused

A request carrying `Authorization: Bearer` either authenticates or is answered `401` with
`WWW-Authenticate: Bearer`; it never becomes an anonymous view. Silently downgrading turns "why is my
page showing the sign-in form" into a debugging session, because a stale or wrong token is
indistinguishable from no token at all. The scheme compares in fixed time and validates the account the
way the cookie does on every request — it exists and its `SecurityStamp` has not moved — so `users
passwd` in the middle of a run takes the view away. The cookie stays the challenge scheme, so a browser's
sign-in redirect is unchanged.

*Recorded in [the shell](shell.md#a-runs-view), [ViewAuthenticationHandler](../src/uk.osric.asp.Web/Authentication/ViewAuthenticationHandler.cs).*

## Assistant

### assistant-always-on

The assistant is not optional: every host runs it, and the provider's key is a required configuration
value with no fallback, so the host refuses to start without it unless the provider is configured as one
that needs none. There is no enable switch, no runtime toggle, no disabled-state view and no `404`
fallback: the routes, the worker, the restart recovery sweep and the site menu item are unconditional,
and a request the assistant will not answer is refused by its own authorization decision rather than by a
feature test. The key check at startup is where a misconfigured deployment stops, instead of serving a
half-working assistant.

*Recorded in [the assistant](assistant.md), [AssistantOptions](../src/uk.osric.asp.Web/Assistant/AssistantOptions.cs), [deployment](../deploy/README.md).*

### assistant-writes-are-revisions

The page-authoring assistant sends the prompt, the conversation and the pages it reads to a configured
OpenAI-compatible provider. It uses no process or host-filesystem tools; a page write is an ordinary
revision and a delete is a tombstone, and the per-conversation approval default is `AlwaysApprove` with
an opt-in `RequireApproval` mode for the tools flagged as such.

*Recorded in [the assistant](assistant.md#ai-page-assistant), [ai page assistant](plans/archive/ai-page-assistant.md).*

### assistant-a-run-is-not-resumed

A durable transcript is paired with an in-memory replay journal. A restart interrupts a run rather than
resuming the provider request it was in the middle of.

*Recorded in [the assistant](assistant.md#ai-page-assistant), [ai page assistant](plans/archive/ai-page-assistant.md).*

### assistant-knows-the-guard

The page-authoring prompt learns the `ctx.User` guard, `ctx.AntiforgeryField` and the public-by-default
rule, and is told never to write a sign-in form, password handling or cookie logic. It is not made
paranoid: the guard is added when the owner asks for a private page, not to every page. The prompt's
version moves when its text does, so a change is visible in the transcript.

*Recorded in [page authentication](plans/archive/page-authentication.md#d7--the-assistant-learns-the-guard-but-is-not-made-paranoid).*

### assistant-deadlines-bound-silence

`Assistant:RequestTimeoutSeconds` is a deadline on **silence**, not on duration: it is re-armed for every
event the provider sends, so a model that reasons for minutes is never cut off, and only a stalled stream
ends the step. A stall fails the run with `provider_timeout` and its own sentence rather than the
run-timeout code, and the sentence is rendered in the transcript, because the reload that follows a
terminal event is exactly when a bare `failed` would erase the reason. `Assistant:RunTimeoutSeconds`
applies the same rule to the whole run: two minutes without a streamed event fails it with `run_timeout`,
an SSE keep-alive comment does not count as an event, and the clock is stopped while a tool executes or the
owner is being asked, so neither counts as the run going quiet. A message that stops at the
provider's output limit is stored as `truncated` for the same reason: the warning in the status line does
not survive the reload.

*Recorded in [the assistant](assistant.md#ai-page-assistant).*

### assistant-a-run-waits-on-one-owner-interrupt

Every time a run stops for its owner it is one thing: a row in `AssistantInterrupts` holding the kind, the
request the owner is shown and the resolution they give, with the run stored `awaiting_owner` while it
waits and the run's id as the key, because a run waits on one thing at a time. The kinds are the approval
of a flagged tool call and the continuation of a spent budget; a further kind is a request and a card,
not a new status, table, coordinator or route, because the store never reads inside either half of the
record. One coordinator wakes the worker and the row is the authority, so no signal can disagree with what
was recorded. One status, because the run's availability is the fact and the kind says why. The route
carries no kind either, and its body is parsed against the record's kind, so a form built for another wait
is refused rather than guessed at. A restart fails a pending interrupt closed and leaves the synthetic tool
result the stop path writes.

*Recorded in [the assistant](assistant.md#ai-page-assistant), [the owner interrupt](plans/archive/assistant-owner-interrupt.md).*

### assistant-a-run-pauses-on-budget

Running out of a run budget pauses the run instead of failing it: the step loop, the tool-call budget and
the per-step call budget stop at the point the budget forbids, the run is stored `awaiting_owner` with a
pending `continuation` interrupt that carries the budget it ran out of and what continuing buys, and the
page asks "Do you want to continue?" in a card rendered from that request, so a reload still shows it. The transcript gains nothing for the pause, so the next provider
request is the one the run would have made and the model never learns it happened. Continue buys one
window of that budget alone, kept in the run's `StepLimit` and `ToolCallLimit`, and Stop ends a paused run
as `cancelled`, exactly as Stop ends a running one. The silence clock stops while the question waits; a
host restart still interrupts the run, as it interrupts any other wait on the owner. Provider silence and a timed-out tool
keep failing, because neither is a budget the owner can grant.

*Recorded in [the assistant](assistant.md#ai-page-assistant), [RunProcessor](../src/uk.osric.asp.Web/Assistant/RunProcessor.cs).*

### assistant-tool-calls-count-attempts

The assistant's Tool calls row counts the calls a run has attempted, because that is what the tool-call
budget spends and what a pause names; the response's call count is not used for it, because it describes
the whole step and lands before any of those calls has run, so it would read past the limit while the run
was still working and a step that stopped early would reload to a count it never reached. The figure is
published as each `tool.started` is handled, so the row ticks with the card rather than at the step's
edge, and `RecordToolCallAsync` stores each attempt so the reload agrees. A denied or refused call counts
once, as the cap counts it. A reload taken while a call is in flight lags by that one call, the same way
Steps lags by the step in flight.

*Recorded in [RunProcessor](../src/uk.osric.asp.Web/Assistant/RunProcessor.cs), [the assistant](assistant.md#ai-page-assistant), [assistant live figures](plans/archive/assistant-live-figures.md).*

### assistant-budgets-are-one-snapshot

Every assistant budget figure the server reports is one pure `BudgetSnapshot` in `Core.Assistant`,
measured by the trimmer's own rule and the stored transcript's tool calls and results, and read by both
the page render and the run stream, so a reported live figure and a reloaded one cannot disagree. The
conversation page shows every budget that can stop a run as a used/available pair drawn with a `<meter>`,
because a run that failed on a budget should have shown how close to it the conversation already was;
HTML has no `<gauge>`, and the number is spelled out beside the meter so the picture is not the only
reading. The panel is compact — ten rows in two columns, each a label and a meter with its figure drawn
over it, at most three digits plus a unit letter, which replaces the count line beside the meter — and
the amount is one formatting rule the live script mirrors, so a figure updated live and the same figure
after a reload read alike.

The figures move while a run runs, over the journal the page already listens to: the loop publishes a
budget update when it learns something — as a step starts, after that step's usage is stored, as each
tool call starts and once before `run.completed` — and never on a timer. There is no budgets endpoint,
because polling would be a second mechanism beside an open stream with no replay and no ordering against
the deltas it would arrive between. A terminal event reloads the page and the reload is authoritative. An
update changes the count text, the meter's `value` and the exceeded class, and nothing else: the meter
stays hidden from assistive technology and the list is not a live region, because ten counts moving
several times a run are noise read aloud.

*Recorded in [BudgetSnapshot](../src/uk.osric.asp.Core/Assistant/BudgetSnapshot.cs), [assistant.js](../src/uk.osric.asp.Web/Pages/Seeds/assistant.js), [assistant live budgets](plans/archive/assistant-live-budgets.md).*

### assistant-live-figures-are-marked

A figure the conversation page projects between snapshots is marked, because the panel's promise is that
a reported figure a reader watches is the figure a reload shows and a projection is not that figure. The
projection is made from bytes the page already receives — the streamed answer and the tool-call arguments
divided by the token spike's prose ratio, about 3.8 characters per token, never the request's own bytes
per prompt token — and it covers Completion, Context and Window only: those three are tokens, and every
other row is the reader's own input, a reported count, or already exact on the event that carries it. The
mark is a `~` for the eye and a visually-hidden "estimated" for a screen reader, cleared by the
next snapshot that supplies the row, and a projection never takes the exceeded class, which is reserved
for a figure the server measured. Each replayed snapshot resets the characters counted, so a reload
rebuilds the projection rather than doubling it, and the reload after a run renders reported figures only.

*Recorded in [assistant.js](../src/uk.osric.asp.Web/Pages/Seeds/assistant.js), [the assistant](assistant.md#ai-page-assistant), [assistant live figures](plans/archive/assistant-live-figures.md).*

### assistant-the-request-budget-is-tokens

The assistant's request budget is estimated tokens, not bytes. The provider bills and limits in tokens,
so the exact byte count is the estimator's input and not the budget. `TokenEstimator` starts from the
`prompt_tokens` the provider reported for the request the previous step sent — an exact count — and adds
only the bytes appended since, divided by the bytes-per-token ratio that report implies, with a
three-percent margin on that delta and a 1.6-bytes-per-token default before the first report; the ratio
is clamped between 1.0 and 6.0. Masking a payload and dropping a turn are known byte amounts, so they
move the anchor by their own size and nothing else. The prompt's share of the budget is the model's
reported window less the configured reply floor, and `max_tokens` is what that estimate leaves
([assistant-the-budget-is-window-minus-reply](#assistant-the-budget-is-window-minus-reply)).

*Recorded in [TokenEstimator](../src/uk.osric.asp.Core/Assistant/TokenEstimator.cs), [ContextBudget](../src/uk.osric.asp.Core/Assistant/ContextBudget.cs), [prompt-token-budget](plans/archive/prompt-token-budget.md).*

### assistant-old-tool-results-are-masked

Before any whole turn is dropped, `ToolResultMasker` replaces old tool results with
`[tool result elided: N chars]` and long string values in old tool-call arguments with
`[elided N chars]`. Every user and assistant word, every tool-call id and name and the tool definitions
are kept; nothing else is exempt. The mask is derived per request and never stored, so the transcript
stays complete, the changed-pages card still reads real results, and there is no migration. Tool-call
arguments are masked as well as results because a page write carries the whole page source, the largest
thing in a page-authoring history.

Masking is attempted one old message at a time, oldest first, and only when the request is already over
budget; a result or an assistant message's long arguments are masked as one payload. Whole user turns are
dropped only after masking is exhausted, never split from their tool calls and results, and the newest
turn is never dropped. A request that still does not fit fails with `context_too_long` naming the estimate
rather than being sent. A request already under budget is returned untouched, so the common step keeps
its prefix byte-identical for the provider's prompt cache. There is no protected window: a count of
exempt results can be larger than the whole budget, and the oldest-first order already says what matters
([assistant-one-budget-decides-what-fits](#assistant-one-budget-decides-what-fits)).

*Recorded in [ToolResultMasker](../src/uk.osric.asp.Core/Assistant/ToolResultMasker.cs), [ContextPlanner](../src/uk.osric.asp.Core/Assistant/ContextPlanner.cs).*

### assistant-usage-is-accumulated

A provider response's usage is recorded in full — prompt, completion, cache-hit and reasoning tokens — and
accumulated per run rather than overwritten by each step. The run keeps the totals and the last step's
output count, and the live stream mirrors the same arithmetic. The conversation page shows prompt,
cached, completion, reasoning and total, and the budget update carries the same object; cached is part of
prompt and reasoning part of completion, so the list names the subsets rather than summing them twice. The
token instrument carries all four kinds, and its panel is a line chart because the kinds no longer
partition a total.

*Recorded in [the assistant](assistant.md), [metrics](metrics.md), [RunUsage](../src/uk.osric.asp.Core/Assistant/RunUsage.cs).*

### assistant-memory-belongs-to-the-assistant

Memory is the assistant's, not the page contract's. Its store, its FTS5 recall, its versions, pins and
provenance are written by the assistant's tools inside a run and read back into the next prompt, so the
owner's view of it arrives with the assistant page rather than as a capability of its own. What a page
cannot do is keep anything for a signed-in account: page storage is shared by every visitor and a page
session belongs to one browser and goes when it signs out. That gap is [`no-account-store`](backlog/no-account-store.md),
shaped as a per-account SQL store whose tables the page declares, and a capability arrives with the view
that needs it, so the job names its first customer before it is built.

*Recorded in [page first](plans/archive/page-first.md), [where a view lives](architecture.md#where-a-view-lives).*

### assistant-memory-is-the-owners

A memory is owner-scoped and outlives the conversation that produced it. Every statement filters on the
signed-in owner in SQL, and the model never supplies one. Provenance is a nullable conversation and run id
rather than a foreign key, because a memory that dies with its source cannot be remembered into the next
conversation; deleting a conversation nulls it explicitly. The one foreign key is a memory's history to
its head, which has no meaning without it.

A memory has an id and a version. An update records the previous content as history and bumps the head
under an expected-version check, so a stale model write is refused with the current content rather than
silently overwriting an owner's edit. A forget deletes the memory, its history and its index row in one
transaction; there is no tombstone, because a memory is personal data and keeping its content is what
forgetting exists to stop. Removing an account removes its memories.

Memory content is data, not instruction: it enters the system prompt inside a labelled `<memory>` block
that says so, escaped so a memory cannot close its own element, normalised and capped on the way in, and
never written to a log line or a metric tag. A write that looks like a secret — a private-key block, a
`password=` or `token=` assignment, a provider key — is refused by rule as well as by the prompt, because
a memory persists an injection into every later conversation.

Memory is written only by an explicit tool call the transcript records — `remember`, `update_memory`,
`forget_memory` — and never by a pass that reads the conversation afterwards. The three writes are
approval-gated under `RequireApproval` and run immediately under the default, where they are still
visible as a tool call and as a row; a derived profile is rejected because it produces memory nobody
asked for and cannot be attributed to a turn.

*Recorded in [assistant memory](plans/archive/assistant-memory.md), [spike](spikes/assistant-memory.md).*

### assistant-memory-recall-is-fts5

Recall is FTS5 keyword search with porter stemming, ranked by BM25 and selected by a pure rule together
with the owner's pinned memories and those already used in this conversation, into a fixed character
budget. The index is external-content over the memory table and maintained by triggers, so there is one
copy of a memory's text. There are no embeddings, no vector store and no reranker: the provider client
speaks chat completions only, and the measured recall at this size does not need them. Recall happens once
per run, so the context figure does not move mid-run.

*Recorded in [assistant memory](plans/archive/assistant-memory.md), [spike](spikes/assistant-memory.md).*

### assistant-memory-page

The memory page is shell code under `/assistant/memory`, behind the editor policy and the owner filter,
server-rendered with no JavaScript, and one document per response: the list, a row's form, a forget's
confirmation. Every row is a form carrying the version it was rendered from, and a save answers `303`
back to the list. A memory is shown as encoded text and never rendered as Markdown or HTML, because it is
data the prompt already declares to be data.

Pinning is the owner's, and only the owner's: one store operation can change it, it is reached from the
page and never from a tool, and the assistant's update keeps a signature with no such argument. A pinned
memory is listed first inside its kind and honoured first by recall. A stale save is refused with the
stored content rather than merged, the same rule the page tools already use, and a row is one form
holding the content, the kind and the pin so one version check covers all three. A refused save
re-renders the row with the submitted text and an error line above it, because the textarea is the only
copy of what the owner typed; the answer is a `400` or a `409` carrying the page, not a bare error page.
The store is still the authority on the content: the page validates the form's shape, while
normalisation, the cap and the secret rule stay in the memory's own types.

Forgetting from the page is the same delete `forget_memory` makes — head, history and index row in one
transaction, no tombstone — and it asks on a page of its own first, which also says what the delete cannot
reach: backups, snapshots and the operator's copies, exactly as a page scrub does.

*Recorded in [assistant memory page](plans/archive/assistant-memory-page.md), [AssistantMemoryEndpoint](../src/uk.osric.asp.Web/Assistant/AssistantMemoryEndpoint.cs).*

### assistant-a-tool-is-one-type

An assistant tool is one type: it carries its name, its description, its argument schema, its approval
flag, the verb its refusals use, whether a successful call changes a stored page, the argument the path
policy refuses before a prompt, and the code that runs it. The tool registry is the only list — the
model's definitions, the dispatcher, the approval gate and the transcript's changed-pages question all
read it — and it keeps declaration order, because that order is part of the request body and of the
provider's cached prefix. A tool type that is not registered fails a contract test, and a tool that does
not implement its run does not compile.

*Recorded in [the assistant](assistant.md), [assistant tool types](plans/archive/assistant-tool-types.md).*

### assistant-reads-the-shell-files

The assistant reads the site's shell files and never changes them. The shell files are the keys in the
reserved-path configuration, and each one has a host-written record of its methods, access and purpose,
because the running image has no source tree to quote; a test pins the records to the reserved list in
both directions. Every listing command covers them, and every entry carries the assistant's own
permission in the `ls -l` first column — writable where a save would be accepted, read-only where it would
not. A save answers "Not permitted", and under `RequireApproval` the policy decides before the owner is
asked. Seeded pages are ordinary pages the assistant may edit, and a script check reads page source only,
so a shell file is refused there. There is no separate assistant-only lock list: every page the shell does
not route is a page the assistant may write, subject to the writable-path prefix.

*Recorded in [the shell](shell.md#authentication-and-the-editor), [ShellRoutes](../src/uk.osric.asp.Web/Assistant/ShellRoutes.cs).*

### assistant-reads-history

The assistant reads and restores history: `list_revisions` and `read_revision` answer through the same
reads the editor uses, and `restore_revision` appends under approval, refusing the source the page already
holds and any hash that is not a revision of that path. Both listings page with a limit and a cursor named
for its own direction — `before` for a history, `after` for a key-ordered listing — a read and a history
read share one line-range projection, and every hash check goes through the canonical revision-hash test.
This reverses the rule that the editor's restore is the owner's alone: the reason for it was the model's
inability to say what a page used to hold, and those reads remove it. A scrub still has no tool.

*Recorded in [revision restore](plans/archive/revision-restore.md#d8--list_revisions-answers-the-history-the-editor-answers).*

### assistant-edit-page-is-safe

`edit_page` answers with what it changed: the unified diff, the added and removed line counts, and
`changed: false` for an edit that would leave the source byte-identical. A no-op is not stored, because a
duplicate revision is noise in a history whose job is to record changes; the answer carries the current
hash and a warning, and the page is not listed in the transcript's changed-pages card. It takes optional
`expectFirst`/`expectLast` exact-line assertions, and a mismatch refuses the edit and names the line that
failed, because a line range counted in an escaped JSON blob can be off by one and a refusal is safer than
replacing the wrong line: the assertion refuses, it does not correct. `read_page` and `read_revision`
prefix every source line with its 1-based physical line number and `| `, and a read truncated to the
result budget keeps complete lines at both ends; the prefix is not source, and it is what removes the need
to count lines in escaped JSON. An edit takes either one line range or a non-empty array of hunks: every
hunk addresses the source that was read, ranges may not overlap, an omitted end line means one line, and
the hunks are applied from the bottom up and saved once against the same revision hash, so several hunks
leave one revision rather than one per hunk.

*Recorded in [assistant edit safety](plans/archive/assistant-edit-safety.md).*

### assistant-script-checks

Two read-only tools share one ECMAScript validator: one takes a stored page's path, and one takes a
complete page template and touches no store. Both return `valid` plus a flat list of line, column and
message, and invalid script is a successful call: only bad arguments, a missing page, a key with no page
source or an unexpected fault fails it. They are JavaScript only, do not consult the content-type map, do
not resolve imports and check syntax rather than intent. They are never a save gate, and the text tool
takes the whole page because an edit fragment can be valid only inside its surrounding function.

The parser is pure managed — no native asset, it parses and never executes, so there is no new process
boundary to argue about. `Core` blanks each template construct with spaces while keeping every line
break, so a parser's positions are page positions; a parser error is one code and up to twenty
unclosed-opener hints are another. A page is parsed as a module, so a top-level `return` is an error and a
duplicated top-level declaration is a redeclaration. Page code cannot reach the parser: the API policy is
default-deny.

*Recorded in [script validation](plans/archive/assistant-script-validation.md), [JavaScriptValidator](../src/uk.osric.asp.Runtime/JavaScriptValidator.cs).*

### assistant-line-numbers-are-physical

Assistant tool line numbers are 1-based physical lines of stored template source, before tokenization,
generated C#, or rendering. Literal HTML, C# blocks and delimiters count as authored; CRLF is one break,
LF and CR are each one, and a trailing line break creates an empty final line.

*Recorded in [AI page assistant](plans/archive/ai-page-assistant.md#tools), [PageRangeEditor](../src/uk.osric.asp.Core/Assistant/PageRangeEditor.cs).*

### assistant-the-shell-lexes-before-it-judges

The virtual shell lexes the whole command line before it judges any of it: the words with their quoting
and escaping applied, and the first operator the line carries outside quotes. Quoting is what makes a
punctuation character data, so a quoted search is a search and an unquoted one is refused, and no rule has
to know what one command's arguments mean in order to read the line. A single quote is literal
throughout, a double quote escapes only `"`, `\`, `$` and the backtick, a backslash outside quotes takes
the next character literally, adjacent parts join into one word and an empty pair of quotes is an empty
word. `$` and a backtick are refused outside single quotes rather than passed through as data, because
there is no expansion. A first word that is not a command is refused as that word whatever else the line
carries.

*Recorded in [the assistant](assistant.md#ai-page-assistant), [ShellLine](../src/uk.osric.asp.Core/Shell/ShellLine.cs).*

### assistant-the-shell-is-posix

Where the virtual shell implements a command, it implements the POSIX command: its options keep the
letters and meanings POSIX gives them, its default output is the POSIX output, and its behaviour on the
edges — an empty answer, a pair of operands, a count — is POSIX's too. The shell is nowhere near all of
POSIX and a command it does not answer is refused as not there, but what is there is not an invention: a
caller who knows a shell already knows the command, and a deviation from POSIX is a bug to fix rather than
a preference to record.

The store is read as a tree of keys: a path names a page or a folder, a listing command shows what is at
the path and a recursive one everything below it, a search reads the level it is given and descends only
when asked, and a key that exists only because another lives beneath it is listed as a folder too, because
a folder that cannot be listed is one nobody can find. Options mean what their letter says, and an option
a command does not take is refused rather than ignored. A search's query is a basic regular expression —
`^` and `$` anchor, `.`, `*` and `[...]` are metacharacters, `\|` is alternation, everything else is
literal — and matching is linear time, so a pattern cannot spend a run's budget. A result that reached the
match or listing cap, and a search whose match lies below the level it read, say so rather than leaving an
answer that looks complete.

`find` is the POSIX command rather than a long listing under another name, with its own pattern type and
its own expression of tests joined by an implicit and; an unknown primary is refused by name rather than
ignored. `diff` writes what POSIX `diff` writes: the normal format by default, unified with a context
count, and white-space-insensitive on request by comparing a collapsed copy of each line and putting the
text back as it was written, so the ignored difference is the space and never the line. Two operands only,
identical documents write nothing, and the result carries `identical` so an empty answer cannot be
mistaken for a failure.

*Recorded in [the assistant](assistant.md#ai-page-assistant), [ShellRunner](../src/uk.osric.asp.Web/Assistant/Shell/ShellRunner.cs), [GrepPattern](../src/uk.osric.asp.Core/Assistant/GrepPattern.cs).*

### assistant-the-shell-is-applets

The virtual shell is a kernel and applets, and a command is one type: an applet carries its name, its
usage line, the description help prints, the notes that need more than a line, the syntax the kernel
validates against, and the code that runs it. Adding a command is a file and a row in the registry, and
`help` renders the registry, so the help text cannot fall behind the commands.

Under them the kernel is one pipeline: the line lexes into words and operators; the registry finds the
applet; one algorithm turns the words into flags, values and operands for every command, so flags cluster
and values attach; expansion turns the operands into nodes, patterns expanded and folders known; and the
applet runs against a context of the expanded nodes, the one read seam and an output to write to. Lexing,
parsing and expansion happen once, so no applet reads its own command line and none can forget a pattern.
Operators come out as tokens and are refused for now — that refusal says "yet" — which is where pipelines
and redirections will go, with the applets already writing to an output and the shape of an input waiting.

*Recorded in [the assistant](assistant.md#ai-page-assistant), [ShellApplet](../src/uk.osric.asp.Web/Assistant/Shell/ShellApplet.cs), [ShellSyntax](../src/uk.osric.asp.Core/Shell/ShellSyntax.cs).*

### assistant-a-view-is-a-read

`view` names a path on this site — never a URL — and is fetched as the owner the run belongs to, so the
editor, the conversation, the memory page and the status page render instead of answering a sign-in
redirect. It needs no approval: it reaches nothing but `Site:Origin`, writes nothing, and answers a
question about the run's own work, which is the standing `read_page` has, where `fetch` stops for the
owner because it can carry what the model just read to somebody else's server. A page cannot name the
token, because it is not in `Pages:Fetch:Credentials` and has no configuration key.

*Recorded in [assistant](assistant.md#ai-page-assistant), [ViewTool](../src/uk.osric.asp.Web/Assistant/Tools/ViewTool.cs).*

### assistant-a-view-is-the-response

A view answers what the server produced: the status, **every** response header including `Set-Cookie`
and the security policy, the body's own bytes read as UTF-8 (base64 when the body is not UTF-8, never
replacement characters), the redirects it followed, and the byte length of what came back when a cap cut
it. A redirect chain is what makes a guard visible: `302 → /login?returnUrl=…` then 200 cannot be
mistaken for a page that renders a sign-in form. `fetch` keeps its own flattening — nine allow-listed
headers, and text decoded by the charset the response declares — because that is what a *page* wants, so
the two answers are one transport with two projections rather than one shape with a flag.

*Recorded in [assistant](assistant.md#ai-page-assistant), [OutboundResponse](../src/uk.osric.asp.Web/Pages/OutboundResponse.cs).*

### assistant-the-balance-is-sampled-on-a-clock

The provider account's credit is sampled by a background service at startup and then every
`Assistant:BalanceSampleSeconds`, not when the conversation page is opened. The page's fetch is free to
reuse, but a history built from page views is a diary of the owner's visits, where a batch of runs
between two visits draws as one step at the second.

*Recorded in [assistant](assistant.md), [AccountBalanceSampler](../src/uk.osric.asp.Web/Assistant/AccountBalanceSampler.cs).*

### assistant-a-balance-read-carries-its-reason

Reading the balance answers a value — the figures, or one of a closed set of reasons (`unsupported`,
`error`, `timeout`, `unreachable`, `invalid`) — and the client logs nothing. The page shows one sentence
for any absence, and the sampler logs a reason when it changes. A client that logs its own failures
cannot be used twice without one caller deciding the other's logging.

*Recorded in [assistant](assistant.md), [AccountBalanceRead](../src/uk.osric.asp.Core/Assistant/AccountBalanceRead.cs).*

### assistant-a-balance-is-read-as-a-number

The provider's money text is parsed once in `Core` as an invariant-culture decimal. A figure that is not
a number is skipped rather than recorded as zero, because a zero on the chart reads as credit gone; a
negative figure is recorded as it came, because a provider that owes the account money is telling the
truth. An unreadable figure logs its currency code and never the provider's text.

*Recorded in [assistant](assistant.md), [AccountBalanceAmounts](../src/uk.osric.asp.Core/Assistant/AccountBalanceAmounts.cs).*

### assistant-the-balance-cadence-is-configuration

The cadence of the outbound balance read is `Assistant:BalanceSampleSeconds`, a policy value with no
fallback: it decides how much history a day has and how much of the provider's time is spent, and its
range (30 to 3600 seconds, 60 shipped) is a safety net rather than a ration.

*Recorded in [assistant](assistant.md), [AssistantOptions](../src/uk.osric.asp.Web/Assistant/AssistantOptions.cs), [appsettings.json](../src/uk.osric.asp.Web/appsettings.json).*

### assistant-tokens-anchor-on-the-last-report

Every step's estimate starts from the provider's `prompt_tokens` for the previous request and adds only
what has been appended since. The margin covers the delta, not the conversation, so it is three percent
rather than ten, and every step records how far the estimate was from the count the provider returned,
both as a log line and as `assistant.context.estimate.error` labelled `over` or `under`. The exact
tokenizer the spike measured was rejected: it costs 1 MB of DeepSeek-acquired vocabulary, a chat-template
renderer and a fixture regenerated on every vocabulary bump, and still cannot see how the API injects
`tools`. The anchor has the same blind spot and needs none of the rest.

*Recorded in [TokenEstimator](../src/uk.osric.asp.Core/Assistant/TokenEstimator.cs), [RunProcessor](../src/uk.osric.asp.Web/Assistant/RunProcessor.cs), [spike](spikes/deepseek-token-counting.md).*

### assistant-one-budget-decides-what-fits

There is one content budget, derived from the model's window, and every other content cap is a fraction
of it or gone: one tool result and one owner's message are an eighth of the prompt's share, a run's
memory block a thirty-second. The fractions are invariants in `ContextBudget` rather than configuration,
because a value that could raise one to the whole budget would switch the guard off. The one exception
is a tool call's arguments, which must carry a page and so is `MaxPageChars` plus room for the envelope.
A cap that cannot be derived this way must either come from the model or have a note and a way to ask
again; a free-standing character count never decides what the model may read. `MaxContextTokens`,
`ModelContextTokens`, `MaxOutputTokens`, `MaxToolResultChars`, `MaxMessageChars`,
`MaxMemoryCharsPerRun` and `MaxMemoriesPerRun` are gone.

*Recorded in [ContextBudget](../src/uk.osric.asp.Core/Assistant/ContextBudget.cs), [AssistantOptions](../src/uk.osric.asp.Web/Assistant/AssistantOptions.cs), [assistant](assistant.md).*

### assistant-tool-results-are-cut-not-refused

At a cap, a tool result is truncated, marked `truncated: true` and named in the note, and the head is
kept. It is never replaced by a refusal, because a refusal loses the information and the fact that there
was information, and the model usually calls the tool again. Refusals stay for what they are for: an
invalid argument, a stale revision hash, a path outside the writable prefix, a missing page.

*Recorded in [ToolResults](../src/uk.osric.asp.Web/Assistant/Tools/ToolResults.cs), [assistant](assistant.md), [metrics](metrics.md).*

### assistant-the-budget-is-window-minus-reply

The model's limits come from the provider's model list, not from configuration: `GET /models` publishes
each model's `context_window` and `max_output_tokens`, and `ModelWindowSampler` reads it once at startup
and again when a request is rejected for length. The prompt's share is the window less
`Assistant:MinOutputTokens`, the reply's floor; `max_tokens` is what the estimate leaves of the window,
capped at the published maximum and floored at that minimum, so prompt and reply always add up to the
window. A provider that does not answer leaves the assistant without a budget and a run refuses with
`model_window_unknown`; a provider that answers without the configured model stops the deploy, because
that answer is authoritative. Neither `Assistant:MaxContextTokens` nor `Assistant:ModelContextTokens`
exists any more.

*Recorded in [ContextBudget](../src/uk.osric.asp.Core/Assistant/ContextBudget.cs), [ProviderModelsClient](../src/uk.osric.asp.Web/Assistant/ProviderModelsClient.cs), [ModelWindowSampler](../src/uk.osric.asp.Web/Assistant/ModelWindowSampler.cs).*

### assistant-the-vocabulary-is-the-providers

Where the provider has a word, use it; where it does not, one concept has one word. A tool's returned
content is a **tool result** everywhere — `ToolResultMasker`, the budget row and the decision that records
masking — and never an observation, a tool output or an answer. The owner's message is a **message**, not
a prompt; *prompt* names only the system prompt unless it is the provider's `prompt_tokens`. The reply's
quantity is a **completion**, and `RunUsage` says `LastCompletionTokens` for the last step and
`CompletionTokens` for the run. `max_tokens` is the request's own field name, and the only configured
figure left is `Assistant:MinOutputTokens`.

*Recorded in [assistant](assistant.md), [glossary](glossary.md), [RunUsage](../src/uk.osric.asp.Core/Assistant/RunUsage.cs).*

### assistant-a-question-is-a-tool-call

The assistant asks its owner with a tool, `ask_user`, whose result is the answer: one to four questions
in one call, each with two to five choices or none and always room for the owner's own words, returned
in the order asked. The wait is the owner interrupt's third kind — a `question` request, a card
rendered from the record, and a resolution the tool reads after the run wakes — so a reload, a second
device or a disconnected browser changes nothing, and no clock judges it: the tool timeout does not
apply and the owner's wait is taken off `assistant.tool.duration`. A question left blank is an answer
(`selected: []`, `text: ""`), the owner leaving the decision to the model, with no second flag to
interpret. The card is one form holding every question, so the owner can read the whole set before
answering any of it, and a finished turn reads the question and the answer from the resolved records
rather than the tool result, which may be cut at its cap. The questions are bounded by
`Assistant:MaxQuestionsPerCall`, `MaxQuestionOptions` and `MaxQuestionChars`, and the answer by the
owner message's share of the budget. A tool argument may describe itself in the schema, emitted only
when set, and this tool's arguments do.

*Recorded in [the assistant](assistant.md#ai-page-assistant), [the ask-owner plan](plans/archive/assistant-ask-owner.md), [OwnerQuestions](../src/uk.osric.asp.Core/Assistant/OwnerQuestions.cs).*

## Deploy

### deploy-one-small-host

One Hetzner Cloud VM: a CAX21 — four Ampere arm64 vCPUs, 8 GB of RAM, an 80 GB SSD, Debian 13 — with
rootless podman quadlets for the app and Prometheus, a separate `ci` account that runs the gate in a
2 vCPU / 4 GiB slice, and rootful Caddy as the only privileged unit. Caddy owns 80 and 443 and proxies
to the app's loopback port. The move from the older x86 host replaced it rather than adding to it: the
topology is unchanged.

*Recorded in [deploy](../deploy/README.md), [cloud-init](../deploy/cloud-init.yaml), [the CI
slice](../deploy/ci/uk.osric.asp-ci.slice).*

### deploy-ci-is-its-own-account

The gate runs on the host as `ci`, a separate account from `deploy`: no sudo, a `git-shell` login, its
own subuid ranges, a bare clone and a 2 vCPU / 4 GiB slice. The account that receives a push is not
the account that runs the site; the only credential it holds is the GitHub deploy key, and the app's
configuration, keys and storage are not reachable from it.

*Recorded in [deploy](../deploy/README.md#ci-on-the-host), [install-host](../deploy/ci/install-host),
[cloud-init](../deploy/cloud-init.yaml), [Makefile](../Makefile), [the plan](plans/archive/host-ci.md).*

### deploy-trunk-is-checked-after-push

A push to `trunk` is accepted and then checked: `post-receive` starts `make ci` in a transient unit,
and the result is written to `/var/lib/uk.osric.asp-ci/status.json` and served by Caddy at
`/ci/status.json`; the run's whole output is a log file beside the account, because journald drops a
burst of tens of thousands of lines. A red trunk is fixed forward; the status file is the answer, not
a blocking gate. The check is the same `make ci`, not a second definition of green.

*Recorded in [check-push](../deploy/ci/check-push), [post-receive](../deploy/ci/post-receive),
[Caddyfile](../deploy/Caddyfile), [the Caddy quadlet](../deploy/caddy.container),
[the plan](plans/archive/host-ci.md).*

### deploy-github-mirrors-the-host

The host is the upstream; GitHub is a one-way, pruned mirror of its branches and tags. The bare
repository is cloned from GitHub at install so existing refs survive, and the mirror runs on receive
and on nothing else — a failed run restarts itself for ten minutes as the retry, and the next push is
the next attempt. The deploy key can write this repository and nothing else; there is no branch
protection on the copy, because the gate and the authority are on the host.

*Recorded in [mirror](../deploy/ci/mirror), [the service](../deploy/ci/uk.osric.asp-mirror.service),
[post-receive](../deploy/ci/post-receive), [deploy](../deploy/README.md#ci-on-the-host),
[the plan](plans/archive/host-ci.md).*

### deploy-the-image

The image is built by the SDK's container targets rather than a Containerfile: `dotnet publish
-t:PublishContainer` packs the publish output, adds the labels and hands the image to podman, and the
SDK's `SOURCE_DATE_EPOCH` (the commit's own timestamp) makes two builds of one commit one image. The base
is pinned by digest and the SDK never enters the image. The base image is `dotnet/aspnet:10.0-alpine-extra`
for its `/usr/share/zoneinfo`: the status page labels its time axis in `Europe/London` and plain alpine
carries no tzdata.

The app ships framework-dependent: a self-contained single file is smaller on disk but moves ~52 MB per
release, and trimming would reach ~29 MB and dies at startup, so trimming is deliberately off. The image
is built for `linux-musl-arm64` on `linux/arm64` by default, matching the host; an x86 host overrides both
the RID and the platform, and the base's manifest-list digest is shared, so the same pin resolves on
either architecture. `make image-check` proves two builds of one commit match.

*Recorded in [deploy](../deploy/README.md), [operations](../deploy/operations.md#image-layout-and-size).*

### deploy-loopback-and-caddy

The app publishes on the loopback only, and that is the control that makes trusting every proxy's
forwarded headers safe: do not widen the publish port or add another proxy without revisiting it. There is
no `UseHttpsRedirection` in the app — Caddy redirects HTTP to HTTPS, and doing it twice behind a proxy is
a redirect loop waiting to happen.

*Recorded in [operations](../deploy/operations.md#proxy-trust-and-forwarded-headers).*

### deploy-a-release-is-gated

`make deploy` refuses a dirty tree and skips the image work when the host already runs the commit; the
override is a deliberate exception, not a default. It runs the browser suite before the image is built,
because a deploy is rare enough that two minutes is cheap and a deploy is the moment it matters that the
site serves a page in a browser at all. Releases travel through the private registry with an explicit auth
file on the server, because podman's default auth file lives under the runtime directory and is gone after
a reboot.

*Recorded in [Makefile](../Makefile), [deploy](../deploy/README.md#the-registry).*

### deploy-quadlets-are-the-source-of-truth

The quadlets, Caddyfile and CI files in `deploy/` are the source of truth. The host is a deployment
target, never edited by hand, and so are the steps that install it: `make deploy` runs
`deploy/deploy-host`, which is a file in the tree like the quadlets it writes.

*Recorded in [Caddyfile](../deploy/Caddyfile), [the CI hooks](../deploy/ci/post-receive),
[deploy-host](../deploy/deploy-host), [the instructions](../.github/copilot-instructions.md).*

### deploy-target-is-a-script

`make deploy` is the entry point and the release's steps are `deploy/deploy-host`, beside the files
they install. The target keeps what belongs to a task runner — the gate (`deploy-guard check
test-browser image`), the control-socket directory the first `ssh` needs, and one call — and the
script holds the package checks, the image's push and pull, the app's paths, the rootless and rootful
units, the CI status seed, the three waits and the note at the end, with the reason for each step
beside the command that performs it.

The values it needs are named and passed on the one call — the host, the ssh command, the three image
tags, both auth files, the status directory and the commit — and none has a default in the script, so
a direct run refuses rather than installing a host on a guess. A helper the target calls is not a
parallel entry point: `ci-install` and `pull-install` already work this way.

*Recorded in [deploy-host](../deploy/deploy-host), [Makefile](../Makefile), [deploy](../deploy/README.md),
[the plan](plans/archive/deploy-host-script.md).*

### deploy-caps-and-swap

The container is capped — memory with an equal swap cap, a process limit and a CPU share — and probed
regularly with three failures allowed, so a runaway page takes the container and not the host, and a
wedged process becomes a ~35 s outage. The caps scale with the host, leaving a whole vCPU for Caddy and
the probe. The app's cgroup cannot touch the host's swap file: the swap is headroom for Caddy, Prometheus,
podman and the kernel, and keeping it away from the app is what keeps a runaway page a short outage rather
than a thrash that outlasts the probe.

*Recorded in [operations](../deploy/operations.md#resource-limits-and-restarts), [the app quadlet](../deploy/uk.osric.asp.container).*

### deploy-the-firewall-and-the-backups

`ufw` denies inbound by default and allows SSH, HTTP and HTTPS; unattended upgrades are enabled on the
host, and **no cloud firewall** is an accepted policy choice, recorded rather than assumed and revisited
deliberately rather than by accident. Backups use SQLite's online backup or a copy of the stopped volume:
copying live database files is refused because the write-ahead log can hold committed data, and a
database backup is a credential store, because it holds password hashes and stamps. Backups are no longer
a habit: [deploy-backups-are-pulled](#deploy-backups-are-pulled) is what takes and keeps them.

*Recorded in [cloud-init](../deploy/cloud-init.yaml), [operations](../deploy/operations.md#backups).*

### deploy-backups-are-pulled

The site snapshots `storage.db` nightly — SQLite's online backup through `podman unshare`, checked with
`PRAGMA integrity_check`, kept as a 31-slot ring of daily archives beside the two host-only configuration
files — and the backup host pulls the newest one on its own timer, with a key restricted to read-only
`rrsync` of that one directory. The direction is the rule, not an artefact of wiring: the public host
holds no credential to the machine that keeps its history, the copy is verified where the failing host
does not control it, and retention belongs to the destination, which keeps dated directories rather than
mirroring a source that can have a bad day. The site never deletes what the pull has taken, and the pull
never deletes on the site.

*Recorded in [operations](../deploy/operations.md#backups), [the snapshot script](../deploy/backup-snapshot.sh),
[the pull script](../deploy/pull-backups.sh), [the snapshot timer](../deploy/uk.osric.asp-backup.timer),
[the pull timer](../deploy/uk.osric.asp-pull.timer), [the plan](archive/offsite-backups.md).*

### deploy-no-outbound-alerting

No outbound alerting yet. A failure is discovered by visiting the site or reading the journal; a job
carries the notification work.

*Recorded in [`ops-no-outage-notification`](backlog/ops-no-outage-notification.md).*

### deploy-the-domain-is-public

The hostname and domain are public: they are in the repository and the Caddyfile. An earlier attempt to
keep the domain out bought nothing, because robots find it anyway.

*Recorded in conversation, 2026-10-31.*

## Metrics

### metrics-prometheus-only

Metrics are exported by OpenTelemetry's Prometheus exporter, metrics only: no traces, no logs, no OTLP, no
instrumentation packages. Instrument names, tags and bucket scales are a dashboard contract, so they live
beside the code that records them rather than in a library's defaults. Prometheus runs rootless beside the
app on a shared network and scrapes the app's loopback-facing port; it is never proxied. `/metrics` keeps
two gates — the loopback-only publish and a proxy-level 404 on the public origin — and `/admin/status` is
owner-only, server-rendered and JavaScript-free.

*Recorded in [metrics](metrics.md#export-and-the-status-page), [operations](../deploy/operations.md#metrics-and-the-status-page).*

### metrics-logs-are-one-json-line

Logs go to the journal as one single-line JSON object per entry, written through the console JSON
formatter with scopes included: podman hands the container's stdout to systemd, so a line is an entry and
a scope's fields stay queryable. The systemd formatter was rejected because it flattens a scope to text,
which is the one thing a line about a failed run needs. Levels are about who can fix it — Error is our
defect, Warning is something outside this repository, Information is the rumble of things that happened,
Debug is noise for a developer's own machine — and a scope carries the ambient ids and the signed-in
account, filled in as they become known rather than left out.

*Recorded in [logging](logging.md), [architecture](architecture.md#logging).*

### metrics-one-panel-per-instrument

`/admin/status` draws one panel per instrument, and the instrument's type decides the chart: a histogram is a
heatmap, a gauge a line chart, and a counter a line chart unless its labels partition one total, when it
is a stacked area. A question that spans instruments may add a composed panel that names its sources and
owns none. The catalogue is the only source of panels, the dashboard holds no queries, and a test fails
when either side gains an entry the other lacks, when a type disagrees with the chart, or when a query
names a family that is neither an instrument nor a known runtime one.

A bucket scale follows the work it measures: the assistant's instruments are minutes and dozens rather
than page milliseconds, so each carries its own scale beside the meter that records it. A histogram's
scale reaches past the cap its series measures, because a sample that hit its deadline is the one the
panel was opened for and a top boundary at or below the cap files it under `+Inf`; the caps were brought
down to what the work reaches rather than letting an inflated one stretch the scale, and a test reads the
shipped configuration to hold each cap to its scale. A series with no cap at all is outside that rule.

*Recorded in [metrics](metrics.md#the-panels), [PageMetrics](../src/uk.osric.asp.Web/Pages/PageMetrics.cs), [AssistantMetrics](../src/uk.osric.asp.Web/Assistant/AssistantMetrics.cs).*

### metrics-a-series-is-identified-without-colour

The page is a dense grid of figures under three group headings — Pages, Assistant, Host — and each drawing
carries its own title and legend inside its SVG, so a panel adds no heading, no legend list and no prose;
the drawing's title is the link that opens the same chart on its own. A series is identified without
colour: a colour-blind-safe palette, a dash pattern per series, a direct label at the end of every line, a
hover or focus that dims the others and a click that pins one by fragment target, all in CSS because
`/admin/status` stays JavaScript-free. A series' legend entry sits inside the series' anchor, so the legend
behaves the same way, and a heatmap's row is its series. Each drawing is self-contained: colour, dash, fill
and type size are presentation attributes from a palette in `Core.Charting`, not rules in a stylesheet, so
the same SVG is legible with no stylesheet, and the page's sheet keeps only the grid, the caption and the
hover and pinned states, which are properties of the page rather than of the picture.

*Recorded in [metrics](metrics.md#the-panels), [status page rebuild](plans/archive/admin/status-page-rebuild.md#identifying-a-series-without-colour).*

### metrics-a-chart-stands-alone

A chart can be served on its own: one route returns one panel's SVG under the owner policy, with
`no-store` and the global security headers, addressing panels by catalogue id. The window is an explicit
start plus a length, snapped to the step, so a link reproduces the chart it showed; a malformed window is a
`400`, an unknown panel a `404` and an unreachable backend a `503`. The page embeds the same renderer and
never fetches the endpoint. A chart served on its own is a standalone SVG document and declares its
namespace on the root element: inline in the page the namespace is implied, but a browser opening the
route directly treats an element without one as XML it does not recognise and shows the markup as a tree.

*Recorded in [metrics](metrics.md#one-chart-on-its-own), [ChartChrome](../src/uk.osric.asp.Core/Charting/ChartChrome.cs).*

### metrics-a-chart-counts-from-its-window

A chart's time axis carries the window it covers, and every sample, grid line and label is placed at its
fraction of that window rather than measured from the first sample. A metric that started late is drawn
where it started, a quiet panel is not stretched across the plot, and an empty answer keeps the axis the
query asked for rather than one inferred from nothing. A sample outside the window is cut at the edge,
interpolated from its neighbours, instead of being pinned to it, so an overshoot ends where it leaves the
picture. A heatmap cell is drawn at its bucket's instant and as wide as the step between buckets, so its
columns and the time labels under it agree. The renderer supplies the window it queried; nothing infers a
window from the samples.

*Recorded in [metrics](metrics.md#the-panels), [ChartTimeAxis](../src/uk.osric.asp.Core/Charting/ChartTimeAxis.cs), [ChartClip](../src/uk.osric.asp.Core/Charting/ChartClip.cs).*

### metrics-a-balance-is-a-level

A provider account's balance is an observable gauge tagged `currency`, and its panel is a line chart
queried as a level (`max by (currency)`), so each currency is its own series rather than two currencies
merged into one line. It is read beside `assistant.tokens` as the effective price: a step up is a
top-up, and the slope is the spend.

*Recorded in [metrics](metrics.md#the-panels), [AssistantMetrics](../src/uk.osric.asp.Web/Assistant/AssistantMetrics.cs), [StatusCatalogue](../src/uk.osric.asp.Web/Metrics/StatusCatalogue.cs).*

### metrics-a-failed-sample-keeps-the-last-level

A failed sample leaves the last good level in place rather than reporting nothing or zero, as the
storage gauges already do. A dead sampler therefore draws a flat line rather than a gap; its log line is
where a stuck sample is diagnosed. An age-of-sample gauge is rejected until a second level needs the
same guard, when it becomes one panel for all of them rather than one more for each.

*Recorded in [metrics](metrics.md#the-panels), [AccountBalanceSampler](../src/uk.osric.asp.Web/Assistant/AccountBalanceSampler.cs).*

## Retired

A name that was used and then removed is listed here with what replaced it, so it is not taken again by
someone who cannot see that it was once used.

- `pages-the-editor-capability` — replaced by **pages-any-page-may-write-the-store**. It made the
  editor's registration the authority: one content key, `Pages:Editor:Key`, and the store's write offered
  to no other page. The owner removed that narrowing on 2026-11-15.
- `shell-admin-is-a-page` — replaced by **shell-admin-is-a-shell-route**. It kept `/admin` as a seeded
  fragment index so the owner could edit it; the index is the shell's now, because it draws the shell's
  own route table and must answer when the page store is broken. The owner retired it on 2026-11-16.

The numbering scheme this file replaces was a way of writing decisions down rather than a decision with a
name, and the entries that were already superseded when this file was written are in the
[frozen file](archive/decisions.md), which is where their numbers still mean what they meant.

## Open questions

Not decisions yet — recorded so they are not rediscovered:

- The process boundary (`sec-page-runs-in-process` step 3) rests on the owner being the only author. If that premise changes,
  `pages-there-is-no-sandbox` changes with it.
- A page that blocks synchronously is unchanged by `pages-page-io-is-async`: async makes a *waiting* page
  cheap to abandon, and does nothing for arbitrary C# the deadline cannot interrupt.
- `pages-the-api-policy` denies `Task`'s starting and blocking members, not `Task` itself. A future BCL
  type that starts work beside a render would need its own entry, because the allow-list admits types and
  the member list is the only exception surface.
- `pages-page-storage` makes page state shared by every visitor and racy under concurrency: an increment
  is last-write-wins, not atomic. The identity hook a per-user store needs exists, and that store is
  [`no-account-store`](backlog/no-account-store.md) — a job with its own plan, migration and decision rather than a blocked
  one. An atomic increment would still be a different storage primitive.
