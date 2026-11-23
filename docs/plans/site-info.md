<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The site's own facts are one capability

| | |
| --- | --- |
| Status | Agreed 2026-11-23 — the owner said yes and the work below is built; it waits for the merge, which writes the decisions |
| Size | Large — one new contract type with six value records, the reference model moved into the contract, two doors made obsolete, five figures read from the objects that enforce them, the seeds, the assistant's prompt and three routes |
| Area | Sdk, Web (pages, admin, documentation, work), tools, docs |
| Spike | None. Every claim was read off trunk on 2026-11-23: `SiteOptions`, `BuildInfo`, `EditorPaths`, `LicenceLibrary`, `ChangesEndpoint`, `ReferenceLibrary`, `FetchBudget`, `ReadBudget`, `BodyBudget`, `RenderLifetime`, `JobLifetime`, `PageChain` and `PageJobRunner`; the linked corpus files in [tools/reference-corpus](../../tools/reference-corpus/reference-corpus.csproj) were read the same day |
| Backlog | [`the-site-is-one-type`](../backlog/the-site-is-one-type.md) is this plan |
| Decisions | Intends **pages-the-site-is-one-type**; retires **pages-build-info**; amends **shell-the-site-knows-its-own-origin**, **shell-the-reference** and **shell-the-licences-are-read-not-restated**. Taken at the merge (`product-plan-before-building`) |

## The short version

One capability, `ISiteInfo`, replaces the scattered doors to the site's own facts. It carries facts and
no verbs, a closed set of typed members and never a key lookup:

| Fact | Today | After |
| --- | --- | --- |
| The editor's key | `IContext.EditorPath` | `ISiteInfo.EditorPath`; `IContext.EditorPath` is obsolete for one release |
| The build's commit | `IBuildInfo.Commit` | `ISiteInfo.DeployCommit`; `IBuildInfo` is obsolete for one release |
| The public origin | `Site:Origin`, read only by `SiteOptions` | `ISiteInfo.Origin` |
| The repository URL | a constant in `ChangesEndpoint` | `ISiteInfo.RepositoryUrl` |
| The site's name and contact | constants in `OutboundUserAgent` | `ISiteInfo.Name`, `ISiteInfo.Contact` |
| `LICENSE`, the notices, `LICENSES/*`, `REUSE.toml` | `LicenceLibrary`, reached only by `/admin/copyright` | `ISiteInfo.Documents` |
| The changelist | `AdminOptions.Changelist`, reached only by `/admin/changes` | `ISiteInfo.Commits` |
| The documentation corpus | `ReferenceLibrary`, reached by `/admin/reference` and one assistant tool | `ISiteInfo.Reference` |
| The allowance a render or job has left | internal budgets | `ISiteInfo.Bounds` |

The three routes stay where they are and keep reading the same sources the capability reads, so one
fact still has one home and two callers. Turning the copyright, changelist and reference routes into
pages is `pages-page-first`'s work: the reference renderer is the large part of it, and this item adds
no fact that conversion needs.

The wrong answer is one capability per fact — a new interface, two registrations, a `/help` entry and
a policy line for every fact the site gains. The right answer is one home per fact, and the home is one
type.

## What exists

- `SiteOptions` reads `Site:Origin` and keeps `TryResolve` for the assistant, which takes a path from a
  model. Nothing else reads the origin.
- `BuildInfo.Current.Commit` is assembly metadata read from `git rev-parse HEAD`; `IBuildInfo` is the one
  capability a page reads for it, and the seeded `/about` links it.
- `EditorPaths.Of(services)` resolves `Pages:Editor:Key` per request for the shell's own links;
  `IContext.EditorPath` is a member of the contract rather than a capability, and every layer already
  carries it. `PageEditorOptions` remains the key's one home.
- `OutboundUserAgent` holds `Host` and `Contact` as constants and builds the `User-Agent` every
  outbound request carries. `ChangesEndpoint` holds `RepositoryUrl` as a constant and `CopyrightEndpoint`
  writes the same URL as prose.
