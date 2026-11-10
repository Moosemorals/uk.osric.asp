<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Architecture

The words this document uses — page, key, revision, image, run, step and the rest — are defined in
[the glossary](glossary.md), which is the record of the vocabulary. This document is the map: the
core/shell split, where a piece of code belongs, and the shape of the code itself. The subsystems it
introduces have their own documents, linked at the end.

## Functional core, imperative shell

The boundary is pure rules over values, with effects isolated in their own project where the compiler
can enforce it.

- **`src/uk.osric.asp.Sdk`** is the page contract: `IContext`, `PageRequest`, `PageResponse`,
  `IHandler`, `UserFacingException`, and the capability interfaces a page may ask for through
  `IContext.Capabilities`. It is what a compiled page binds against and the only product assembly a page
  may name. It has no references at all, so nothing it exposes can drag the host in.
- **`src/uk.osric.asp.Core`** holds the host's pure half: the template tokenizer and source
  generator, the page diagnostics, the API policy, page identity and the chart helpers. It has no
  project or package references at all, so it cannot reach for I/O, a clock, DI or ASP.NET. Its only
  dependency is the base class library.
- **`src/uk.osric.asp.Runtime`** holds the imperative half of page execution: `PageCompiler` reads
  platform assembly paths, generates assembly names, emits to a stream, and loads and invokes the
  compiled handler in a collectible `AssemblyLoadContext`. It also applies the page API policy
  (`PageApiChecker`) before emitting, and hands each image a `PageLoadContext` that refuses the
  shell's assemblies. It also parses page JavaScript (`JavaScriptValidator`) so the assistant can
  check a script without running it, and renders Markdown through `MarkdownRenderer` (Markdig) for
  stored pages and the assistant transcript. It references `Core`, `Sdk`, Roslyn, Acornima and
  Markdig, and nothing else — in particular no ASP.NET, storage or logging types, because it is not
  built on the Web SDK.
- **`src/uk.osric.asp.Web`** owns HTTP endpoints, hosting, configuration, `SimpleContext`, and source
  storage. EF Core, SQLite, database migrations and `IPageStore` belong here.

The dependency direction is one-way: `Web` → `Runtime` → `Core`, `Web` → `Sdk`, and
`Runtime` → `Sdk`. Nothing points back up. `PageCompiler` returns a `CompileResult` of an
optional image and `PageDiagnostic` values rather than Roslyn `EmitResult`/`Diagnostic` values, so
the shell never sees the compiler's types.

Nothing the author types is an exception either. A template that does not parse comes back from
`TemplateSourceGenerator` as a `TemplateGenerationResult` with no source and a `TEMPLATE`-coded
`PageDiagnostic`, so it is stored beside the source exactly like a compile error: the editor shows
it, the page answers "Handler failed to compile.", and it is not recompiled on every request.

A template is text, `<% %>` code blocks, `<%! %>` declaration blocks, and expression blocks:
`<%= expression %>` writes a string through `ctx.Write` (HTML-encoded), while
`<%== expression %>` uses `ctx.WriteRaw` (unencoded). Expressions must be strings; convert other
values explicitly, such as with `.ToString()`. The generator emits the corresponding call, trimming
expression edges and removing at most one trailing semicolon. Declarations are hoisted above
`GeneratedPage.HandleAsync` in template order, so a page's `using` directives and types are visible to
its whole body. Declarations are compile-time only: they write no output and are never conditional,
even inside an `if` in the template. Template order is also the using order, so a `using` written
after a type is `CS1529` on the line that wrote it.

Diagnostics live in template space: `PageDiagnostic.Position` is a 1-based `TemplatePosition` in the
page the author typed, or `null` when there is no position. The generator wraps each code block in
`#line` directives that name the template line and pads the first line to the template column.
Expression code is mapped separately from its generated `ctx.Write(...)` or `ctx.WriteRaw(...)`
wrapper, so expression errors point to the expression and wrapper errors map to the opener's line.
For a wrapper diagnostic, Roslyn's column identifies the generated method name (for example,
`Write`), not the opener itself. The closing braces are mapped to one line past the template, which
turns an unclosed block into a positioned author error. A Roslyn error with no mapped position, and
no mapped error explaining it, is a fault in generated code: `PageCompiler` throws instead of
returning a diagnostic, `PageEditor` preserves the source and rethrows, and the host answers `500`.
Warnings are author-facing only when they are mapped; a warning never stops a page compiling.

Author-facing codes are handed out once and never reused for a different meaning, because the editor
and the logs quote them:

