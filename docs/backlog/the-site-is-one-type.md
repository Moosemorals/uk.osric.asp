<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The site's own facts are a capability each, and two of them are not reachable at all

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [site-info](../plans/site-info.md) |
| Area | Sdk, Web (pages, admin, documentation, work), tools, docs |
| Location | `src/uk.osric.asp.Sdk/ISiteInfo.cs` (new), `src/uk.osric.asp.Sdk/ReferenceModel.cs` (moved), `src/uk.osric.asp.Web/SiteOptions.cs`, `Pages/EditorPaths.cs`, `Pages/BuildInfo.cs`, `Admin/LicenceLibrary.cs`, `Admin/ChangesEndpoint.cs`, `Documentation/ReferenceLibrary.cs`, `Pages/PageChain.cs`, `Work/PageJobRunner.cs`, `Pages/Seeds/help`, `docs/pages.md` |
| Spike | — |

## Idea

A page that wants to draw one of the site's own views cannot see what the site knows about itself. The
facts are scattered, and each one has grown its own door:

| Fact | Where a page reaches it today |
| --- | --- |
| The editor's key | `IContext.EditorPath`, a member every page meets |
| The build's commit | `IBuildInfo`, a capability with one member |
| The public origin | nowhere — `Site:Origin` is read by `SiteOptions` for the assistant and the deployment |
| The repository URL | nowhere — a constant inside `ChangesEndpoint` |
| The site's name and contact | nowhere — constants inside `OutboundUserAgent` |
| `LICENSE`, the notices, `LICENSES/*`, `REUSE.toml` | nowhere — `LicenceLibrary`, reached only by `/admin/copyright` |
| The changelist | nowhere — `AdminOptions.Changelist`, reached only by `/admin/changes` |
| The documentation corpus | nowhere — `ReferenceLibrary`, reached only by `/admin/reference` and one assistant tool |
| The allowance a render or a job has left | nowhere — the budgets are internal |

The last four are what keeps `/admin/copyright`, `/admin/changes` and `/admin/reference` in the shell
against `pages-page-first`: a page cannot read a file beside the assembly. But the *read* is not the
interesting part — what a page needs is the value, and the value is already normalised in the host.
The corpus, for instance, is a model shared with the generator by a file link in
[tools/reference-corpus](../../tools/reference-corpus/reference-corpus.csproj), which is a shape
`Sdk` can hold properly.

The wrong answer is one capability per fact: the registry grows with the site's vocabulary, and every
new fact is a new interface, a new registration in two places, a new `/help` entry and a new line in
the policy. The policy — one *home* per fact — is right; the home should be one type.

## Sketch

One capability, `ISiteInfo`, registered with every layer and with a job, carrying facts and no verbs:

- **Identity.** `Origin` (a `Uri`), `EditorPath`, `DeployCommit` (null when the build has none),
  `RepositoryUrl`, and the site's `Name` and `Contact`. The commit is the one this build was made from,
  never the changelist's newest entry. The name and contact stay constants for now, because development
  runs on loopback while the identity stays the same.
- **Documents.** `IReadOnlyList<SiteDocument>` for the files the image carries: a
  `SiteDocument(Kind, Name, Format, Text)` where `Kind` says the site's licence, the third-party
  notices, a licence text or `REUSE.toml`, and `Format` says text or Markdown. No heading and no
  markup — the page decides how to present it.
- **The changelist.** `Commits`, the parsed hashes and subjects, empty when the build carries no file
  or an empty one. The parse is a pure function, and the page links a hash with `RepositoryUrl`.
- **The corpus.** `Reference`, the documentation the build carries, as the model
  `ReferenceLibrary` already reads. The records move from `Web/Documentation` to `Sdk`, the
  generator references `Sdk` instead of linking two files out of `Web`, and a page can render a
  reference page without the shell.
- **The bounds.** `Bounds`, a snapshot of what this render or job has left: fetches (capacity,
  available, per second), body and template characters, embeds, and time. Each figure is read from
  the object that enforces it, so it cannot disagree with a refusal, and each carries the
  configuration key that sets it.

The rules that keep it a fact sheet:

1. **Facts, not verbs.** A page joins a URL with `new Uri(origin, path)` and scans the list it was
   given; there is no `Resolve`, `FindType` or `Search`. The `//` guard in `SiteOptions.TryResolve`
   exists for the assistant, which takes URLs from a model.
2. **A closed set of typed members, never a key lookup.** There is no `Setting(name)`. A page cannot
   probe `Pages:Fetch:Credentials:*`, `ConnectionStrings:Storage` or the provider key.
3. **No authority.** Credentials, connection strings, the provider key, `AllowedHosts`, the page API
   list and operator data stay out. The test is whether a page may know it, not whether it is
   configured.
4. **Live where it ticks.** `Bounds` is a snapshot; read it again after a fan-out and it has moved.
5. **Lazy and process-wide.** The documents, the changelist and the corpus are read once and held; a
   page that asks for none pays nothing, and a background warm-up at startup means the first page
   normally finds them. A page that asks while the warm-up runs waits for the same read.
6. **No presentation.** The type says what a document is, never what it looks like.

Two doors retire behind one-release obsolete shims, so an unedited stored page still compiles:

- **`IBuildInfo`** — its `Commit` is `ISiteInfo.DeployCommit`. `pages-build-info` is folded into the new
  entry and its name retired.
- **`IContext.EditorPath`** — it becomes a documented forwarding door for one release, then goes; the
  seeds, `/help` and the assistant's prompt use the capability. A page offering "edit this" says
  `ctx.Capabilities.GetRequired<ISiteInfo>().EditorPath`.

The three endpoints stay where they are: each reads the same library the capability reads, so one
fact still has one home and two callers. Converting them to pages is `pages-page-first`'s work and
needs no fact this item adds — they already draw their menu from `ShellLinks`, which
`pages-the-catalog-knows-the-shell` gives a page.

The owner's webfinger question is answered by a test rather than code: `/.well-known/webfinger` is
already an addressable key, no route claims it, and nothing writes it into `ReservedPaths`. The proof
belongs with this item's origin, so an ActivityPub node can point a peer at it.

## Decisions

The merge intends **pages-the-site-is-one-type**, retires `pages-build-info`, and amends
`shell-the-site-knows-its-own-origin` (the origin is now the page's too),
`shell-the-reference` (the corpus is a page value) and
`shell-the-licences-are-read-not-restated` (the files are a page value, and the route still reads
them).

## Not in scope

- **Turning the endpoints into pages.** The capability makes it possible; the conversion is a
  `pages-page-first` step, and the reference renderer is the large part of it.
- **The origin for the assistant.** `SiteOptions` already answers it; this item only exposes the same
  value to a page.
- **A general settings reader.** Operator data and credential-shaped names are not facts.
- **What a page may *do* with a document.** A licence text is prose; publishing it, caching it and
  linking it are the page's business.

## Open questions

None. On 2026-11-22 the owner chose one type rather than a capability per fact, the corpus in the same
step, the identity constants exposed but not yet configured, and the `EditorPath` door retired with
every seed and the manual updated. On 2026-11-23 they named the build's commit `DeployCommit` so it
cannot be confused with the changelist, preferred an empty list to a nullable one, and asked for the
documents, changelist and corpus to be warmed on a background thread at startup.

**Origin:** the owner's ask 5 of 2026-11-22, and their correction that the site's facts belong in one
type, with the copyright page, the changelist and the reference as the cases that made the gap
visible.