- `LicenceLibrary` reads `LICENSE`, `THIRD-PARTY-NOTICES.md`, `REUSE.toml` and `LICENSES/*.txt` once,
  lazily, beside the assembly. It is registered as a singleton and injected only into the copyright
  route.
- `ChangesEndpoint` reads the file `Admin:Changelist` names, parses each line as a full hash and a
  subject (or as plain text), and writes the list. `AdminOptions` is injected only into that route.
- `ReferenceLibrary` reads `reference.{slug}.json` for the four assemblies listed by
  `ReferenceAssemblyInfo.All`, lazily and once. It is injected into the reference routes and the
  assistant's `reference` tool. The records it reads are `internal` in `Web/Documentation`, and
  `tools/reference-corpus` compiles two of those files by link.
- `FetchBudget` and `ReadBudget` expose `Capacity`, `Available`, `PerSecond` and `Key`; `BodyBudget`
  exposes `MaxChars` and `Remaining`; `RenderOptions` holds `TimeoutSeconds`, `MaxBodyChars` and
  `MaxEmbeds`; `PageJobOptions` holds `MaxDurationSeconds`. `PageChain` counts embeds in `_embeds` and
  `RenderLifetime`/`JobLifetime` own the clocks. None of these figures is visible to a page.

## The shape

### The contract

`Sdk/ISiteInfo.cs` holds the whole surface. Every member is a property: a page reads a value and does
not ask the host to look one up.

```csharp
public interface ISiteInfo {
    public Uri Origin { get; }
    public string EditorPath { get; }
    public string? DeployCommit { get; }
    public string RepositoryUrl { get; }
    public string Name { get; }
    public string Contact { get; }
    public IReadOnlyList<SiteDocument> Documents { get; }
    public IReadOnlyList<SiteCommit> Commits { get; }
    public IReadOnlyList<ReferenceCorpus> Reference { get; }
    public SiteBounds Bounds { get; }
}
```

- `Origin` is the `Uri` `Site:Origin` names. A page joins a path with `new Uri(info.Origin, "/path")`;
  there is no `Resolve` and no `//` guard, because a page is the site's own code. `SiteOptions.TryResolve`
  stays exactly as it is for the assistant, which takes a URL from a model.
- `EditorPath` is the canonical key `Pages:Editor:Key` decides.
- `DeployCommit` is the full hash of the commit this build was made from, or `null` when the build has
  none. The name says what it is: it is **not** `Commits[0]`. The changelist is the history the build
  carries, newest first, and a missing, stale or hand-edited changelist must not be able to change what
  the site says it is running; `DeployCommit` is assembly metadata read at build time. The obsolete
  `IBuildInfo.Commit` answers exactly this value.
- `RepositoryUrl`, `Name` and `Contact` are constants for now. Development runs on loopback while the
  site's identity is unchanged, as the owner chose; making them configuration is a later item if that
  stops being true.
- `Documents`, `Commits` and `Reference` are lazy and process-wide. A page that asks for none pays
  nothing; a read is done once and held for the life of the process. The host starts a background
  warm-up at startup (below), so the first page that asks normally finds the value already there; a
  page that asks while the warm-up is still running waits for that same read rather than starting a
  second.
- `Bounds` is a snapshot, read again after a fan-out. It is the one member that moves; every other
  member is immutable for the life of the process.

### The values

```csharp
public enum SiteDocumentKind { SiteLicence, ThirdPartyNotices, LicenceText, ReuseAnnotation }
public enum SiteDocumentFormat { Text, Markdown }
public sealed record SiteDocument(SiteDocumentKind Kind, string Name, SiteDocumentFormat Format, string Text);

public sealed record SiteCommit(string? Hash, string Subject);

public sealed record SiteAllowance(int Capacity, int Available, double PerSecond, string Key);
public sealed record SiteCounter(int Capacity, int Remaining, string Key);
public sealed record SiteTime(TimeSpan Total, TimeSpan Remaining, string Key);
public sealed record SiteBounds(
    SiteAllowance Fetches,
    SiteAllowance Template,
    SiteCounter? Body,
    SiteCounter? Embeds,
    SiteTime Time);
```

