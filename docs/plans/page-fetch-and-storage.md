<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Fetch and per-page storage

Implementation plan for [IDEA-002 — a page cannot call an outbound HTTP API](../jobs/IDEA-002.md)
and [IDEA-003 — a page has nowhere to keep state](../jobs/IDEA-003.md). The two jobs are one
change because they share the same seam: both are capabilities a page reaches through `IContext`,
both need the render's deadline, and both must keep the page contract free of ASP.NET, database and
`HttpClient` types.

## Status

Proposed 2026-11-06. Not implemented. IDEA-002 and IDEA-003 stay open until the phases below ship;
this document is the implementation contract and the job files keep the original idea. See
[Folding the jobs](#folding-the-jobs) for the index changes.

## The short version

1. `IContext` gains two capabilities: `Storage`, a key/value store scoped to the page that owns it,
   and `Fetch`, a bounded server-side HTTP request.
2. Storage is a `PageState` table in the existing SQLite database. Keys are ordinal strings; values
   are JSON round-tripped by the host. A page cannot name another page's storage because the only
   handle it ever gets is bound to `IContext.Path`.
3. Fetch is off until an operator lists a host. It is `GET`/`POST` only, has no automatic redirects
   to hosts that are not also allowed, and returns a value for every failure rather than throwing.
4. Both capabilities are synchronous at the page surface, because `IHandler.Handle` is synchronous.
   They run on the page's own thread, are bounded by the render deadline, and stop accepting work
   once `SimpleContext.Abandon` has been called.
5. A narrow set of `System.Text.Json` types joins the page policy so a fetched JSON body can be
   parsed. `System.Net` stays denied: `ctx.Fetch` is the only way out.
6. The page-author prompt, architecture, decisions, deployment notes and the job index are updated.
   [IDEA-009](../jobs/IDEA-009.md) gains a requirement to export and import page state.

## Goals and non-goals

**Goals**

- A page can call an HTTP API and keep a small amount of state between requests.
- The page contract stays plain values. Page code never names `HttpClient`, `DbContext`, SQLite,
  ASP.NET or the shell's storage types.
- Storage is isolated per page at the API and storage layers, and is deleted with the page.
- Every outbound request and every storage write is bounded in time and size.
- A failure is understandable in page code: fetch failures are values; storage misuse is an
  exception that the existing buffered response and render budget already handle.
- No new NuGet package. `System.Text.Json`, `IHttpClientFactory`, EF Core and SQLite are already in
  the framework or already referenced.

**Non-goals**

- SQL, joins, queries, indices or transactions exposed to page code. This is key/value only.
- Per-user state. `PageRequest` has no identity, and pages are public; state is shared by every
  visitor. [IDEA-011](../jobs/IDEA-011.md) is the separate auth hook that makes a private page and,
  later, per-user state possible; it is not part of this plan.
- Binary request or response bodies. Fetch is for text, JSON, HTML and similar payloads.
- Fetch caching, storage TTL, expiry, compare-and-set or cross-key transactions.
- Encryption at rest. The database is the site's own storage and is already treated as sensitive.
- A process boundary. [SEC-03](../jobs/SEC-03.md) is still open, and fetch widens egress; the
  allow-list is a footgun guard, not a sandbox.

## The page API

### Storage

```csharp
namespace uk.osric.asp.Sdk;

/// <summary>A page's own key/value store.</summary>
public interface IPageStorage {
    /// <summary>
    /// Reads a value and reports whether the key exists. A missing key is false with the default.
    /// A stored JSON null is true with a null value when T can represent null; a non-nullable value
    /// type throws JsonException. The test and the fetch are one read, so they cannot race.
    /// </summary>
    bool TryGetValue<T>(string key, out T? value);

    /// <summary>Stores a value, replacing any value already under the key. Null is stored as JSON null.</summary>
    void Set<T>(string key, T value);

    /// <summary>Removes a key; true when it existed.</summary>
    bool Delete(string key);

    /// <summary>The page's keys, in ordinal order.</summary>
    IReadOnlyList<string> Keys();
}

public interface IContext {
    // ...existing members...

    /// <summary>This page's key/value store.</summary>
    IPageStorage Storage { get; }

    /// <summary>Performs a bounded outbound HTTP request.</summary>
    PageFetchResponse Fetch(PageFetchRequest request);
}
```

Page code:

```csharp
<% ctx.Storage.TryGetValue("visits", out int visits);
   visits++;
   ctx.Storage.Set("visits", visits); %>
<p>Visited <%= visits.ToString() %> times.</p>
```

### Fetch

```csharp
namespace uk.osric.asp.Sdk;

public enum PageFetchMethod {
    Get,
    Post,
}

public enum PageFetchFailure {
    None,
    InvalidRequest,
    NotAllowed,
    Timeout,
    Aborted,
    Network,
    HttpError,
    TooManyRequests,
    TooManyRedirects,
}

public sealed record PageFetchRequest(string Url) {
    public PageFetchMethod Method { get; init; } = PageFetchMethod.Get;

    /// <summary>The request body; POST only, and text only.</summary>
    public string? Body { get; init; }

    /// <summary>Additional request headers; the host filters and validates them.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();
}

public sealed record PageFetchResponse(
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    PageFetchFailure Failure,
    bool Truncated) {

    /// <summary>True only for a 2xx response with no transport failure.</summary>
    public bool Succeeded => Failure == PageFetchFailure.None && Status is >= 200 and < 300;
}
```

On a transport failure, `Status` is `0`, `Headers` is empty, `Body` is empty and `Truncated` is
false. An HTTP error response — a 4xx or 5xx, including a 500 — keeps its real status and sets
`Failure == HttpError`, so a page can tell a remote server failure from a successful fetch without
also having to range-check `Status`. A 3xx that is not followed keeps its status and
`Failure == None`.

Page code:

```csharp
<% PageFetchResponse response = ctx.Fetch(
       new PageFetchRequest("https://api.example.com/status") {
           Headers = new System.Collections.Generic.Dictionary<string, string> {
               ["Accept"] = "application/json",
           },
       }); %>
<% if (response.Succeeded) { %>
    <p><%= response.Body %></p>
<% } else { %>
    <p>The status API is unavailable (<%= response.Failure.ToString() %>).</p>
<% } %>
```

## Decisions

### D1 — Storage is a per-page key/value store with JSON values

`IContext.Storage` is created for the page being rendered and carries that page's canonical key.
There is no method that takes a page key, no way to name another page, and no static access. The
isolation is therefore structural: a page can only reach the handle the host handed its own handler.

- Keys are opaque ordinal strings. They are stored with SQLite's `BINARY` collation, so `Name` and
  `name` are different keys. A key is 1–200 characters and has no control characters.
- Values are serialised with `System.Text.Json` using `JsonSerializerDefaults.Web` plus
  `JsonStringEnumConverter`. A page hands over a value and gets the same value back; the page is
  responsible for the type surviving a JSON round trip. `null` is a permitted value and is stored as
  JSON `null`; `Delete` is how a key is removed. Read a stored `null` with a nullable `T` — a
  non-nullable value type cannot represent it and throws `JsonException`. A `null` literal needs an
  explicit type argument, for example `ctx.Storage.Set<Entry?>("last", null)`.
- `TryGetValue<T>` returns `true` and sets `value` when the key exists, including a stored JSON
  `null`; it returns `false` and the default when the key is absent. The test and the fetch are one
  read, so there is no separate has-key check to race. A stored value that cannot be deserialised as
  `T` throws `JsonException`; that is a page bug, and the existing crash protection turns it into a
  clean error rather than a half-written response.
- `Set<T>` replaces the value, `Delete` returns whether a key existed, and `Keys()` returns the
  page's keys in ordinal order.
- Writes are last-write-wins per key. There is no compare-and-set and no read-modify-write primitive.
  A page that increments a counter is racy under concurrency; that is documented, not hidden behind
  a transaction that does not exist.

### D2 — Storage lives in `PageState`, and is deleted with the page

A `PageState` table in the existing SQLite database keeps the state:

```text
PageState
  PagePath  TEXT NOT NULL, BINARY
  Key       TEXT NOT NULL, BINARY
  Value     TEXT NOT NULL          -- JSON
  UpdatedAt TEXT NOT NULL
  PRIMARY KEY (PagePath, Key)
```

- The primary key is the whole access path, so no separate index is needed.
- There is no foreign key. SQLite foreign-key enforcement depends on the connection and the
  explicit deletes below are required anyway; keeping the relationship out of the schema avoids a
  migration-order dependency for no gain.
- `DeletePageAsync` deletes the page's state and tombstones the head in one transaction. This is
  deliberately different from revisions: revisions are history and are retained, while state is
  live data and must not be inherited by a new page created at the same path.
- `ScrubPageAsync` deletes the state as well, only when a tombstoned head was actually removed, so
  a database written by an older build or a direct writer cannot leave orphaned rows.
- `Set` checks that the page head is still live in the same statement as the write. A render that
  was already running when the page was deleted cannot write state back into existence.
- State is per path, not per revision. Editing, restoring or reformatting a page keeps its state.
  Deleting it clears the state; saving the same path again starts empty.

### D3 — Storage calls are synchronous, per call, and deadline-bound

The page handler is `void Handle(IContext)`. Making storage asynchronous would either change that
contract or force every page to write `.Result`; neither is worth it. Storage is therefore a
synchronous API implemented with EF Core's synchronous calls.

- Each call creates its own `StorageDbContext` from `IDbContextFactory<StorageDbContext>` and
  disposes it. It never uses the request-scoped context: an abandoned render can outlive the
  request scope, and a page thread must not touch a disposed scope.
- Each call is linked to the render's deadline and the page's command timeout. A cancelled write
  rolls back; a cancelled read reports the cancellation to the page thread.
- Each `Set`/`Delete` commits immediately. A page that throws after writing keeps the write. There is
  no page-wide transaction, because the page contract has no "end of render" hook and a buffered
  response is not a transaction.
- `SimpleContext.Abandon` makes later storage and fetch calls fail; a call already inside SQLite is
  cancelled by the render deadline. The race is best-effort, and documented.
- The implementation lives in `Web`, behind `IPageStateStore`; the SDK only sees `IPageStorage`.

### D4 — Fetch is the only egress, and it is off until configured

`PageApiPolicy` continues to deny `System.Net`, `System.Net.Http` and everything that would give a
page its own socket or request API. `ctx.Fetch` is the single controlled path out.

- `Pages:Fetch:AllowedHosts` is empty by default. Every fetch then returns `NotAllowed` without a
  connection being attempted. An operator opts in by listing hosts.
- Entries are bare hosts, matched exactly and case-insensitively against `Uri.IdnHost`. No
  wildcards, no prefixes, no regular expressions. A configured entry that is empty, whitespace, or
  contains a scheme, path, query or port is a startup error, not a silently ignored line.
- Only absolute `http` and `https` URLs are accepted. Credentials in the URL (`user:pass@host`),
  other schemes and fragments are rejected as `InvalidRequest`; a fragment would never be sent, so
  accepting it would only hide a broken URL.
- Ports are not restricted beyond the URL's own port. The allow-list is the trust boundary; a host
  the operator lists may expose several services. A configured entry is a bare host; a bracketed
  IPv6 literal is allowed, but a `:` outside brackets is a port and is a startup error.
- No IP-address filtering or DNS pinning in this version. An allow-listed hostname that resolves to
  a private address is the operator's decision. This is recorded, not pretended away; SEC-03 remains
  the boundary for a hostile page author.
- No cookies are sent and `Set-Cookie` is never returned to the page. `UseCookies = false`; a page
  that needs a credential puts it in `Authorization` or an `X-` header deliberately.
- `ctx.Fetch` runs in the host, not the browser. The response's `connect-src 'self'` policy is
  unchanged; it still blocks browser-side fetch, and pages cannot add script anyway.

### D5 — Fetch failures are values, not exceptions

A page that shows live data should be able to fall back. Every transport outcome is therefore a
`PageFetchResponse`:

| Situation | `Status` | `Failure` |
| --- | --- | --- |
| 2xx response | the HTTP status | `None` |
| 3xx response that is not followed | the HTTP status | `None` (a redirect the page can inspect) |
| 4xx or 5xx response, including a 500 | the HTTP status | `HttpError` (`Status` names the code) |
| DNS, connect, TLS, protocol error | 0 | `Network` |
| The per-fetch timeout or the render deadline fired | 0 | `Timeout` |
| The request was cancelled or the render was abandoned | 0 | `Aborted` |
| The host is not allow-listed | 0 | `NotAllowed` |
| The URL, method, body or a header is not acceptable | 0 | `InvalidRequest` |
| This render has made too many requests | 0 | `TooManyRequests` |
| The redirect limit was reached | last 3xx | `TooManyRedirects` |

`Succeeded` means a 2xx with `Failure == None`. A 4xx or 5xx is `HttpError`; the page can test
`Status >= 500` for a remote server failure or a specific code such as `404`. A truncated 2xx has
`Succeeded == true` and `Truncated == true`; the page decides whether a partial body is useful. A
redirect to a host that is not allowed keeps the 3xx status and its `Location` so the page can see
where it pointed, but `Failure` is `NotAllowed` and no connection is made to the new host.

Storage misuse is different: it throws. A key that is too long, a value that cannot be serialised,
a value over the cap, a page over its total cap, a write on `HEAD`, or a write to a deleted page is
a programming error, not a normal outcome a page should branch on. It is validated and serialised
before the write, so the failure leaves no partial row. The buffered response and the exception
handler already turn this into a clean 500 with no half-written body; an authenticated owner sees
the exception detail on the error page, and a visitor sees the generic message.

Answering the question directly: the existing crash protection covers the response and the process,
not the database. It does not stop a page catching an exception and looping, and it does not make a
write atomic with the page's render. The caps and the render deadline are the real protection; the
exception handler is the cleanup.

### D6 — Both capabilities are synchronous because the handler is

`IHandler.Handle` is synchronous and the template language has no `await` form. The host already runs
the handler on a dedicated thread off the request thread, so `Fetch` and `Storage` block only that
page thread. They never block the request thread, the render slot is still bounded by the deadline,
and an abandoned page is left to unwind when its network or database call is cancelled.

The alternatives were considered and rejected:

- Making `IHandler.Handle` async would change every page, every template expression and the
  compiler's generated signature, for no benefit to authors who write synchronous templates.
- Buffering storage writes until the handler returns would need a local read-your-writes view and a
  commit phase that the contract does not have.
- A background writer would make `Set` asynchronous in effect and introduce a queue with its own
  lifetime and failure modes.

### D7 — `HEAD` may read, but not write

The host runs the page for `HEAD` to produce the representation and its validators. A page that
increments a counter on every render would then count HEAD requests, and a page that POSTs to an API
on every render would mutate a remote system on a HEAD. The footgun is closed at the seam:

- `Storage.TryGetValue` and `Storage.Keys` work on `HEAD`.
- `Storage.Set` and `Storage.Delete` throw on `HEAD`.
- `Fetch` with `PageFetchMethod.Post` returns `InvalidRequest` on `HEAD`; `Get` is allowed.

Page code that genuinely needs to distinguish the verbs can read `ctx.Request.Method`, as it can
today. The rule is documented on the SDK members.

### D8 — JSON parsing is a page capability too

A fetched body is text, and most APIs return JSON. The page policy therefore gains a narrow set of
`System.Text.Json` types:

- `System.Text.Json.JsonDocument`
- `System.Text.Json.JsonElement`
- `System.Text.Json.JsonSerializer`
- `System.Text.Json.JsonSerializerOptions`
- `System.Text.Json.JsonSerializerDefaults`
- `System.Text.Json.JsonValueKind`
- `System.Text.Json.JsonException` — the existing exception rule covers only the `System` namespace,
  not `System.Text.Json`, so this one has to be named explicitly.

`System.Text.Json.Serialization` attributes are not added in this version. `JsonSerializerOptions(
JsonSerializerDefaults.Web)` gives camelCase and case-insensitive names, which is enough for the
usual API and matches how storage serialises values. If attributes become necessary, adding the
namespace is a separate, reviewed policy change.

This is a policy *change*, not only a tightening. `PageCompiler.PolicyVersion` goes to `2` and its
comment changes from "tightens" to "changes". The SDK's module version id is already part of
`RuntimeId`, and adding members to `IContext` changes it, so every stored image is recompiled on its
next request anyway. That is the existing deploy behaviour, not a new migration.

### D9 — Caps and defaults

`Pages:Storage` and `Pages:Fetch` follow the shape of `Pages:Render` and `Pages:Compile`: a record
with `FromConfiguration`, clamped to safe minimums.

```json
"Pages": {
  "Storage": {
    "MaxKeyChars": 200,
    "MaxValueBytes": 65536,
    "MaxKeysPerPage": 256,
    "MaxPageBytes": 262144,
    "CommandTimeoutSeconds": 5
  },
  "Fetch": {
    "AllowedHosts": [],
    "TimeoutSeconds": 4,
    "MaxResponseBytes": 1048576,
    "MaxRequestsPerRender": 10,
    "MaxRedirects": 3
  }
}
```

| Setting | Default | Why |
| --- | --- | --- |
| `MaxKeyChars` | 200 | Long enough for a readable key; short enough that keys are not a second value store |
| `MaxValueBytes` | 64 KiB | Bounds one JSON value; the job's suggested size |
| `MaxKeysPerPage` | 256 | Bounds a page that writes a key per request |
| `MaxPageBytes` | 256 KiB | Four times the value cap; a page that writes in a loop hits a wall |
| `CommandTimeoutSeconds` | 5 | A local SQLite command should not outlive the default render budget |
| `Fetch.TimeoutSeconds` | 4 | Below the default 5-second render budget, so a slow API becomes a value the page can render instead of a 504; raise both together |
| `MaxResponseBytes` | 1 MiB | The job's suggested body cap; decompressed bytes, because decompression is on |
| `MaxRequestsPerRender` | 10 | A page cannot turn one render into a request amplifier |
| `MaxRedirects` | 3 | Redirects are followed manually, re-checking the allow-list each hop; `0` disables following |

Each cap is clamped to a safe minimum; `MaxPageBytes` is never allowed below `MaxValueBytes`, so a
single value at the per-value limit can always fit an empty page. The per-value cap is hard: the
JSON is serialised and measured before the write. The per-page caps are enforced in the same SQLite
statement as the write, so concurrent writers either fit or get a busy/rule failure; they cannot
silently exceed the cap through a read-then-write race in the host. A busy database is reported as a
storage error, not retried forever.

### D10 — No new SQL, no user identity, no caching

These are deliberate limits, not omissions:

- Storage has no query language. Keys are exact matches and `Keys()` is a listing. Anything that
  needs a query is a different job.
- All visitors share a page's state. A guestbook is possible; "my drafts" is not until
  [IDEA-011](../jobs/IDEA-011.md) adds page authentication, and per-user storage is still a later
  change after that.
- Nothing expires. The caps bound the store; deleting the page clears it. A TTL or sweep is a
  follow-up.
- Fetch has no cache. Every call goes to the network. Storage is not a fetch cache; a page that
  wants one writes one explicitly.

### D11 — Observability without leaking content

- `page.fetch.duration` is a histogram on the existing `uk.osric.asp` meter, tagged with
  `page.path` and `outcome` (`ok`, `http-error`, `not-allowed`, `timeout`, `network`, `invalid`,
  `limit`, `redirect-limit`, `aborted`). The histogram's count is the request count, so no separate
  counter is needed.
- Storage writes request a `PageStorageRefresh`; `PageStorageSampler` includes `PageState` in the
  `page.storage.payload.size` gauge, and the gauge description says so.
- No log line contains a full URL with its query, a request header, a response body or a stored
  value. The page category `uk.osric.asp.Pages`, with the page's key on a `Path` scope, is used for
  page-level refusals; the host is named, not the URL.
- A fetch refusal is a warning because it is usually a missing allow-list entry. A transport
  failure is information; a page that polls a flaky API should not fill the journal with warnings.

## Storage design

### Types and wiring

| Piece | Home | Job |
| --- | --- | --- |
| `IPageStorage` | `Sdk` | The page contract |
| `PageStateEntry` | `Web/Pages` | The EF entity beside the page entities |
| `IPageStateStore` | `Web/Pages` | Path-parameterised storage operations used by the shell |
| `PageStorageScope` | `Web/Pages` | Implements `IPageStorage` for one canonical path |
| `SqlitePageStateStore` | `Web/Pages` | EF Core implementation, beside `SqlitePageStore` |
| `PageStorageOptions` | `Web/Pages` | `Pages:Storage`, in the style of `RenderOptions` |

`PageEndpoint` resolves the store and creates the scope; `SimpleContext` receives the scope and
never the store. A page cannot manufacture a scope because `PageStorageScope` is internal and the SDK
exposes only the interface. `IPageStateStore` exposes the atomic text read —
`TryGetValue(PagePath, string, out string?)` — and `PageStorageScope` deserialises it for the SDK's
generic method; the one SQL read is where presence and value are decided.

### Operations

- `TryGetValue<T>`: one `SELECT Value FROM PageState WHERE PagePath = ? AND Key = ?`. No row →
  `false` and `value = default`. A row → deserialize and `true`; a stored JSON `null` is a row, so
  it is `true` with a null value and is how "deliberately null" is distinguished from absent. A row
  that does not deserialize as `T` → `JsonException`. There is no separate contains query, so no
  test-then-fetch race.
- `Set<T>`: validate the key, serialise (a null value becomes JSON `null`), then a single conditional
  upsert. The statement
  inserts only when the page head is live, the key count after the write is within
  `MaxKeysPerPage`, and the total key+value bytes after the write are within `MaxPageBytes`; the
  conflict branch updates under the same byte cap. Zero affected rows means a rule was hit; the
  store re-reads the key count and total to name it in the exception. That re-read is diagnostic
  only — the write statement's condition is the authority.
- `Delete`: delete by both columns; `true` when a row was removed. Deleting a missing key is not an
  error.
- `Keys`: select the page's keys and return them ordinal-sorted; a key whose value is JSON `null` is
  still present.
- `UpdatedAt` comes from the registered `TimeProvider`, not `DateTimeOffset.UtcNow`, so tests can
  pin it; the column is informational and no read path depends on it.
- `DeletePageAsync`: update the head to a tombstone and delete the page's state in one transaction.
- `ScrubPageAsync`: delete the page's state, then follow the existing head-then-revisions order
  (the head references a revision with `Restrict`, so the head must go first).
- `PageStorageSampler.ReadSizesAsync`: add the `PageState` key and value bytes to the payload total,
  and update the gauge description.

### JSON

`SqlitePageStateStore` owns one static `JsonSerializerOptions`:

```csharp
new JsonSerializerOptions(JsonSerializerDefaults.Web) {
    Converters = { new JsonStringEnumConverter() },
};
```

`JsonSerializerOptions` is thread-safe once it has been used, so one instance is shared. It is
Web-only; the SDK's generic signatures do not mention JSON, which keeps the page contract free of
serializer types. A page-declared record or class with public properties round-trips; a type with
no usable constructor, a reference cycle, a delegate or a `Type` member does not, and the page sees
the `JsonException`. JSON `null` round-trips as `null`; `TryGetValue<T>` returns `true` with a null
value, which is how a stored null is distinguished from an absent key.

### Concurrency and deletion

- SQLite serialises writers. Two pages writing different keys both succeed; two writing the same
  key are last-write-wins.
- A write checks the live head in the same statement, so a deleted page cannot gain state from a
  render that was already running.
- A read may see an older value while another render is committing; there is no snapshot isolation
  contract beyond SQLite's own.
- The total cap statement sees the committed rows plus its own write. A concurrent writer gets a
  busy/rule outcome rather than a silent overrun.

### Deletion and export

`DeletePageAsync` and `ScrubPageAsync` are the two deletion paths today. Both must clear
`PageState`, and both have tests. [IDEA-009](../jobs/IDEA-009.md) export/import must carry state
as well; a page archive that loses a guestbook is not a round trip. The archive shape is a
follow-on: the simplest form is one JSON document per page keyed by the storage keys, validated with
the same key and value caps on import.

## Fetch design

### Types and wiring

| Piece | Home | Job |
| --- | --- | --- |
| `PageFetchRequest`, `PageFetchResponse`, `PageFetchMethod`, `PageFetchFailure` | `Sdk` | The page contract |
| `PageFetchOptions` | `Web/Pages` | `Pages:Fetch`, in the style of `RenderOptions` |
| `IPageFetcher` | `Web/Pages` | The seam `SimpleContext` calls; a test replaces it |
| `PageFetcher` | `Web/Pages` | The `HttpClient` implementation, request filters and metrics |

```csharp
internal interface IPageFetcher {
    PageFetchResponse Fetch(
        PageFetchRequest request,
        string pagePath,
        CancellationToken cancellationToken);
}
```

`PageFetcher` is registered as a typed client (`AddHttpClient<IPageFetcher, PageFetcher>`) so the
handler is pooled and the host owns the socket lifetime. `SimpleContext` receives an `IPageFetcher`
and passes its own page path for the metric and log tags. The caller's token already combines
`IContext.Aborted` and the render deadline; the fetcher adds its own per-fetch timeout.

### Request validation

Before any socket is opened:

1. `Uri.TryCreate(url, UriKind.Absolute)` and the scheme is `http` or `https`.
2. `Uri.UserInfo` is empty and the URL has no fragment; both are `InvalidRequest`.
3. `Uri.IdnHost` matches an `AllowedHosts` entry ordinal-ignore-case.
4. `Method` is `Get` or `Post`.
5. A `Get` request has no body.
6. Every header name and value is accepted by the request-header allow-list and contains no CR or LF.

Request headers are an allow-list, not a pass-through:

`Accept`, `Accept-Charset`, `Accept-Encoding`, `Accept-Language`, `Authorization`, `Cache-Control`,
`Content-Type`, `If-Modified-Since`, `If-None-Match`, `Range`, `User-Agent`, and any `X-` name.

`Host`, `Content-Length`, `Connection`, `Transfer-Encoding`, `Upgrade`, `Proxy-*`, `Cookie` and
other framing or ambient-credential headers are refused. The page can read `ctx.Request.Cookies`
and put a value in `Authorization` if it really means to forward a credential; nothing does it by
accident. A `Content-Type` header with a body becomes the `StringContent` media type; without a body
it is ignored.

### Response

- Response headers are filtered to `Content-Type`, `Content-Language`, `ETag`, `Last-Modified`,
  `Cache-Control`, `Expires`, `Vary`, `Location`, `Retry-After` and any `X-` name.
  `Set-Cookie` and framing headers are never returned.
- One value per name, matching `PageRequest`'s dictionary contract.
- The body is read as bytes with a hard cap. The response stream is disposed as soon as the cap is
  hit; `Truncated` is set. `Content-Length` is not trusted to be small.
- The charset comes from `Content-Type`; unknown or missing charsets fall back to UTF-8. Binary
  payloads are decoded as text and are the page's problem; no binary API is offered.
- `AutomaticDecompression = All`, so the cap applies to decompressed bytes.

### Redirects and methods

- `AllowAutoRedirect = false`. Redirects are followed manually up to `MaxRedirects`.
- Each `Location` is resolved against the current URI and the resulting `Uri` is re-checked against
  the allow-list and the scheme rule. A redirect to a host that is not allowed returns the 3xx
  response with `Failure = NotAllowed`; no connection is made to the new host.
- `301`/`302`/`303` on a `POST` becomes a `GET` with no body; `307`/`308` preserves the method and
  body. That is the browser convention and the least surprising one.
- Exceeding `MaxRedirects` returns the last 3xx with `Failure = TooManyRedirects`.

### Timeouts and abandonment

`PageFetcher` creates a linked `CancellationTokenSource` from the caller's token (which already
combines `IContext.Aborted` and the render deadline) and `Fetch.TimeoutSeconds`. Classification is
by which token fired:

- caller aborted → `Aborted`
- caller's render deadline → `Timeout`
- the per-fetch timeout → `Timeout`
- `HttpRequestException`, DNS failure, TLS failure → `Network`

`HttpClient.Timeout` is infinite; the linked token is the only timeout. The handler uses
`UseCookies = false`, `AllowAutoRedirect = false`, `AutomaticDecompression = All`, and a bounded
connection pool lifetime. `SimpleContext` refuses a call after `Abandon` and does not start one
whose token is already cancelled.

### Metrics and logs

`PageFetcher` records `page.fetch.duration` for the outcomes it owns and returns; `SimpleContext`
records the two it refuses before the fetcher is called (`limit` and a `HEAD` POST). `SimpleContext`
logs a refusal as a warning under the page category with the host, and a transport failure at
information. Neither logs the URL query, request headers, response headers or body. The page's own
`Log` calls remain the place for page-specific detail.

## Wiring

### `Program`

- Replace `AddDbContext<StorageDbContext>` with `AddDbContextFactory<StorageDbContext>` (the EF
  convenience overload also registers the scoped context the existing stores inject). The factory
  is what lets a page's storage call own its context and outlive the request scope safely.
- Register `PageStorageOptions.FromConfiguration`, `PageFetchOptions.FromConfiguration`,
  `IPageStateStore`/`SqlitePageStateStore`, and `IPageFetcher`/`PageFetcher` via
  `AddHttpClient<IPageFetcher, PageFetcher>`.