| Code | Raised by | Means |
| --- | --- | --- |
| `TEMPLATE001` | `TemplateTokenizer` | a statement, escaped-write, raw-write or declaration opener has no `%>` |
| `TEMPLATE002` | `TemplateSourceGenerator` | a `<%=` or `<%==` expression is empty |
| `PAGE001` | `PageApiPolicy` | the page used an API it may not use |
| `PAGE002` | `PageApiChecker` | page code escaped its method, or a declaration block declared something other than a type |
| `PAGE003` | `PageImageBuilder` | the page waited too long for a compile slot |
| `PAGE004` | `PageImageBuilder` | the page took too long to compile |
| `PAGE005` | `Sdk`, from the `[Obsolete]` attributes on `IContext.Features` and `IPageFeatures` | the page used a capability name DEC-228 retired |
| `SCRIPT001` | `JavaScriptValidator` | the assistant's script check found a syntax error |
| `SCRIPT002` | `JavaScriptValidator` | the assistant's script check found an opener the failed parse never closed |

## Where does code go?

| Kind of code | Home |
| --- | --- |
| Business rules, calculations, parsing, formatting, validation | `Core`, as pure functions |
| Values passed between layers | `Core`, as `record`/`readonly record struct` |
| A view a visitor reads, or a form they submit | A stored page, and `Pages/Seeds` when it ships with the site |
| Emitting a page assembly, loading it and invoking the handler | `Runtime` |
| Deciding when to compile, store and run a page | `Web`, in `PageImageBuilder`, `PageRenderer` and `PageEditor` |
| HTTP binding, status codes, responses, hosting, `SimpleContext` | `Web` |
| I/O, time, randomness, configuration | `Runtime` or `Web`, turned into plain values before calling the core |
| Source storage: the EF Core context and its migrations | `Web`, under `Storage` |
| A table's EF entity | `Web`, with the vertical that owns it (`Users`, `Authentication`, `Pages`, `Assistant`) |
| The SQLite store behind a vertical's interface — `SqlitePageStore`, `SqliteUserStore`, `SqliteConversationStore` | `Web`, beside the interface it implements |
| Local accounts, password hashing and the `users` command | `Web`, under `Users` |
| Cookie authentication, the `Editor` policy, antiforgery and login throttling | `Web`, under `Authentication` |
| The page contract: `IContext`, `PageRequest`, `PageResponse`, `PageUser`, `IHandler`, `UserFacingException`, `IPageCapabilities`, `IPageStorage`/`PageValue<T>`, `IHttpFetch`/`HttpFetchRequest`/`HttpFetchResponse`/`HttpFetchMethod`/`HttpFetchFailure` | `Sdk`, as plain values a compiled page binds against |
| The page policy: `PageApiPolicy`, `ApiReference` | `Core.Compilation`, as a pure rule over strings |
| Which media type a page's key names, and what an unmapped key falls back to | `Core.Pages`, as `PageContentTypes` beside `PagePath`; `Web` reads the section |
| The Roslyn walk that applies the page policy | `Runtime` |
| The template language and its diagnostics | `Core.Compilation` |
| The URL rule a restricted Markdown render applies | `Core.Markdown`, as `LinkPolicy` |
| Markdown parsing and HTML generation | `Runtime`, as `MarkdownRenderer` and its renderers; the page capability is `Sdk/IMarkdownRenderer` |
| The `/status` charts, including the palette and the presentation attributes that make one legible on its own | `Core.Charting`, as `LineChart`, `Heatmap`, `StackedAreaChart` and `ChartChrome` |
| Text whose case a key ignores — the page key and an account name | `Core.Text`, as `LowercaseText` |

When something in the shell needs a decision from a pure rule, pass the data in and return a value;
do not hand that rule `IPageStore`, a database context, a logger, or `HttpContext`. The runtime passes
`IContext` to a compiled handler; that is the deliberate seam between compiled page code and the
host, and `SimpleContext` is its only implementation.

A new view starts as a page. It becomes an endpoint only when it needs something a page may not have
— identity, cookies, the page store, framing headers, or work that outlives the request — and the
jobs that would move more of the shell are `IDEA-014` to `IDEA-019` in [jobs](jobs/README.md).

## Visibility

Nothing is `public` unless another product assembly needs it. `Sdk` exposes the page contract and
nothing else; `Core` exposes the diagnostics and the pure helpers `Runtime` and `Web` call; `Runtime`
exposes the compiler seam `Web` uses; everything else is `internal`. Every type in `Web` is
`internal`, because it is the application assembly and its consumers are its own endpoints and the
host.

Test projects are the one deliberate exception: they reach internals through `InternalsVisibleTo`
in the project file rather than by widening a type. A member that only a test uses stays `internal`
too. `Program` is `internal` for the same reason; `WebApplicationFactory<Program>` only needs the
type's assembly, and the integration tests see it through `InternalsVisibleTo`.

EF Core generated migrations come back `public`: change the new `partial class ... : Migration` to
`internal` to keep the rule. The designer and snapshot halves are already `internal` by default, and
the EF tools discover internal migrations and an internal `StorageDbContext` without trouble.

