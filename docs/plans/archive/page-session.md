<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page session

| | |
| --- | --- |
| Status | Implemented 2026-11-12 |
| Size | Large — an SDK capability, two tables and a store, the shell's cookie and rotation, the loss of `context.Session`, a caching change with a known regression, a cookie rename, and a seeded page |
| Job | IDEA-014 — a page cannot keep per-visitor state, so a form cannot redirect and say why; closed, see [jobs](../../jobs/README.md#closed) |
| Spike | None. The shape is settled; the uncertain part is cost, not behaviour |
| Decisions | DEC-233–DEC-237, recorded with this plan: the identity, the ownership model, the caps, pages become uncacheable, and the cookie names |

## The short version

A page can keep per-visitor state, bounded and cleared on sign-out:

- **One capability**, `IPageSession` from `ctx.Capabilities`, offering `TryGetValueAsync<T>`, two
  overloads of it for reading another page's key, `SetAsync<T>`, `DeleteAsync`, `KeysAsync` and
  `AllKeysAsync`. Async and tokenless like `IPageStorage`; misuse throws like `IPageStorage`; a
  `HEAD` render reads and does not write; a layout reads and does not write.
- **One cookie**, the existing `.uk.osric.asp.session`, reused and renamed to `s` with the other
  two. Minted only when a page writes, `HttpOnly`, `SameSite=Strict`, the same `SecurePolicy` as the
  rest.
- **Two tables in the site database**, so a visitor does not notice a restart: a session row that
  carries the sliding timestamp and the account, and one row per value keyed by
  `(SessionId, PagePath, Key)`.
- **The editor's flash message moves onto it**, and `context.Session` goes: the answer to a `POST`
  is still a `303`, the message is now written under the path of the endpoint that will read it, and
  `save-and-view` stops writing one because nothing reads it.
- **Pages become uncacheable.** Every stored page answers `no-store` with no validator, which
  removes the last hazard a per-visitor body had. Scripts and images wait for IDEA-010.

## Goals

1. A page that answers a form with a `303` can say what happened, publicly and as a signed-in
   visitor, without the view growing into a shell endpoint.
2. The state is per visitor, bounded by configuration, swept, cleared on sign-out, and rotated when
   the identity changes.
3. The shell gets smaller, not larger: `AddSession`, `AddDistributedMemoryCache`, `UseSession` and
   every `context.Session` call go, and the flash message becomes a key with a path like any other.
4. One page cannot write, list or delete another page's keys, by construction rather than by a
   check; reading is open, deliberately, because it is what lets a page show what another left.
5. A visitor sees no loss across a restart, a deploy or an idle half hour spent on a half-written
   form inside the configured window.
6. The site says what it does, on a `/privacy` page that ships as a seed.

## Non-goals

- **Page-to-page messaging as a feature.** Reading another page's key ships here; a *send* with a
  redirect is [IDEA-027](../../jobs/IDEA-027.md), and the editor becoming a page is
  [IDEA-017](../../jobs/IDEA-017.md).
- **Merging with `IPageStorage`.** Page state is site content — exported, imported, scrubbed,
  measured — and session values are a visitor's scratch space; the reasoning is under *considered
  and rejected*.
- **Caching for stored assets.** Losing the validator on `/site.css` and `/rocket.svg` is accepted
  until IDEA-010 landed ([immutable page assets](immutable-page-assets.md)).
- **A general key/value framework.** Two capabilities that look alike and share nothing but a JSON
  options instance is the right amount of sharing.

## What exists today

- `context.Session` is the framework's, backed by `AddDistributedMemoryCache`, with a 20-minute idle
  timeout and `.uk.osric.asp.session` (`SameSite=Strict`, `HttpOnly`, `IsEssential`). Lost on
  restart and per process.
- `EditorEndpoint` writes `editor.message` at seven call sites and reads it in two views;
  `save-and-view` writes one and redirects to the page, where nothing reads it.
- `IPageStorage` is one store per page, shared by every visitor, bound to `PagePath` so a page cannot
  name another, with caps enforced in the same statement as the write.
- Anonymous pages get `Cache-Control: no-cache` and a strong `ETag`; signed-in pages get `no-store`
  and no validator; a page may set `Cache-Control`, `ETag`, `Expires` and `Vary` through the
  header allow-list.
- `PagePath.Normalize` already lowercases with the invariant culture and composes to NFC
  (`LowercaseText.Normalize`), so a path used as a key component is canonical before it is stored.
- `LocalUser.UserName` is the primary key of an account and the `NameIdentifier` claim, already
  normalised by `UserName.Normalize`.

## Design

### D1 — The capability

In `Sdk`, beside `IPageStorage`:

```csharp
Task<PageValue<T>> TryGetValueAsync<T>(string key);                              // own path
Task<PageValue<T>> TryGetValueAsync<T>(string pagePath, string key);             // any page
Task SetAsync<T>(string key, T value);                                           // own path
Task<bool> DeleteAsync(string key);                                              // own path
Task<IReadOnlyList<string>> KeysAsync();                                         // own path
Task<IReadOnlyList<PageSessionKey>> AllKeysAsync();                              // every page
```

`PageSessionKey` is a `readonly record struct` of `Page` and `Key`. `AllKeysAsync` returns pairs
because a bare key cannot be read: reads are addressed by `(page, key)`.

Path composition is the host's, never the author's: `SetAsync("draft", …)` stores under the
rendering page's path, so a page *cannot name* a key outside its own space and there is no prefix
rule to enforce. The second read overload normalises its argument through `PagePath.Normalize`
before use, so `../`, a trailing slash and a different case cannot address something else.

Serialisation is the same static `JsonSerializerOptions` the storage scope uses (web defaults, string
enums), and a stored JSON `null` is a found value with a null, as storage does.

### D2 — The store

Two tables, both in the existing `StorageDbContext`:

- `PageSessions`: `SessionId` (TEXT, key), `Account` (TEXT, null for anonymous), `CreatedAt`,
  `UpdatedAt` (both UTC ISO-8601 text, as the pair scans already read them).
- `PageSessionValues`: `(SessionId, PagePath, Key)` key, `Value` (TEXT, JSON), cascade delete from
  the session row.

`PagePath` is required on every value row; the shell is an owner with a reserved path, exactly like a
page. There is no empty-string or null area, which also keeps SQLite's NULL-is-distinct rule out of a
unique key.

`Account` holds the normalised user name. It is set when a session is minted for a signed-in visitor
and when sign-in rotates one. A row whose `Account` is not null is only honoured when it matches the
principal of the request; a mismatch deletes the row and starts a fresh session, so a stale page
cookie cannot inherit an account's caps or read what an owner's page left behind.

Each `SetAsync` is one statement that refuses at the cap, the way `SqlitePageStateStore.UpsertAsync`
does: the session must still exist, the key count must stay inside `MaxKeys` and the byte total
inside `MaxBytes`. The caps are per session, not per page, so one page's keys can use the room
another would have had — acceptable when the pages are the owner's.

### D3 — Identity, caps and lifetimes

Configuration under `Pages:Session`, with a safe minimum for each value, read the way
`PageStorageOptions` reads its section:

| | anonymous | signed in |
| --- | --- | --- |
| value bytes | 4 KiB | 16 KiB |
| keys | 8 | 32 |
| total bytes | 16 KiB | 64 KiB |
| idle lifetime | 30 min | 8 h |

Plus:

- Key length reuses `PageStateKey` and the `Pages:Storage:MaxKeyChars` behaviour.
- A total live-session cap, default a few thousand rows. At the cap, the most idle **anonymous**
  session is evicted first; signed-in sessions are untouched while any anonymous one remains, and only
  once none does is the most idle signed-in session evicted. A mint is refused only when nothing can be
  evicted at all. The owner's session is the one nobody can mint again, so a flood of anonymous
  sessions must not be able to push it out: the ordering is the difference between a cap that sheds
  strangers and one that sheds the person using the site.
- Eviction runs as one bounded batch when the count reaches the cap, not one row per mint, so a burst
  of new sessions costs a handful of deletes rather than one per insert.
- Expiry is swept by a `BackgroundService`, `PageSessionCleanupService`, on a five-minute
  `PeriodicTimer` — the same shape as `AuthTicketCleanupService`, with the same catch-and-log so a
  sweep that fails retries rather than taking the host down. The timer is the plan, not a sweep on a
  request: a request should not pay for housekeeping, and a site whose visitors stop arriving still
  needs its rows to go.
- The cap path deletes expired rows before it counts anything, so a mint can never evict a live
  session because stale ones are holding the room. This is one index-backed `DELETE` on the mint that
  would otherwise have evicted, not a sweep.
- The service logs one line per sweep that removed anything, with the counts by kind: expired,
  anonymous evicted, signed-in evicted.

Reading slides the idle timer, which is one `UPDATE` on the session row per request that touches the
session, not one per key. `HEAD` reads do not slide it.

### D4 — The cookie

`.uk.osric.asp.session` keeps its name for one release and becomes `s` in the cookie step (D8). A
value is 128 random bits in base64url; a page cannot set or read it (`Set-Cookie` is refused by the
allow-list, and `HttpOnly` keeps it from script). It is minted lazily: a render that only reads gets no
`Set-Cookie`, so a page that never uses the session costs a visitor nothing and is still cheap to
serve.

The cookie is written after the render, on every path that leaves the page pipeline — the document and
the redirect — because the body and headers are already buffered there. Once D6 removes the `304`
there is no third path, and before it there is nothing to remember in a bodyless answer anyway.

### D5 — Replacing `context.Session`

`EditorEndpoint` writes its flash under the path of the endpoint that reads it: `/edit` for the
listing, `/edit/{path}` for the editor view. Both are reserved, so no page can occupy those
namespaces, and a page can read them but not write or delete them.

`save-and-view` stops writing a message: it redirects to the page, where nothing reads it, and the
message today survives until the next editor render, which is a bug rather than a feature.

Sign-in discards the pre-existing session row and mints a new one with `Account` set. Sign-out deletes
the rows. Neither writes a message: a message written after a sign-out has nowhere to live.

`AddSession`, `AddDistributedMemoryCache`, `app.UseSession()` and the `ISession` feature go.

### D6 — Pages become uncacheable

`PageEndpoint` sends `no-store` on every stored page, whether or not the session was touched, drops
the strong `ETag`, and loses the `If-None-Match` branch: a per-visitor body has no business in a
shared cache and no business inviting a conditional request. `Cache-Control`, `ETag`, `Expires` and
`Vary` leave `SimpleContext`'s allow-list, so a page cannot claim a policy the host will override.

`docs/pages.md`, `PageAuthorPrompt` (its guidance says a page may set its own `Cache-Control`) and
`/help` say so in the same change.

Known regression, accepted: `/site.css` and `/rocket.svg` are fetched in full on every page load
instead of revalidating once. IDEA-010 is where that comes back, and it is the job that also decides
what `immutable` means for a stored asset.

### D7 — The hosting

`PageChain.CreateLayer` registers `IPageSession` with the request's session id, the rendering path,
and `writable: !_isHead`; the layout layer gets a read-only handle, as it has for `IPageWriter`. The
session id and its row are resolved once per request by the page endpoint, so two layers cannot see
different sessions.

### D8 — The cookie names

`a` (auth), `f` (antiforgery), `s` (session). One deploy signs everyone out and invalidates any form
in flight; the mapping is documented in `docs/shell.md` because a browser's cookie pane stops being
self-explanatory. This is the cheapest moment: the session cookie's value changes meaning anyway.

### D9 — `/privacy`

Already drafted as a seed. It names no cookie name and no storage mechanism, so D8 cannot date it. The
plan adds the link: the seeded `_layout` gains it, and an existing installation shows it after
"Restore from seed" on `_layout`.

## Considered and rejected

- **Merging with `IPageStorage`.** The rows would look similar and the lifetimes would not: page state
  is content that is exported, imported, scrubbed and measured (`PageArchiveReader` will refuse an
  archive that holds a third table; `PageStorageSampler` sums `PageState`), while a session value is
  scratch space that must never leave the site. One table plus a discriminator makes every one of
  those tools learn about visitors.
- **A reserved `system.` prefix for host keys.** With writes restricted to the page's own path, the
  prefix only guarded a name, and the value was readable anyway.
- **A host-only area with a null or empty path.** It added a sentinel whose only job was concealment,
  at the cost of a nullable column and SQLite's NULL-in-a-unique-key trap.
- **A separator-composed key instead of a path column.** It is an invariant to keep true forever, it
  hides the key in the database, and it moves the key cap onto text the author did not write.
- **Key normalisation.** Path components are canonical before they are stored; keys stay ordinal and
  case-sensitive, as they are in page state.
- **`Try` methods instead of exceptions.** The failure modes are the ones `IPageStorage` already
  treats as page bugs, and a `bool` cannot say why. Two capabilities that fail the same way are worth
  more than a second pattern.
- **A charge for `AllKeysAsync`.** It is bounded by the visitor's own cap and cannot reach another
  visitor, so there is nothing to charge and nothing to restrict.
- **A `TryGetValueAsync(PageSessionKey)` overload.** It saves typing `entry.Page, entry.Key` for a
  caller who already knows the path, and it cannot serve the loop it looks made for: a page does not
  know the type of a key another page wrote, so reading over `AllKeysAsync` needs a raw JSON read
  rather than a typed one. `KeysAsync` covers a page inspecting its own keys. A non-generic
  `GetRawAsync(PageSessionKey)` is the member that would earn its place, and only for a session
  inspector that does not exist.
- **Keeping the `ETag` and suppressing a `304` only when the session was written.** Correct, and
  harder to keep correct than removing the validator for good.

## What landed

The shape above, with these details worth recording beside it:

- `PageSessionResolver` and `PageSessionRequest` carry the once-per-request decision: the id, whether
  the row exists, and whether the response owes a cookie. The endpoint resolves it before the chain
  exists, so every layer shares one session.
- `Web.Tests` uses `FakePageSessionStore` for the endpoint and capability tests, the way
  `FakePageStateStore` already stands in for page state, and `SqlitePageSessionStoreTests` proves the
  real statements, caps, sweep and eviction order against a migrated database.
- `PageSessions` stores its timestamps as `DateTime` rather than `DateTimeOffset`: SQLite cannot
  order by an offset, and eviction is an order. Every value written is UTC.
- The migration regenerated the model snapshot with the EF 11 generator, which reformats the file; the
  only model changes are the two entities and their indexes. The snapshot it replaced was stale
  (`ProductVersion` 10.0.14) and had lost the columns `AddRunUsage` added.

## Components

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/IPageSession.cs` | New — the contract of D1 |
| `src/uk.osric.asp.Sdk/PageSessionKey.cs` | New — the `(Page, Key)` value |
| `src/uk.osric.asp.Web/Pages/PageSessionScope.cs` | New — the page-bound handle: validation, JSON, `HEAD`, abandonment |
| `src/uk.osric.asp.Web/Pages/IPageSessionStore.cs` | New — the store contract |
| `src/uk.osric.asp.Web/Pages/SqlitePageSessionStore.cs` | New — D2's statements, D3's eviction and `RemoveExpired` |
| `src/uk.osric.asp.Web/Pages/PageSessionCleanupService.cs` | New — the five-minute sweep |
| `src/uk.osric.asp.Web/Pages/PageSessionOptions.cs` | New — D3's configuration |
| `src/uk.osric.asp.Web/Pages/PageSessionEntry.cs`, `PageSessionValueEntry.cs` | New — the entities |
| `src/uk.osric.asp.Web/Storage/StorageDbContext.cs` | Changed — two entities, cascade |
| `src/uk.osric.asp.Web/Storage/Migrations/*` | New — one migration |
| `src/uk.osric.asp.Web/Pages/PageChain.cs`, `PageEndpoint.cs`, `PageEnvironment.cs` | Changed — the session resolved once, the capability registered, the cookie written |
| `src/uk.osric.asp.Web/Authentication/AuthenticationExtensions.cs` | Changed — `AddSession` and the cache go, the cookie options move |
| `src/uk.osric.asp.Web/Authentication/PageSessionCookie.cs` | New — mint, read and clear |
| `src/uk.osric.asp.Web/Authentication/LoginEndpoint.cs` | Changed — rotation on sign-in, clearing on sign-out |
| `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` | Changed — `context.Session` replaced, `save-and-view` loses its message |
| `src/uk.osric.asp.Web/Pages/SimpleContext.cs` | Changed — the header allow-list, the default `no-cache` |
| `src/uk.osric.asp.Web/Pages/PageEndpoint.cs` | Changed — `no-store`, no validator, no `304` |
| `src/uk.osric.asp.Web/Pages/Seeds/_layout` | Changed — the privacy link |
| `docs/pages.md`, `docs/shell.md`, `docs/storage.md`, `docs/jobs/README.md` | Changed — D1–D9 |
| `docs/jobs/IDEA-027.md` | New — page-to-page messaging |

## Tests

Unit, in `Web.Tests`, one behaviour each:

- A value one visitor writes is invisible to another.
- A page writes under its own path and reads it back; it cannot write or delete under another path.
- A page reads another page's key by path, and a path that normalises to a different page is refused.
- `KeysAsync` lists only the caller's keys; `AllKeysAsync` lists pairs from every page.
- A `HEAD` render reads and refuses a write; a layout gets a read and refuses a write.
- A value over the cap, a key over the length, a key with a control character, and a key that would
  take the session over its key count or byte total are each refused before a row is written.
- A read slides `UpdatedAt`; a `HEAD` read does not.
- A session row whose `Account` does not match the principal is discarded.
- `RemoveExpired` deletes the rows past their idle window and leaves the rest, against the injected
  `TimeProvider`.
- A mint at the cap removes expired rows before it evicts a live session.
- The live-session cap evicts the most idle anonymous session first, and leaves a signed-in session
  alone while any anonymous one remains.
- With no anonymous session left, the most idle signed-in session is evicted.
- A mint is refused only when nothing can be evicted at all.
- The editor writes its flash under the landing path, and a `303` after a save shows it once.

Integration, in `Web.IntegrationTests`, because they need the real cookie:

- A page that writes sets the cookie, with `HttpOnly`, `SameSite=Strict` and the production
  `SecurePolicy`; a page that only reads sets nothing.
- Two clients keep separate values, and a client presenting an unknown key starts empty.
- Signing out clears the rows; signing in rotates the key and the old key no longer resolves.
- A page response for any verb is `no-store` with no `ETag`, and `If-None-Match` is ignored.

The seeded `/privacy` page is covered by `EveryShippedSeedCompilesWithoutDiagnosticsAsync` and by its
own fragment test, both already in place.

## Phases

### Phase 0 — the store (two days)

Tables, migration, options, the store, the cleanup service, the scope and the capability; unit tests
for isolation, caps, sliding, expiry and eviction. Nothing is registered yet.

### Phase 1 — the shell (one day)

The cookie, the endpoint resolving the session once, the capability on the chain, the layout read-only,
the cleanup service registered beside the store, the integration tests for the cookie.

### Phase 2 — `context.Session` goes (half a day)

The editor's flash moves, `save-and-view` stops writing, sign-in rotates, sign-out clears, and
`AddSession`/`UseSession`/the memory cache come out. `TestSession` in `Web.Tests` is replaced by the
real store over a test database.

### Phase 3 — pages uncacheable (half a day)

`no-store`, no validator, allow-list, `PageAuthorPrompt`, `docs/pages.md`.

### Phase 4 — the cookie names (half a day)

`a`, `f`, `s`, and the mapping in `docs/shell.md`.

### Phase 5 — documents and jobs (half a day)

`/help` gains a session section, `docs/storage.md` gains the two tables, `docs/shell.md` gains the
identity and rotation rules, the privacy link lands, IDEA-014 is closed and IDEA-027 is filed with its
IDEA-017 cross-reference.

## Documentation and decisions

- `/help`: the capability, the two read forms, own-path write, the caps, and that a `HEAD` cannot
  write. One sentence on the difference from `IPageStorage`.
- `docs/pages.md`: the capability list, and the response rules without a validator.
- `docs/shell.md`: the cookie, the rotation, the sign-out, the shell's own keys and their paths.
- `docs/storage.md`: the two tables, their caps and the sweep.
- Decisions recorded: the identity (DEC-233, a shell-minted cookie, lazy), the ownership model
  (DEC-234, host-composed path, open reads, own-path writes), the caps and their configuration
  (DEC-235), pages are uncacheable (DEC-236), and single-character cookie names (DEC-237).

## Risks

- **SQLite write amplification.** One row per `SetAsync` plus one session touch per request. Small,
  but it is the one place a page's cost changes character; measure it if the site ever gets busy.
- **The session table is attacker-growable.** A public page that writes on every visit lets any
  client create rows. The live cap, the anonymous-first eviction, the 30-second sweep and the
  anonymous caps are the control, and they are tested. The eviction order is what keeps a flood from
  costing the owner their own session; a mint refused is the last resort, not the first.
- **The cookie rename locks the owner out once.** It is a deploy note, not a bug: sign out, deploy,
  sign in.
- **Losing validators is visible.** Every page load refetches the stylesheet and the icon until
  IDEA-010.
- **In-flight sessions are lost on deploy.** Acceptable once, for a feature that exists to survive
  restarts afterwards.

## Delivered when

- A page on a `POST` answers `303` and the next render of that page shows what happened, for an
  anonymous and a signed-in visitor.
- Two clients hold different values, a restart loses nothing inside the idle window, and signing out
  leaves nothing behind.
- A page cannot write, list or delete another page's keys; it can read them by path.
- `context.Session`, `AddSession`, `UseSession` and `AddDistributedMemoryCache` are gone from the
  host.
- Every stored page answer is `no-store` with no validator.
- The three cookies are `a`, `f` and `s`, and `/privacy` is linked.