- Resolve `PageFetchOptions` before `RunAsync`, beside the `IPageCompiler` resolution, so an
  invalid `AllowedHosts` entry fails the deploy instead of the first page that fetches.
- Add `page.fetch.duration` to the OpenTelemetry `AddView` duration buckets, beside the render and
  compile durations.

### `PageEndpoint`

- Resolve `IPageStateStore`, `IPageFetcher` and `PageFetchOptions`.
- Build `PageStorageScope(path, store, writable: !HttpMethods.IsHead(...))`.
- Pass the scope, fetcher and request cap to `SimpleContext`.

### `PageRenderer` and `SimpleContext`

- `PageRenderer.RenderAsync` calls `context.AttachDeadline(deadline.Token)` before `RunAsync`, so a
  page's blocking calls are cancelled with the render rather than only with the request.
- `SimpleContext` gains `Storage`, `Fetch`, `AttachDeadline` and the abandoned checks. The
  `_abandoned` flag is read under the same lock as the body (or with `Volatile.Read`) so the page
  thread and the abandoning thread agree; the storage and fetch paths do not otherwise take the
  body lock.
- Tests that construct `SimpleContext` directly pass a fake `IPageStorage` and `IPageFetcher`; the
  `Runtime.Tests` `TestContext` implements the two new members with no-op values.