**The rule is a build failure, not a paragraph.** Each product assembly carries
`Microsoft.CodeAnalysis.PublicApiAnalyzers` and a `PublicAPI.Shipped.txt`. `Web`'s is empty, which is
the application-assembly rule in one file: a public type or member there fails the build (RS0016)
until someone declares it, and removing a declared one fails it too (RS0017). `Core` and `Runtime`
declare what they publish, and `Sdk` declares its whole contract shipped, because widening the page
contract should be a deliberate edit to a file rather than an accident. `PublicAPI.Unshipped.txt`
holds entries for API added since the last release; an entry moves to the shipped file when it lands.
The files also carry `#nullable enable` so the analyzer records annotations, and because the analyzer
parses every line as an entry they can carry no licence header: they are named in `REUSE.toml`
instead. `Web` keeps that marker in its unshipped file, which is why its shipped file is empty.

The narrowing loop is mechanical and compiler-checked: make a member `internal`, build, and the
compiler names the one caller that needs it. `InternalsVisibleTo` keeps the test projects compiling,
so only a genuine cross-assembly use fails. The public surface that remains is the smallest the
product's own assemblies and tests bind to; nothing was narrowed because it looked unused.

### Validation

An argument is validated where it enters the assembly, and trusted everywhere after that.

- **Public methods and constructors** of public types in `Core`, `Runtime` and `Sdk` validate their
  arguments: `ArgumentNullException.ThrowIfNull` for a reference, and
  `ArgumentException.ThrowIfNullOrWhiteSpace` for a string that must carry something. Values a page
  constructs, such as `NavLink` and `HttpFetchRequest`, are checked at construction for the same
  reason: a page is compiled outside the product's boundary and can pass `null` to anything.
- **The `Sdk` implementations** in `Web` — `SimpleContext` and the capability classes — validate the
  same way, because a page calls them with its own values.
- **Endpoint handlers** validate what they bind from the request. `HandleAsync(HttpContext, …)` and
  its siblings check `context` and any value they read out of it; ASP.NET is a boundary the compiler
  does not type.
- **`internal` and `private` members do not validate.** The compiler has checked the call, and a
  guard there only fires for a `null!` or a reflective caller; the members that carried one had it
  removed. A member reached by a test through `InternalsVisibleTo` is not a reason to keep one.
- **A DI-injected constructor does not validate.** The container is the only caller and does not
  pass null. Guards stay where a constructor is also reached with data the container did not supply.

A record's positional parameters are the one place the rule reads differently: the record is a value
the product passes around, its properties are set through `init`, and the member that consumes the
value is where it is checked. A record a page builds is the exception, and is checked on construction.

## Logging

Metrics answer "how much"; logs answer "what happened to this one". The two are not substitutes, and
the rule for which is which is simple: a number that is useful as a trend is an instrument, and a
fact that is only useful in the context of one request, run or import is a log line.

`Web` is the only layer that logs. `Program` configures one sink — `Microsoft.Extensions.Logging.Console`'s
JSON formatter, scopes included, one single-line object per entry — because podman hands the
container's stdout to systemd a line at a time (DEC-140). `Core`, `Runtime` and `Sdk` have no logger at
all: a compile fault and a page fault are logged where they are answered, so an exception is written
once, with its stack and its inner exception, at the boundary that handles it.

Two rules carry most of the value. Every level is about *who can fix it* — `Error` is a defect in
this repository, `Warning` is something outside it, `Information` is the rumble of things that
happened and `Debug` is noise for a developer's own machine. And every line carries a scope: the
request's id, method and path, then the signed-in account and any route value that ends in `id`, or
for background work the `RunId`, `ConversationId` or `StageId` it belongs to. A scope is filled in as
the facts become known, so the exception handler's own line about a failure names the account that
caused it.

[Logging](logging.md) has the level table, the scope keys, the exception rules and the `jq` recipes
for reading the journal.

## Shapes

- `record` (or `readonly record struct` for small values) over classes.
- `struct` for small immutable values where allocation or identity genuinely matters; measure before
  reaching for one.
- `static` classes of pure functions when there is no state to carry.
- `class` only when the framework demands it (controllers) or the type has identity.
- Prefer composition over inheritance; there is no inheritance in the core today and little need for it.

## Simplicity first

`n` is small. Prefer the obvious loop, the linear scan, and the in-memory dictionary over clever data
structures or caches. Optimise only what a measurement shows to be slow, and optimise it then.

Reach for `System.*` and `Microsoft.*` first. Write the remaining code ourselves rather than adding a
package: a dependency is a long-term liability, and most of what we need is short.

## The rest of the system

The page pipeline, the shell and the operational surfaces are their own documents, so this one can
stay a map:

- [Pages](pages.md) — the page contract, the API policy, load isolation, the execution budget and
  how a stored image is compiled, judged and run.
- [The shell](shell.md) — the document shell, layouts, error responses, and authentication.
- [Storage](storage.md) — revisions, page state, and importing and exporting an archive.
- [Metrics and the dashboard](metrics.md) — the instruments and `/status`.
- [AI page assistant](assistant.md) — the optional `/assistant` subsystem.
- [Deployment](../deploy/README.md) — quadlets, Caddy, the first release and rollback.
- [Running it](../deploy/operations.md) — limits, storage, backups, metrics and proxy trust.