- A `SiteDocument` says what a file is, never what it looks like: `Kind` is the site's licence, the
  third-party notices, one of the `LICENSES` texts or `REUSE.toml`; `Name` is the file's name without
  its folder (`MIT`, `LICENSE`, `REUSE.toml`); `Format` is text or Markdown. There is no heading and no
  markup: a page decides how to present it.
- `Documents` is in a fixed order — the site's licence, the third-party notices, the licence texts by
  name, then `REUSE.toml` — so a page can draw the list without sorting it. A build that carries none
  of a file leaves it out rather than inventing an empty one; the copyright route keeps naming what is
  missing from the library's own nullable members.
- `Commits` is never `null`: a build with no changelist answers an empty list, and so does a changelist
  with no lines. The distinction between "missing" and "empty" is gone from the page contract by the
  owner's choice; the route keeps it through `ChangeListRead` (below), because the route is where a
  reader is owed the reason.
- A `SiteCommit.Hash` is a full 40-character ASCII hex hash, or `null` for a line that is not a commit.
  `make changelist` writes `git log --no-decorate --format='%H %s'`, so every line a real build carries
  has a hash; a `null` means a hand-edited or malformed changelist, and the route shows that line
  unlinked rather than dropping it. A strictly non-null type would either lose the line or need a second
  list for non-commit text, which is a worse trade for a malformed file.
- **Why `Commits` is not a value-and-reason result.** The site's value-and-reason shapes answer a
  question the caller decides on: `SiteOptions.TryResolve` has a path that did not resolve, a fetch
  has a policy refusal, `ModelWindowRead` has a model that did not answer. A page rendering the
  changelist has nothing to decide: the list is the fact, and an empty one is what a build without a
  changelist has. The reason a file is missing or unreadable is an operator's diagnostic, so it stays
  at the `ChangeList` seam, and `ISiteInfo` stays a fact sheet.
- **`ChangeListRead` is the seam's shape.** The route does need the reason, so `ChangeList` answers an
  internal named result rather than a tuple:
  `ChangeListRead(IReadOnlyList<SiteCommit> Commits, ChangeListReason Reason)`, where `Reason` is
  `Ok`, `Missing` or `Unreadable`. `SiteFacts` takes `Commits` and the page sees a plain list; the
  route uses `Reason` to say "this build carries no changelist" (`Missing`), "the changelist could
  not be read" (`Unreadable`, with the exception logged) or "0 commits, newest first" (`Ok` with no
  lines). This is the `ModelWindowRead`/`AccountBalanceRead` shape, not a raw tuple, and it is the
  only reason a page never sees.
- `SiteAllowance` is a rate and its burst: `Capacity` is the most one burst may spend, `Available` is
  what is left now, `PerSecond` is the configured rate, and `Key` names the configuration key that sets
  it. It is used for fetches (units) and for template characters, because both enforcers already answer
  the same four figures.
- `SiteCounter` is a bounded count: `Capacity` and `Remaining`. It is used for body characters and for
  embeds.
- `SiteTime` is the clock a render or a job is under: `Total` and `Remaining`, with the key that sets
  the total.
- `Body` and `Embeds` are `null` in a job: a worker has no response to buffer and cannot compose a page.
  The other three are always there.

Each figure is read from the object that would refuse the next attempt, never recomputed from the
configuration:

| Figure | Enforcer | Key |
| --- | --- | --- |
| `Fetches` | `FetchBudget` (`Capacity`, `Available`, `PerSecond`, `Key`) | `Pages:Fetch:FetchesPerSecond` in a render; `Pages:Jobs:FetchesPerSecond` in a job |
| `Template` | `ReadBudget` (`Capacity`, `Available`, `PerSecond`, `Key`) | `Pages:Read:SourceCharsPerSecond` in a render; `Pages:Jobs:SourceCharsPerSecond` in a job |
| `Body` | the layer's `BodyBudget` (`MaxChars`, `Remaining`) | `Pages:Render:MaxBodyChars` |
| `Embeds` | `PageChain._embeds` against `RenderOptions.MaxEmbeds` | `Pages:Render:MaxEmbeds` |
| `Time` | `RenderLifetime` or `JobLifetime`, measured from the same instant the deadline was armed | `Pages:Render:TimeoutSeconds` in a render; `Pages:Jobs:MaxDurationSeconds` in a job |