## Agent prompt and documentation

### Page-author prompt

`PageAuthorPrompt.Version` becomes `page-author-v2`. The `IContext` contract section already lists
every member by reflection, so `Storage` and `Fetch` appear automatically, but the model needs the
semantics. `Describe` also needs to render generic parameters, `out` parameters and nullable
returns; otherwise `TryGetValue` reads without its `<T>` and the page-author model loses the type
argument that makes the store useful.

- A `## Page storage` section: per-page, shared by all visitors, JSON values, keys, caps, deletion,
  last-write-wins, `HEAD` writes refused. It must also say that a missing key is `false` with the
  default, a stored null is `true` with null, and `TryGetValue` needs a type: `out int visits` or
  `TryGetValue<int>(...)`, because `out var` cannot infer the type parameter.
- An `## Outbound fetch` section: allow-listed hosts only, `GET`/`POST`, failures are values,
  `HEAD` cannot POST, no secrets in URLs or logs, timeout and body cap.
- The policy section gains `System.Text.Json` and keeps `System.Net` unavailable, pointing at
  `ctx.Fetch`.
- The safety line changes from "those tools do not exist" to "you cannot fetch directly; page code
  may call `ctx.Fetch` only for allow-listed hosts, and must not use it to exfiltrate secrets".
- Two new examples: a stored counter and a JSON fetch. Both must compile through the real page
  compiler, as the existing example test does. Generated source has no implicit usings, so an
  example that needs `Dictionary` either qualifies it or adds a `<%! using ...; %>` declaration.

