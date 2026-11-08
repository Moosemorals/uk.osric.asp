<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Content types from page extensions

Status: **implemented 2026-11-08**. Written 2026-11-07; revised 2026-11-08 so that the map and
the fallback are configuration rather than a table in code, and so that caching is one default with
a per-page opt-out. This is now the record of that work: the decision is DEC-144 and the behaviour is
recorded in [architecture](../../architecture.md#page-contract). It changed the page contract, so
every stored image is recompiled on its next request (DEC-26); it did not change the page SDK, the
template language, the compiler or the page store schema. One thing here was not built: nothing
consumes the source-language kind decision 4 proposed, so `ForPath` returns the media type string and
the kind arrives with the validator that needs it (IDEA-021).

## The short version

A stored page's default response content type is chosen from the last segment of its key. Which
extension means which media type, and what a key with no mapped extension gets, are configuration:
`Pages:ContentTypes` in `appsettings.json`, read once at startup, so the map is data and a host can
replace it without rebuilding the image.

The map as it ships, which is the whole of the default behaviour:

| Extension | Media type | Kind |
| --- | --- | --- |
| `.js`, `.mjs` | `text/javascript; charset=utf-8` | JavaScript |
| `.css` | `text/css; charset=utf-8` | Css |
| `.json` | `application/json; charset=utf-8` | Json |
| `.txt` | `text/plain; charset=utf-8` | Text |
| anything else | `Pages:ContentTypes:Fallback`, `text/html; charset=utf-8` | from its media type |

The kind is derived from the media type, so the configuration holds one string per extension and a
typo cannot pair a JavaScript kind with an HTML media type. A page can still call
`ctx.SetContentType` to override the default, including back to HTML.

Caching is deliberately not inferred from the type at all: every rendered page response defaults to
`Cache-Control: no-cache`, and a page opts out by setting its own `Cache-Control`. That is what the
seeded scripts and stylesheets already ask for, so they stop asking.

The extension is also a **source-language signal**: it says the author intends the literal text to
be JavaScript, CSS or JSON. It is not a promise that the text is valid, and it does not validate
anything. [IDEA-021](../jobs/IDEA-021.md) is the separate idea for using the signal in the
assistant's validator.

## Goals and non-goals

Goals:

- A page's default content type follows a small, explicit, auditable mapping from its key.
- The map and the fallback are configuration: changing the list of extensions is a settings edit,
  not a build, and adding a type to the site never touches the repository.
- `ctx.SetContentType` remains the one explicit override; precedence is unchanged.
- Caching has one default for every page and one opt-out, rather than a rule inferred from the type.
- The seeded scripts and stylesheets keep their current content type and cache behaviour.
- A pure JavaScript page needs no template directive at all, so its source is valid JavaScript as
  stored.
- The mapping is available to the validator as a source-language kind, without the validator being
  part of the response path.

Non-goals:

- Binary assets in the page store. Pages are text templates; a `.png` key is still a text page and
  is not in the map.
- MIME sniffing, `Content-Disposition`, content negotiation on `Accept`, or a second static-file
  pipeline.
- Inferring `Content-Security-Policy` or `AllowScripts` from the extension.
- Validating the page source. That is [IDEA-021](../jobs/IDEA-021.md).
- A general MIME database: the section is a short, hand-maintained map for a text page store, not a
  copy of `/etc/mime.types` and not `FileExtensionContentTypeProvider` in another shape.
- A cache policy per type or per row. Caching is one default and one page-level opt-out
  (decision 6).
- A configuration file of our own. The map is ordinary configuration read through
  `IConfiguration`, so it lives where the other settings live (decision 2).
- Letting a page, the editor or the assistant change the map. It is host configuration.
- Changing `PageResponse.Default`, the SDK, the compiler, the page store or the editor's save
  contract.

## What already exists

- `SimpleContext` starts with `PageResponse.Default.ContentType`, currently
  `text/html; charset=utf-8`, and `SetContentType` overwrites it. `PageEndpoint` writes the final
  value to the response.
- The seeds in `src/uk.osric.asp.Web/Pages/Seeds` set their own content type and
  `Cache-Control: no-cache` in a template block at the top of the file. Each block is already inside
  a native comment (`//`, `/* */` or `<!-- -->`), so the stored source is valid in its own language.
- `SecurityHeaders` sets `X-Content-Type-Options: nosniff` on every response and owns the CSP
  header. A page can widen scripts only through `ctx.AllowScripts()`.
- `PageEndpoint` computes an ETag from the rendered body and honours `If-None-Match` for GET and
  HEAD, so `no-cache` means revalidate, not necessarily re-render without a validator. An
  authenticated response is written `Cache-Control: no-store` with the ETag removed, after the
  page's own headers, and a redirect is `no-store` whoever asked.
- `PagePath` canonicalises keys to lowercase and rejects `?` and `#`. A key has no separate
  extension property today.
- Settings come from `appsettings.json` plus the environment, and `RenderOptions`, `CompileOptions`,
  `PageStorageOptions`, `PageFetchOptions` and `ArchiveOptions` each have a `FromConfiguration`
  factory whose result is registered as a singleton. There is no `IOptionsMonitor` anywhere in
  `src/`: nothing rereads a setting while the process runs.
- The framework already layers `appsettings.{ASPNETCORE_ENVIRONMENT}.json` over `appsettings.json`,
  both optional and both watched for changes, and the quadlet sets `ASPNETCORE_ENVIRONMENT` to
  `Production`. The repository ships only `appsettings.Development.json`, so a Production file is
  absent from the image today.
- The deployment already has a host-owned place for settings that must not be in the image: the
  quadlet reads `~/.config/uk.osric.asp/assistant.env` as an environment file, and `make deploy`
  seeds it once with `test -e … || install`, because podman refuses a missing `--env-file`. The same
  directory and the same seed-once step are what a mounted settings override needs.
- `PageEnvironment` is the record of singletons the page endpoint is handed, so a new singleton the
  endpoint needs belongs there.

## Decisions

### 1. The extension selects a default; `SetContentType` still wins

The mapping is a default, not a constraint. A `.js` page may still call
`ctx.SetContentType("text/html; charset=utf-8")` if that is what it wants to send, and an HTML page
may call it to serve something else. The extension decides the initial value; the page decides the
final one. This is the same precedence as today, with a different initial value. Configuration is a
source of defaults, not a policy: nothing in the file can stop a page choosing its own type.

The source language and the response type are separate facts. A `.js` page that overrides its
response type is still JavaScript source for validation; a page at `/notes` that sets
`text/javascript` is still HTML source by convention. [IDEA-021](../jobs/IDEA-021.md) uses the key,
not the final header.

### 2. The map is configuration in `appsettings.json`

The extension list is `Pages:ContentTypes:Extensions`, the fallback is `Pages:ContentTypes:Fallback`,
and both live in `appsettings.json` with the rest of the site's settings:

```json
{
  "Pages": {
    "ContentTypes": {
      "Fallback": "text/html; charset=utf-8",
      "Extensions": {
        ".js": "text/javascript; charset=utf-8",
        ".mjs": "text/javascript; charset=utf-8",
        ".css": "text/css; charset=utf-8",
        ".json": "application/json; charset=utf-8",
        ".txt": "text/plain; charset=utf-8"
      }
    }
  }
}
```

A file of our own was the first sketch and is dropped. The section is read through
`IConfiguration` either way, so a `content-types.json` beside `appsettings.json` would buy a second
path, a second layer and a second schema for nothing a section does not already give.

What it would have been buying is the host's ability to change the list without rebuilding the
image, and the framework already has a layer for exactly that: `appsettings.Production.json`, added
over `appsettings.json` by `CreateBuilder` because the quadlet sets `ASPNETCORE_ENVIRONMENT` to
`Production`. The deployment mounts the deploy user's own copy read-only over the one the image does
not have:

```
Volume=%h/.config/uk.osric.asp/appsettings.Production.json:/app/appsettings.Production.json:ro
```

A file mount fails differently from a directory mount when the host path is missing: podman creates
a directory at the target and the app cannot read a file there, so `make deploy` seeds a file
containing `{}` once, with the same `test -e … ||` guard that already protects `assistant.env`.
Editing the repository's `appsettings.json` stays the way to change the default for everyone; the
host's settings file is for the one host that wants a different map, and a restart applies it
(decision 4).

Configuration providers merge key by key, so the host's settings file lists only what it changes: a
new extension, a replacement for a row it disagrees with, or the fallback (decision 3). There is no
"remove" syntax and none is needed: mapping a row to the fallback media type is the same thing as
having no row, because caching no longer reads the map (decision 6) and the kind falls back with the
type.

`ASP.NET`'s `FileExtensionContentTypeProvider` is still deliberately not used. It maps binary
extensions that a text page store cannot actually serve, its list changes with the framework, and
using it would put the table back in code.

Rows are validated when the section is read, not per request. A key must start with `.`, must name
one extension (no second dot, slash or whitespace) and is matched case-insensitively; two rows that
differ only in case are an error rather than a coin toss. A value must look like `type/subtype` with
optional parameters. Errors name the offending key, because the section is hand-edited.

The extension is still the suffix after the last `.` in the final `/` segment, and only when there
is at least one character before the dot. `/foo.js` is JavaScript; `/foo` and `/foo.js/bar` take the
fallback; `/.js` has no stem and takes the fallback. Keys are lowercased by `PagePath`, but the
lookup is case-insensitive so a caller holding raw request text gets the same answer. `.html` and
`.htm` need no row: they fall through to the fallback.

`.svg` is still deliberately absent from the default map. It is active content, and `/rocket.svg`
already sets `image/svg+xml` explicitly. An operator who wants SVG served by extension can add the
row in their own settings file without touching the repository, which is exactly the freedom this
change buys; decision 7 is what keeps that safe.

### 3. The fallback is a config key

`Pages:ContentTypes:Fallback` is the media type for a key whose extension has no row. The default
lives in code and is taken from `Sdk.PageResponse.Default.ContentType`, so the SDK's document default
and the site's fallback cannot drift apart; `appsettings.json` states the same string as data, which
is what an operator reads and edits.

It is a key rather than a constant because it is one row of the same table. Leaving it in code would
split one rule across two places, and a host that is not an HTML site cannot change a constant
without rebuilding the image. The fallback's media type also decides its kind, so a host that sets
`text/plain` gets plain-text source for an unmapped page rather than HTML.

A missing key takes the default. A key that is present but blank or malformed is an error at
startup, like a malformed row: "unset" and "wrong" are different mistakes and the second one must
not be silent.

### 4. Read the map once at startup; a restart applies a change

The rule stays pure and stays in `Core`. `PageContentTypes`, beside `PagePath`, is an immutable
value built from a fallback string and a set of extension rows; `ForPath(string path)` returns a
`PageContentType` of media type and kind. `Create(...)` validates and throws; `ForPath` never
throws. The kind is derived from the media type — `text/html` is Html, `text/javascript` and the
`application/javascript` spelling are JavaScript, `text/css` is Css, `application/json` and any
`+json` subtype are Json, `text/plain` is Text, everything else is Other — so the configuration
holds one string per extension and cannot pair a JavaScript kind with an HTML type.

`Web` owns loading, and it does it the way every other page option is loaded:
`PageContentTypesConfiguration.FromConfiguration(configuration)` reads the section through
`GetSection("Pages:ContentTypes:Extensions").GetChildren()`, hands the pairs to `Create`, and its
result is registered as a singleton beside `RenderOptions` and `PageStorageOptions`. There is no
catalog, no watcher and no last-known-good copy, because nothing in `src/` rereads configuration
while the process runs and this feature does not need to be the first.

A map that is invalid is fatal at startup: the site does not come up with half a map, the journal
names the key, and the health check never goes green. That is the trade `AssistantOptions` already
makes.

A host therefore changes the extension list by editing its own `appsettings.Production.json` and
restarting the service — a second of downtime, and not a redeploy, which is what the requirement was
about. Live reload through a configuration change token was considered and left out: it needs a
watcher, a rebuild that cannot throw into a request, and a rule for when to keep the previous map,
which is machinery for a file that is edited once in a while. If it is ever wanted, it is a later
change with its own tests.

`PageEnvironment` gains the value, and `PageEndpoint` hands it to `SimpleContext`, so one render sees
one map from its first line to its last.

### 5. Apply the default in `SimpleContext`, not in `PageEndpoint`

`SimpleContext` is where the page's response is assembled, and it already owns `_contentType`. Take
the request's `PageContentTypes` as a constructor argument, set `_contentType` from
`ForPath(request.Path).MediaType`, and let `SetContentType` overwrite it. `PageResponse.Default`
stays HTML as the SDK-level default; only stored pages get the extension-based initial value.

The context takes the map as a plain value, not as configuration: a test can build a two-row map
with no settings at all, and one render cannot see two versions of it. No SDK change is needed:
`PageResponse.ContentType` already carries the final value, and `PageEndpoint` already writes it.

### 6. Every rendered page defaults to `Cache-Control: no-cache`, and a page may opt out

`SimpleContext.ToResponse` adds `Cache-Control: no-cache` when the page has not set a `Cache-Control`
header of its own. A page opts out with `ctx.SetHeader("Cache-Control", "max-age=3600")`, which
already works because `Cache-Control` is on the page header allow-list (DEC-32) and the page's value
is what gets written.

This replaces the narrower rule the first draft had, where a mapped type other than the fallback got
`no-cache` and a fallback page got nothing. One rule is simpler to state, simpler to test, and
matches how the site is used: a stored page is something its author changed a moment ago, so a
heuristically cached copy is the wrong default for all of it, not just for scripts. It also drops
the map out of the cache decision, which is what makes mapping a row to the fallback a complete
answer to "remove this row" (decision 2).

`no-cache` does not prevent storage; it requires revalidation, and the existing ETag/304 path makes
that cheap for an anonymous visitor. Authenticated responses keep the final word: `PageEndpoint`
applies `no-store` and removes the ETag after the page's headers, so opting in to caching cannot
make a signed-in render a shared cache's answer. A redirect is already `no-store` whoever asked.

A page that opts out takes responsibility for the body being safe to share: it is the author's
statement that this response is the same for every visitor. [IDEA-010](../jobs/IDEA-010.md)
immutable assets is where a long lifetime gets designed; this decision only makes room for it.

The rule is applied where a stored page's response is assembled, so the shell's own documents —
`/status`, `/edit`, the error page — keep whatever they set for themselves. This is about pages, not
about every byte the host writes.

### 7. Do not infer CSP or `AllowScripts`

The content type describes the resource; `AllowScripts` describes the document that loads it. A
`.js` response must not widen its own `Content-Security-Policy`, and an HTML page that includes a
`<script type="module">` must keep calling `ctx.AllowScripts()` explicitly. `SecurityHeaders` and
`IContext.AllowScripts` are unchanged. Adding a row to the map — `.wasm`, `.svg`, anything — widens
nothing: it changes one response header's default and no policy.

### 8. No content negotiation on `Accept`

The key is the contract. A request for `/foo.js` gets JavaScript even if it sends
`Accept: text/html`; a page that wants to vary by `Accept` can inspect the request itself. This
keeps the response cache and the ETag story simple and matches the site's existing "a key names one
page" model.

### 9. Update the seeds and the author prompt together

Every seed opens with a template block that sets its content type and `Cache-Control: no-cache`.
After this change the host gives both, so the block goes: the scripts and stylesheets begin with
their SPDX comments and nothing else, which is also what makes the stored source directly parseable
as JavaScript or CSS. `/rocket.svg` keeps its `SetContentType("image/svg+xml")` because `.svg` is not
in the map, and loses only the cache line.

Update the `## Scripts` section of `PageAuthorPrompt` to say that a page's response type follows its
key's extension, that the site's configuration currently maps `.js` and `.mjs` to JavaScript, that
`SetContentType` is only for an override, that every rendered page is `no-cache` unless it sets its
own `Cache-Control`, and that the source should be a native ES module. The prompt must not promise a
fixed list of extensions, because the list is the host's. Bump `PageAuthorPrompt.Version` and cover
the change in `PageAuthorPromptTests`.

The seeds' first line is currently doing double duty as a language comment and a host directive.
After this change the script and stylesheet sources are cleaner: `/assistant.js` can begin with its
SPDX comments, and `/site.css` with its own. That is also what makes the source directly parseable
by the validator in the common case.

### 10. Record the decision when it is implemented

This plan proposes the rule; [docs/decisions.md](../decisions.md) should gain the corresponding
entry when the code lands. Take the next free id, under "Page contract and threat model". It has to
name the two keys, the fact that the map is ordinary configuration rather than a file of its own,
the startup-fatal validation, the `no-cache` default with its per-page opt-out, the `.svg` exclusion,
and the statement that the extension is a default rather than a constraint.

## Implementation sequence

### Phase 0 — the pure value

1. Add `src/uk.osric.asp.Core/Pages/PageContentTypes.cs`: `PageContentKind` (Html, JavaScript, Css,
   Json, Text, Other), `PageContentType` (`MediaType`, `Kind`), and `PageContentTypes` with
   `Create(string fallback, IEnumerable<KeyValuePair<string, string?>> extensions)` and
   `ForPath(string path)`.
2. Add `test/uk.osric.asp.Core.Tests/Pages/PageContentTypesTests.cs` for the lookup: every shipped
   extension, unknown extensions, no extension, a dot in a directory name, `/.js`, uppercase input,
   `/foo.js/bar` (the fallback, because the last segment has no extension), and parameters in a
   media type (`text/css; charset=utf-8` is Css).
3. Cover `Create`: a valid map, `null` and empty rows skipped, a key without a leading dot, a key
   with a second dot, a duplicate that differs only in case, a media type with no `/`, a blank
   fallback, and kind derivation for each known media type plus `Other`.
4. Pin the exact strings (`text/javascript; charset=utf-8`, not `application/javascript`) in the
   tests.

### Phase 1 — the configuration

1. Add the `Pages:ContentTypes` section to `appsettings.json`: `Fallback`, and the five `Extensions`
   rows. Nothing new on disk, so no `Content` item and no `REUSE.toml` entry.
2. Add `PageContentTypesConfiguration` under `Web/Pages` with
   `FromConfiguration(IConfiguration)`, reading the rows with `GetChildren()` and returning the
   `Core` value; register the result as a singleton in `Program.cs` beside `RenderOptions`, and add
   it to `PageEnvironment`. It logs the fallback and the row count at `Information` once, so the
   journal says what the site is serving.
3. Tests for the reader: the shipped section parses and maps the five extensions; a missing
   `Fallback` takes `PageResponse.Default.ContentType`; an invalid row throws naming its key; two
   rows differing only in case throw; a row mapped to the fallback behaves like no row.
4. A test that the repository's `appsettings.json` still holds the five rows, so the section cannot
   be edited into something the tests do not cover without a failing build.

### Phase 2 — apply the default and the cache default

1. Give `SimpleContext` the request's `PageContentTypes` and initialise `_contentType` from
   `request.Path`; `PageEndpoint` passes `environment.ContentTypes`.
2. Add `Cache-Control: no-cache` in `ToResponse` for every page that has not set the header, with
   `TryAdd` so a page's own value wins. Keep the existing `_headers` dictionary case-insensitive.
3. Add tests to `SimpleContextTests` for:
   - `/app.js` defaults to JavaScript and `/notes` defaults to the fallback;
   - `SetContentType` overrides the inferred type, in both directions;
   - every page gets `no-cache`, whatever its type;
   - a page-set `Cache-Control` wins;
   - a map built with a different fallback behaves the same way.
4. Add `PageEndpointTests` or integration tests for a stored `/x.js` page with no directive, a
   stored `/x.css` page that overrides to HTML, HEAD, and a matching `If-None-Match` 304.
5. Confirm `SecurityHeaderTests.AnAssetPageForbidsScriptsAndKeepsItsContentType` still passes for
   the seeded stylesheet, and add the same assertion for a seeded script if one is served directly
   in a test.

### Phase 3 — seeds, prompt, docs and deployment

1. Remove the template block at the top of each seed: the type now comes from the map and the cache
   header from decision 6. The scripts and stylesheets keep only their SPDX comments; `/rocket.svg`
   keeps `SetContentType("image/svg+xml")` because `.svg` is not mapped, and loses the cache line.
2. Update `PageAssetSeederTests` and any seed-content assertions.
3. Update `PageAuthorPrompt` and its tests; bump the version.
4. Update `docs/architecture.md`:
   - the paragraph that says each seed is a complete template "including its response content type
     and cache headers";
   - the page-contract paragraph that says content type goes through `SetContentType`, to mention
     the extension default and where the map comes from;
   - the ownership table with a row for the pure content-type rule.
5. Update `docs/jobs/IDEA-021.md`: the source-language signal is "the key's extension maps to the
   JavaScript kind", not a fixed `.js`/`.mjs` convention.
6. Add the decision entry to `docs/decisions.md` when the implementation is actually accepted.
7. Update the quadlet: mount the deploy user's settings override read-only over the file the image
   does not have, `Volume=%h/.config/uk.osric.asp/appsettings.Production.json:/app/appsettings.Production.json:ro`.
   The read-only mount is the point: the map is host configuration and no page, editor or assistant
   can write it.
8. Seed that file in `make deploy`, beside the `assistant.env` line and with the same `test -e … ||`
   guard, writing `{}` so podman never turns a missing path into a directory.
9. Update `deploy/README.md` with a short "Content types" subsection: the two keys, that the host's
   `appsettings.Production.json` merges over the shipped section, that a restart applies it and is
   not a redeploy, and where a rejected map appears in the journal.

### Phase 4 — gate

1. Run `make check` (format, licences, package audit, tests, Release publish).
2. Run `make test-browser` if a seeded script or stylesheet changed; the browser suite proves the
   seeded assets still load and run.
3. Before deploying, list stored keys whose final segment contains a dot but is not in the map, and
   confirm none of them relied on the fallback. `SetContentType` remains the escape hatch.
4. After deploying, check a seeded asset and a mapped page with `curl -sI`, confirming both the
   `Content-Type` and the `Cache-Control` of each.

## Tests

The mapping is a pure value and needs table-driven tests, not a host test:

- `/app.js`, `/app.mjs` → JavaScript, from a map that contains those rows.
- `/app.css` → CSS; `/app.json` → JSON; `/app.txt` → plain text.
- `/app` → the fallback; `/app.html` and `/app.htm` → the fallback.
- `/v1.2/app` → the fallback; `/v1.2/app.js` → JavaScript.
- `/.js` → the fallback; `/app.js/child` → the fallback.
- `/APP.JS` → JavaScript if the value is asked about raw text; canonical keys are lowercase.
- A map with a different fallback (`text/plain; charset=utf-8`) applies it to an unmapped key and
  still resolves `.js`.

Configuration tests:

- `Create` rejects a key without a leading dot, a key with a second dot, a value that is not a
  media type, a blank fallback, and a duplicate row differing only in case.
- `Create` treats `null` and empty rows as absent.
- `PageContentTypesConfiguration.FromConfiguration` reads the shipped section into the five rows; a
  missing `Fallback` takes `PageResponse.Default.ContentType`; a row mapped to the fallback resolves
  like an unmapped key; an invalid row throws naming its key.
- The repository's `appsettings.json` still contains `Pages:ContentTypes` with the five rows.

Response tests:

- A page at `/x.js` with no template directive has `Content-Type: text/javascript; charset=utf-8`.
- A page at `/x.css` that calls `SetContentType("text/html; charset=utf-8")` sends HTML.
- A page at `/x` that calls `SetContentType("text/javascript; charset=utf-8")` still sends
  JavaScript; the override is not limited to the extension's kind.
- Every page response carries `Cache-Control: no-cache`, and a page-set `Cache-Control` wins.
- An authenticated response still gets `no-store` and no ETag, whatever the page set.
- `nosniff` and the default CSP are unchanged; an HTML page must still call `AllowScripts` to load
  a script.

Seed tests:

- `/site.css` and `/assistant.js` still send their expected content types and `no-cache` after the
  directive block is removed.
- The stored `/site.css` and `/assistant.js` sources contain no `<%`, so they are directly
  parseable as CSS and JavaScript; `/rocket.svg` still sets its own type.

## Acceptance criteria

- The lookup is a pure `Core` value with table tests, and the extension list exists nowhere in
  code: the section in `appsettings.json` is the only list, and a host's settings file changes it
  without a rebuild.
- `Pages:ContentTypes:Fallback` and `Pages:ContentTypes:Extensions` are the only keys.
- Adding a row to a host's `appsettings.Production.json` changes that page's response after a
  restart, with no rebuild; mapping a row to the fallback retracts it; an invalid map stops the
  process at startup with the offending key named.
- A stored `/x.js` page with no page code sends `text/javascript; charset=utf-8`; `/x.css`,
  `/x.json`, `/x.txt` send their mapped types; everything else sends the fallback.
- `ctx.SetContentType` overrides the default for any page, including back to HTML.
- Every rendered page sends `Cache-Control: no-cache` unless it set its own, and an authenticated
  response is still `no-store` with no ETag.
- `AllowScripts` and CSP are not inferred; `nosniff` remains on every response, and the host's
  settings file is read-only in the deployment, so nothing the site runs can change it.
- The seeds keep their behaviour; the scripts and stylesheets carry no template directive at all and
  are directly parseable in their own languages.
- No change to the SDK, the compiler, the page store schema or the template language, and changing
  the map never recompiles a stored page.
- `make check` passes.

## Risks and open questions

- **Core module version.** Adding `PageContentTypes` to `Core` changes `Core`'s MVID, so
  `RuntimeId` changes and every stored image is recompiled on its next request (DEC-26). That is a
  one-time cost, and it is the last one this feature causes: the map is not compiled in, so editing
  it later changes no page's identity.
- **A valid but wrong map.** Validation catches a malformed row, not a `.js` row that points at
  `text/html`. The deploy notes should show the `curl -sI` check, and the host's settings file
  belongs in version control for the host it configures. A `pages content-types` command to print
  the effective map was considered and left out; the log line at startup already states the fallback
  and the row count.
- **Restart to apply.** A settings edit needs a service restart, so there is a moment of downtime and
  a window where the old map is still being served. That is the price of not carrying a watcher and
  a last-known-good copy; if the site ever needs live changes, that is its own job with its own
  tests.
- **The mount.** A single-file bind of `appsettings.Production.json` depends on the file existing:
  podman turns a missing host path into a directory and the app cannot read it. `make deploy` seeds
  the file, and the deploy notes should say what to do if it has been removed.
- **Existing keys that look like assets.** A page at `/notes.json` that relied on the fallback will
  start being served as JSON. The site is owner-authored and the map is explicit, but a pre-deploy
  audit is cheap. `ctx.SetContentType("text/html; charset=utf-8")` is the escape hatch.
- **Cache default.** Every page becoming `no-cache` is a deliberate simplification, and the cost is
  that a page which really is cacheable revalidates instead. The ETag makes that cheap and a page
  can opt out, but a page that opts out must be safe to share. Confirm that is acceptable before
  implementation.
- **`.svg`.** Deferred from the default map, and now the owner's choice rather than a code change.
  If it is added on a host, the active-content reasoning still has to hold: `nosniff` and the CSP
  are not inferred from the row.
- **Where the rule lives.** `Core` keeps the lookup pure and available to both `Web` and the
  assistant's validator; the section, the reading and the default fallback are `Web`'s, beside the
  other options. Configuration is never handed to the core as configuration: it is turned into a
  plain value first.
- **Kind from the media type.** If the validator later needs a kind a media type cannot express,
  the section grows an explicit `Kind` per row and the derivation becomes the default when it is
  absent. Not needed for the default map.
- **Explicit `.html`/`.htm` rows.** The fallback covers them; adding rows changes nothing and may
  make the map look more complete than it is. Leave them out.
- **Naming.** `PageContentType`, `PageContentTypes` and `PageContentKind` keep the prefix.
  `.editorconfig` only fixes casing, so nothing forces it, but a bare `ContentType` sits beside
  `PageResponse.ContentType`, `HttpResponse.ContentType` and `SimpleContext`'s `_contentType`, and
  `PageContentTypes` reads like its neighbours in `PageEnvironment`, `PageStorageOptions` and
  `PageFetchOptions`. `Core.Pages` already carries `PagePath`, so the prefix is the local habit.
  `PageFileKinds` would fit better if the validator later needs more than a media type and a
  language.