The key names are constants on the records that read them: `PageFetchOptions.RateKey` and
`PageReadOptions.RateKey` exist; `RenderOptions` gains `TimeoutSecondsKey`, `MaxBodyCharsKey` and
`MaxEmbedsKey`, and `PageJobOptions` gains `MaxDurationKey`. `FetchBudget.Key` and `ReadBudget.Key` are
already carried by the enforcers, so the snapshot reads them rather than restating them.

`Body` is the layer's own allowance — the object that refuses its next write — and not a sum across the
chain: the content's body, a wrapper's chrome and an embed's slice are different buffers, and the figure
that matters to the page writing is the one it is charged to. Once a response has committed, its body is
on the wire and no longer charged, so `Body.Remaining` stops moving.

### The process-wide facts and the live view

`ISiteInfo` is one interface, and its work is split in two so the process-wide read and the per-render
snapshot cannot be confused:

- **`SiteFacts`** is a `Web` singleton: the origin, the editor key, `DeployCommit`, the identity
  constants, and three readers — one each for the documents, the changelist and the corpus. A reader is
  a `Func<T>` rather than a nullable source, so the host wires `LicenceLibrary.Documents`,
  `ChangeList.Read.Commits` and `ReferenceLibrary.Corpora`, and a test wires an empty value without a
  special case. `SiteFacts` holds one `Lazy<T>` per reader, so the warm-up and every caller share one
  read, and a caller for the documents does not wait for the corpus. The three routes keep reading
  those same sources, so a fact still has one home.
- **`SiteInfo`** is a thin `ISiteInfo` built for one layer or one job from a `SiteFacts` and a
  `Func<SiteBounds>`. Every fact forwards; `Bounds` re-runs the snapshot on each read.
- **`SiteBoundsSnapshot`** is one internal `Web` type with two factories, `ForRender` and `ForJob`, so
  the mapping from the five enforcers to the five figures is written once and tested directly.
- **`SiteFactsWarmUp`** is a hosted service started with the host. It reads each of the three sources
  on the thread pool, so the listener is not held up and the three reads can overlap. An early caller
  still blocks on the source it asks for until that one read finishes; because each source is a
  process-wide lazy, the caller and the warm-up share one read rather than racing. The loaders answer
  a value rather than throwing for a missing or unreadable file — an absent or unreadable document is
  left out with a warning, an unreadable changelist is empty with a warning, a malformed corpus is left
  out with a warning — so an early `Lazy<T>` cannot cache a file failure and the warm-up cannot take
  the host down.
- **`SiteIdentity`** is one internal static class in `Web` holding `Name`, `Contact` and
  `RepositoryUrl`. `OutboundUserAgent`, `ChangesEndpoint`, `ReferenceEndpoint`, `CopyrightEndpoint` and
  `SiteFacts` all read it, so the identity has one home and its existing callers keep their behaviour.

### The doors

Two doors retire behind one-release obsolete shims, so an unedited stored page still compiles and runs:

- **`IBuildInfo`** is marked `[Obsolete]`, naming `ISiteInfo.DeployCommit` and carrying a diagnostic id
  in the same way the old response spelling does. Both `IBuildInfo` and `ISiteInfo` are registered: the
  old interface answers from `BuildInfo.Current` exactly as it does today.
- **`IContext.EditorPath`** is marked `[Obsolete]`, naming
  `ctx.Capabilities.GetRequired<ISiteInfo>().EditorPath`. `SimpleContext` still answers it. The seeds,
  `/help`, `/about` and the assistant's prompt move to the capability in this change; the member itself
  goes in the release's cleanup, beside the old storage adapters, not here.

A page whose only fault is an obsolete door compiles and renders: an obsolete warning is a warning, and
`pages-a-warning-never-stops-a-page` already says a warning never stops one.

## What changes