### Documentation

On implementation, update:

- `docs/architecture.md`: the page contract list, a storage section, an outbound fetch section,
  the page API policy note, the source storage note, and the instrumentation table.
- `docs/decisions.md`: add DEC-115 onwards for the page-scope/JSON contract, the `PageState`
  lifecycle, the synchronous deadline-bound design, the default-off allow-listed fetch, the
  failure-as-value shape, and the `System.Text.Json` widening.
- `deploy/README.md`: the database now carries page state, so backups include it; the fetch
  allow-list is an environment/config change (`Pages__Fetch__AllowedHosts__0`, `__1`, ... in an
  environment file) and needs a restart;
  the container's egress is not otherwise restricted.
- `README.md`: the page API paragraph names `ctx.Storage` and `ctx.Fetch`.
- `docs/testing.md` only if the suite split changes; it should not.

## Implementation phases

Test-first throughout, one commit per phase. Start from a green `make check`. Every new file carries
its SPDX header; `make license-check` is part of the gate.

### Phase 1 — Contract and policy

1. Add the SDK files (`IPageStorage`, `PageFetchRequest`, `PageFetchResponse`, `PageFetchMethod`,
   `PageFetchFailure`) and the two `IContext` members, with XML docs.
2. Add the JSON types to `PageApiPolicy`; bump `PolicyVersion` to `2` and fix the "tightens" comment.
3. Give `SimpleContext` placeholder implementations (a disabled fetcher, an empty storage) so the
   tree compiles; replace them in phases 2 and 3.
4. Update `Runtime.Tests` `TestContext`, `PageHarness`, `SimpleContextTests` construction and any
   other `IContext` implementer.
5. Tests: a page using `Storage`/`Fetch` compiles; `HttpClient` still gets `PAGE001`; each JSON type
   is allowed; the SDK types are still referenced.

### Phase 2 — Storage

1. Add `PageStateEntry`, `StorageDbContext` mapping and the `AddPageState` migration
   (`make migration NAME=AddPageState`; make the generated migration class `internal`, as the
   visibility rule requires).
2. Add `PageStorageOptions`, `IPageStateStore`, `PageStorageScope`, `SqlitePageStateStore`, and the
   `Pages:Storage` section in `appsettings.json`.
3. Wire `AddDbContextFactory`, the store and the refresh in `Program`; create the scope in
   `PageEndpoint`; bind `SimpleContext.Storage`; attach the deadline in `PageRenderer`.
4. Delete state in `DeletePageAsync`/`ScrubPageAsync`; include it in `PageStorageSampler`.
5. Tests: round-trips for scalar, record, list and enum values; missing key returns `false` with the
   default; a stored null round-trips and `TryGetValue` returns `true` with null; keys and delete;
   page isolation; every cap; bad JSON and a wrong-shape value throw rather than report absent;
   write on `HEAD`; delete/scrub clears; a write to a deleted page is refused; last-write-wins;
   sampler payload includes state.