| Where | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/ISiteInfo.cs` | New: the interface above, every member documented |
| `src/uk.osric.asp.Sdk/SiteDocument.cs` | New: `SiteDocument`, `SiteDocumentKind`, `SiteDocumentFormat` |
| `src/uk.osric.asp.Sdk/SiteCommit.cs` | New: the parsed changelist line |
| `src/uk.osric.asp.Sdk/SiteBounds.cs` | New: `SiteBounds`, `SiteAllowance`, `SiteCounter`, `SiteTime` |
| `src/uk.osric.asp.Sdk/ReferenceModel.cs` | Moved from `Web/Documentation`; namespace becomes `uk.osric.asp.Sdk`; every record becomes public. `ReferenceException` becomes `ReferenceExceptionDoc`, because CA1711 refuses a public type whose name ends in `Exception`; the JSON shape is the property names and does not change |
| `src/uk.osric.asp.Sdk/ReferenceAssemblyInfo.cs` | Moved from `Web/Documentation` and public, so the generator can use it without a link |
| `src/uk.osric.asp.Sdk/IBuildInfo.cs` | Obsolete, pointing at `ISiteInfo.DeployCommit` |
| `src/uk.osric.asp.Sdk/IContext.cs` | `EditorPath` obsolete, pointing at the capability |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | Every new and moved public member, including the whole reference model |
| `src/uk.osric.asp.Web/SiteIdentity.cs` | New: `Name`, `Contact`, `RepositoryUrl` |
| `src/uk.osric.asp.Web/Pages/SiteFacts.cs` | New: the process-wide facts and the three readers |
| `src/uk.osric.asp.Web/Pages/SiteInfo.cs` | New: `ISiteInfo` over `SiteFacts` and a bounds snapshot |
| `src/uk.osric.asp.Web/Pages/SiteBoundsSnapshot.cs` | New: `ForRender` and `ForJob`, the one mapping from enforcers to figures |
| `src/uk.osric.asp.Web/Pages/SiteFactsWarmUp.cs` | New: the hosted service that reads the three sources on the thread pool at startup |
| `src/uk.osric.asp.Web/Pages/PageChain.cs` | Register `ISiteInfo` on every layer; capture the layer's body budget, the chain's budgets, embeds and time |
| `src/uk.osric.asp.Web/Pages/PageEnvironment.cs` | Carry `SiteFacts` |
| `src/uk.osric.asp.Web/Pages/RenderLifetime.cs` | Take the render's start and total; expose `Remaining` |
| `src/uk.osric.asp.Web/Pages/PageRenderer.cs` | Pass the timestamp `OpenSessionAsync` already records; expose `TimeRemaining` on the session |
| `src/uk.osric.asp.Web/Pages/RenderOptions.cs` | The three key constants |
| `src/uk.osric.asp.Web/Pages/PageJobOptions.cs` | `MaxDurationKey` |
| `src/uk.osric.asp.Web/Admin/LicenceLibrary.cs` | Read the files as `SiteDocument`s, in the fixed order; keep the nullable members the copyright route names; take a logger and leave an unreadable file out with a warning rather than caching a failure |
| `src/uk.osric.asp.Web/Admin/ChangeList.cs` | New: the file `Admin:Changelist` names, read lazily once and parsed by the pure function; `Read` answers `ChangeListRead` — the commits and `Ok`, `Missing` or `Unreadable`; take a logger; an unreadable file is empty with a warning |
| `src/uk.osric.asp.Web/Admin/ChangesEndpoint.cs` | Read `ChangeListRead`; `RepositoryUrl` and the nested `Commit` go; use `SiteCommit`; name a missing, unreadable or empty changelist |
| `src/uk.osric.asp.Web/Admin/CopyrightEndpoint.cs` | Use the `SiteDocument`s and `SiteIdentity.RepositoryUrl` |
| `src/uk.osric.asp.Web/Documentation/ReferenceLibrary.cs` | Read the moved model; add `Corpora`, the list `ISiteInfo.Reference` answers; its existing per-file catch gains the unreadable case |
| `src/uk.osric.asp.Web/Documentation/ReferenceEndpoint.cs` | No route change; use `SiteIdentity.RepositoryUrl` and the moved model |
| `src/uk.osric.asp.Web/Assistant/OutboundUserAgent.cs` | Read `SiteIdentity.Name` and `SiteIdentity.Contact`; the user agent on the wire is unchanged |
| `src/uk.osric.asp.Web/Pages/PageJobEnvironment.cs` | Carry `SiteFacts` |
| `src/uk.osric.asp.Web/Work/JobLifetime.cs` | Expose `Total` and `Remaining` from the ceiling it already runs |
| `src/uk.osric.asp.Web/Work/PageJobRunner.cs` | Register `ISiteInfo` with the job's budgets and lifetime |
| `src/uk.osric.asp.Web/Program.cs` | Register `ChangeList`, `SiteFacts` and the warm-up; pass `SiteFacts` into both environments |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The capability's section, the editor link, the facts and the bounds; `IBuildInfo` and `ctx.EditorPath` gone from the prose and examples |
| `src/uk.osric.asp.Web/Pages/Seeds/about` | Read `ISiteInfo.DeployCommit` and `ISiteInfo.RepositoryUrl` |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | Name `ISiteInfo` instead of `IBuildInfo` and `ctx.EditorPath`; add the facts and the bounds rule |
| `tools/reference-corpus/reference-corpus.csproj` | A `ProjectReference` to `Sdk` replaces the two linked `Compile` items |
| `tools/reference-corpus/**/*.cs` | `using uk.osric.asp.Web.Documentation` becomes `using uk.osric.asp.Sdk`; the three `Corpus` files that borrowed `Web`'s namespace move to the tool's own |
| `tools/reference-corpus/SymbolCorpus.cs` | A second copy of the target assembly — the tool's own output carries one because it references `Sdk` — gave Roslyn two references of one identity, and the target read no symbols at all. A candidate whose file name is the target's is skipped, which also fixes `core`, which had read no symbols since the tool referenced it |
| `docs/pages.md`, `docs/shell.md`, `docs/architecture.md` | Below |
| `docs/plans/README.md`, `docs/README.md`, `docs/backlog/the-site-is-one-type.md` | This plan, the item's plan and the order row |
| `docs/decisions.md` | Written by the merge, not here |

## Tests

New tests, each of which fails before the change:

- **One capability, one closed set.** `PageCapabilitiesTests` reflects over `ISiteInfo` and asserts that
  the public surface is exactly the expected properties, that every member is a property with no
  parameters, that there is no method and no event, and that no key-shaped member (`Setting`, `Get`,
  `Find`, `Resolve`, `Search`) exists. This is the guard against the registry growing a verb.
- **Every layer is offered it.** A page that renders `ISiteInfo` at the content layer, inside a layout and
  inside an embed answers the same identity, and `GetRequired<ISiteInfo>()` never throws.
- **A job is offered it.** A worker reads `ISiteInfo` and answers the job's fetch key, template key and
  duration key; `Body` and `Embeds` are `null`.
- **The identity is the site's.** `Origin` is `SiteOptions.Origin`, `EditorPath` is
  `PageEditorOptions.Key`, `DeployCommit` is the build metadata (or `null`), and `RepositoryUrl`,
  `Name` and `Contact` are the identity constants. A separate test keeps the outbound `User-Agent`
  byte-for-byte unchanged, and another proves `DeployCommit` does not move when the changelist is
  missing, empty or names a different newest commit.
- **The documents are the files the image carries.** A temporary folder with `LICENSE`,
  `THIRD-PARTY-NOTICES.md`, two `LICENSES` texts and `REUSE.toml` yields five `SiteDocument`s with the
  expected kinds, names and formats, in the fixed order; a missing file is absent and the others still
  answer. `CopyrightEndpointTests` keeps proving the route renders the same files.
- **The changelist is one read and two callers.** A full hash and subject become `SiteCommit(hash,
  subject)`; a line that is not a commit becomes `SiteCommit(null, line)`; a present file answers
  `ChangeListReason.Ok` (empty when it has no lines), a missing file answers `Missing` with an empty
  list, and an unreadable file answers `Unreadable` with an empty list and one warning.
  `/admin/changes` says the three different things, and `ISiteInfo.Commits` answers the same list for
  all three.
- **The corpus is the process's corpus.** `ISiteInfo.Reference` carries the same `ReferenceCorpus`
  values as `ReferenceLibrary.Assemblies`, including the contract's `sdk` corpus, and a stored page can
  render a type's `Display` and `Summary.Text` from it without the shell. The generator's existing
  round-trip tests prove the JSON shape did not change when the records moved.
- **The warm-up and the first caller share one read.** `SiteFacts` is built over three counting readers;
  `WarmAsync` starts them and a concurrent first ask for one value waits on the same `Lazy<T>` rather
  than starting a second, so each counter is one. A caller that asks for the documents does not wait
  for the corpus, because the three reads are separate.
- **The bounds agree with the refusals.** A page that reads `Bounds`, spends a fetch and reads again
  sees `Fetches.Available` drop by one; an embed moves `Embeds.Remaining`; a write moves
  `Body.Remaining`; `Template` answers the read budget's figures; and each `Key` is the key the
  refusing object carries. The test that a budget refusal leaves `Available` at zero sits beside it.
- **The snapshot moves.** Two reads with work between them answer different figures, and a read after
  the deadline has fired answers `Time.Remaining` of zero.
- **The old doors still work and say what replaced them.** `PageCompilerTests` keeps an
  `IBuildInfo` page and a `ctx.EditorPath` page compiling and running, and asserts each reports one
  warning whose text names `ISiteInfo`; the existing "says capabilities without warnings" test moves to
  `ISiteInfo`.
- **The webfinger key is the pages to serve.** `/.well-known/webfinger` normalises to itself, is
  addressable, is not in `ReservedPaths.Default`, and no `ShellCatalogue` route or menu claims it. No
  code is needed: the proof is that an ActivityPub node can point a peer at a page key today.

Existing tests that move with the code:

- `PageHarness`, `PageEndpointTests`, `ShellViews` and the chain/composition tests gain the `SiteFacts`
  argument both environments now require; the harness exposes one built with empty document and
  changelist sources so a fixture that is not about them pays nothing.
- `RenderLifetimeTests` passes the render's total and start; a new case proves `Remaining` falls and
  clamps at zero.
- `PageJobTests`/`PageJobRenderTests` pass the job environment's new member.
- `PageAssetSeederTests`, `PageAuthorPromptTests` and `SiteChromeTests` assert `ISiteInfo` instead of
  `IBuildInfo`; the about page still links the commit the build records.
- `ChangesEndpointTests` constructs a `ChangeList` and keeps every parsing case; `CopyrightEndpointTests`
  keeps every rendering case. Both libraries gain a logger argument, which the tests pass as a
  `NullLogger` except where the unreadable-file warning is the behaviour under test. A `ChangeListTests`
  case makes an unreadable file by pointing the setting at a directory, which is the one way to get
  the same failure on every platform the suite runs on.
- `ShippedConfigurationTests` does not change: no new configuration key is read. `Site:Origin` and
  `Pages:Editor:Key` keep their existing readers.

## Docs

- `/help` gains a **The site's own facts** section: `ISiteInfo` is always offered, the identity values,
  the documents and their formats, the changelist (empty when there is none), the corpus, and `Bounds`
  as a snapshot with its five figures and their keys. Every `ctx.EditorPath` example becomes the
  capability, and the `IBuildInfo` example becomes `site.DeployCommit` plus `site.RepositoryUrl`.
- [pages](../pages.md#page-contract) counts `ISiteInfo` among the capabilities and describes its
  members, including that the three lists are read once and warmed at startup; the `IContext` member
  list marks `EditorPath` obsolete and names its replacement.
- [the shell](../shell.md#the-copyright-page), [the reference](../shell.md#the-reference) and [a run's
  view](../shell.md#a-runs-view) say that the files and the corpus are page values now and the routes
  keep reading the same sources.
- [architecture](../architecture.md) adds `ISiteInfo` and the site fact records to the page contract's
  row and says the corpus model lives in `Sdk` while the reader and routes stay in `Web`.
- The merge writes **pages-the-site-is-one-type**, moves **pages-build-info** to the Retired list with
  what replaced it, and amends the three shell entries. `make decisions` proves the names.

## Risks

- **The corpus is expensive to parse.** It is warmed on the thread pool at startup, so the common case
  is that the first page finds it ready. The cost is a startup CPU spike while the site is already
  listening, and a page that asks during the warm-up still waits for the corpus read. The warm-up is
  three separate reads, so a page that asks for the documents is not held behind the corpus; a
  deployment that finds the spike too sharp can later move the read behind a readiness gate, which this
  plan does not add.
- **The warm-up changes when the work happens, not what it is.** A missing file is still an empty list
  and a malformed corpus is still left out with a warning, so the warm-up cannot fail the host and a
  caller's answer is the same one it would have got from a cold read.
- **The page contract grows by the whole reference model.** Moving the records into `Sdk` makes a dozen
  data records public and adds them to `PublicAPI.Unshipped.txt`. They add no way to reach a resource,
  so the API policy admits them by the rule it already has, but the surface is wider and the next reader
  should know it was a deliberate trade for one value and two callers.
- **A snapshot that lies.** A figure computed anywhere but the enforcing object can disagree with the
  refusal. `SiteBoundsSnapshot` reads `Capacity`/`Available`/`Remaining` from the budgets, embeds from
  `PageChain`, and time from the lifetime that arms the deadline; the tests above pin the agreement.
- **A body figure that is the layer's, not the request's.** A wrapper's `Body` is its own chrome budget,
  the content's is its own buffer and an embed's is its slice. That is what the page writing is charged
  to, and the docs say so rather than implying a single number for the tree.
- **Synchronous reads in a page render.** The contract's members are properties, so a read is
  synchronous, as `LicenceLibrary` and `ReferenceLibrary` already are. The warm-up makes the common case
  a hit; an early caller blocks on the `Lazy<T>`, which is the same behaviour a cold read has today.
  The alternative, a `Task` property, would make every fact an await and put the read on the page's own
  code.
- **Two obsolete warnings on an unedited page.** A page that uses both old doors reports two warnings
  and still renders. Nothing in the host treats a page warning as an error; the compiler tests prove
  it.
- **Registrations in two places.** Every layer and every job register `ISiteInfo`. A future capability
  with the same rule has the same cost; the tests that render a page and run a worker fail if either
  registration is missed, which is the guard the old `IBuildInfo` registration had.
- **The tool's file links.** The generator now builds against `Sdk`; the JSON it writes must stay
  byte-compatible. The reference round-trip tests and the existing corpus are the proof, and the tool's
  own namespace change touches no behaviour.

## Not in scope

- **Turning the three routes into pages.** The capability makes it possible; the conversion is a
  `pages-page-first` step and the reference renderer is its large part. Each route keeps its markup.
- **The origin for the assistant.** `SiteOptions` already answers it; this item only exposes the same
  value to a page.
- **A general settings reader.** Credentials, connection strings, the provider key, `AllowedHosts`, the
  page API list and operator data stay out. The test is whether a page may know a fact, not whether it
  is configured.
- **Configuring the identity.** `Name`, `Contact` and `RepositoryUrl` stay constants because the site's
  identity has not changed; `Origin` and `EditorPath` remain the configured facts, through their
  existing readers.
- **What a page may do with a document.** A licence text is prose; publishing it, caching it and linking
  it are the page's business, and the copyright route stays the shell's answer until `pages-page-first`.
- **Removing the two old doors.** This plan marks them obsolete and moves every seed, the manual and the
  prompt; the member and the interface go in the release's cleanup.

## Open questions

None. On 2026-11-22 the owner chose one type rather than a capability per fact, the corpus in the same
step, the identity constants exposed but not yet configured, and the `EditorPath` door retired with
every seed and the manual updated. On 2026-11-23 they settled the three shapes that follow from that:
the build's commit is named `DeployCommit` so it cannot be mistaken for the changelist's newest entry;
the changelist is an empty list rather than `null` in the page contract, with the missing, unreadable
and empty cases kept for the route as `ChangeListRead`; and the documents, changelist and corpus are
warmed on the thread pool at startup, with an early caller waiting on the same read. The choices left here are the exact members, and the plan states them so they
are reviewed rather than asked again.