### Phase 3 — Fetch

1. Add `PageFetchOptions`, `IPageFetcher`, `PageFetcher`, request/response header filters, and the
   `Pages:Fetch` section in `appsettings.json`.
2. Wire the typed `HttpClient`, the fetcher and `SimpleContext.Fetch`; add the per-render request
   counter, the `HEAD` rule, metrics and logging.
3. Add the OpenTelemetry view for `page.fetch.duration`.
4. Tests: allow-listed success; disallowed host without a request; invalid URL/scheme/credentials;
   GET body; POST body and headers; header CRLF; response header filtering; 4xx/5xx →
   `HttpError` with the real status (500 included); redirects to allowed and disallowed hosts; 303 →
   GET; timeout; network error; truncation; request cap; `HEAD` POST; metrics; abandonment.

### Phase 4 — Prompt, docs and job closure

1. Update `PageAuthorPrompt` to v2, its examples and its tests; assert the contract shows
   `TryGetValue<T>` (including its `out` parameter) and `Set<T>`, and that the storage/fetch
   sections are present.
2. Update architecture, decisions, README, deployment notes and IDEA-009.
3. Move IDEA-002 and IDEA-003 to the closed list in `docs/jobs/README.md`, delete their files into
   git history, and record the new DEC entries. (The plan is the behavior record from then on.)

## Test matrix

| Area | Test | Suite |
| --- | --- | --- |
| Contract | A page compiles with `ctx.Storage` and `ctx.Fetch` | `Web.Tests` (`PageScopeTests`) |
| Policy | `System.Net.Http.HttpClient` is still `PAGE001` | `Core.Tests`, `Web.Tests` |
| Policy | Each allowed `System.Text.Json` type compiles; a serializer attribute is still denied | `Core.Tests`, `Runtime.Tests` |
| Storage | Value round-trips; missing key returns false/default; keys are ordinal | `Web.Tests` |
| Storage | A stored null returns true with null; an absent key returns false | `Web.Tests` |
| Storage | Pages cannot read or write each other's keys | `Web.Tests`, integration |
| Storage | Key/value/count/total caps refuse the write and leave no row | `Web.Tests` |
| Storage | Bad JSON and a wrong-shape value throw rather than reporting absent | `Web.Tests` |
| Storage | A write on `HEAD` throws | `Web.Tests` |
| Storage | Delete tombstones and clears; scrub is idempotent; recreate starts empty | `Web.Tests` |
| Storage | A write from an already-running render cannot resurrect a deleted page | `Web.Tests` |
| Storage | State survives an edit and a restore | Integration |
| Storage | Payload gauge includes state; sampler query is exact | `Web.Tests` (`PageStorageMetricsTests`) |
| Fetch | Allowed success, status, body, filtered headers | `Web.Tests` |
| Fetch | 4xx and 5xx responses are `HttpError` with the real status; 500 included | `Web.Tests` |
| Fetch | Disallowed, invalid, GET body, header CRLF: no socket, value failure | `Web.Tests` |
| Fetch | Redirect rules, revalidation of each hop, 303/307 behaviour | `Web.Tests` |
| Fetch | Timeout, network, body cap/truncation, request cap | `Web.Tests` |
| Fetch | `HEAD` + POST is refused; GET is allowed | `Web.Tests` |
| Fetch | Per-render counter and `page.fetch.duration` tags | `Web.Tests` (`PageMetricsTests`) |
| Wiring | A real stored page reads/writes state across requests | Integration |
| Wiring | A real stored page calls fetch through the configured fetcher | Integration (fake `IPageFetcher`) |
| Prompt | Storage/fetch sections exist and examples compile | `Web.Tests` (`PageAuthorPromptTests`) |

The integration fetch test replaces `IPageFetcher` in `ConfigureTestServices`; the real fetcher is
proven by the unit tests with `StubHttpMessageHandler`. Starting a local HTTP listener would add a
port and a flaky dependency for no extra proof.

## Risks and open questions

- **Synchronous blocking.** A page that ignores cancellation can still hold a thread after the
  render is abandoned. That is the existing budget behaviour, and the container's memory/pid limits
  are the backstop. The plan does not add a second worker model.
- **Shared state.** Every visitor sees the same keys. A guestbook or counter is fine; per-user data
  needs authentication inside pages and is out of scope.
- **Counters are racy.** Last-write-wins is documented. An atomic increment would be a different
  storage primitive, and SQL is deliberately out of scope.
- **Caps under concurrency.** The single-statement check is the strongest simple guarantee SQLite
  offers; a busy writer fails rather than silently overrunning. A future measurement can decide
  whether a retry is worth adding.
- **Fetch egress.** The allow-list is configured by the operator, not the page. DNS rebinding,
  private addresses and per-host rate limits are not addressed here; SEC-03 remains the process
  boundary if page authors ever stop being the owner.
- **JSON type changes.** A page that changes its stored shape makes old values unreadable; that is
  the page's responsibility, and the old value is not lost, just not deserialisable by the new type.
- **First request after deploy.** Every stored image recompiles because `IContext` changed. This is
  already documented in `deploy/README.md`; the plan repeats it so it is not mistaken for a
  regression.
- **Export/import.** IDEA-009 must be updated before it ships; a page archive without state is not
  a round trip.
- **Wildcards.** No `*.example.com` or port-qualified entries. A future widening is a reviewed
  change with its own matching tests.
- **Bindings.** `TryGetValue<T>` throws on a stored value of the wrong shape rather than returning
  `false`; a false negative would say "missing" when the key is there. Silent corruption is worse
  than a clean page error, and the plan keeps that behaviour.

## Folding the jobs

- [IDEA-002](../jobs/IDEA-002.md) and [IDEA-003](../jobs/IDEA-003.md) each gain a `## Status`
  section pointing here. Their idea and sketch are retained as the original context.
- `docs/jobs/README.md` links both entries to this plan, and items 5 and 6 of the rough order become
  one item: fetch and storage are planned together, with storage first because it is the smaller
  independent slice and fetch's integration test depends on the same context plumbing.
- [IDEA-009](../jobs/IDEA-009.md) gains one line: the archive must include page state once storage
  exists, and import must validate it with the same caps.
- On completion, both files are deleted into git history and their row moves to the closed table,
  following the IDEA-004 convention. IDEA-008 stays as the exception it records because its plan is
  the detailed behavior record; here the plan is, and the job files are only the original idea.
