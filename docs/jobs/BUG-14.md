<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-14 — A canonical redirect drops the query string

| | |
| --- | --- |
| Severity | Low |
| Area | Routing |
| Location | `src/uk.osric.asp.Web/Endpoints/MainEndpoint.cs:18-19`, `src/uk.osric.asp.Web/Endpoints/EditorEndpoint.cs:34-35` |

## Problem

Both GET handlers canonicalise the URL by redirecting to `PagePath.Normalize(...)`'s key and nothing
else, so the query string is discarded:

```csharp
PagePath path = PagePath.Normalize(context.Request.RouteValues["path"] as string);
if (!string.Equals(context.Request.Path.Value, path.Path, StringComparison.Ordinal)) {
    Redirects.Permanent(context, path.Path);
    return;
}
```

`Redirects.Permanent` sends `308` with a `Location` built from that path alone. A browser re-issues
the target exactly as given, so the parameters never arrive:

- `GET /Hello?q=metrics` → `308 Location: /hello`, and the page sees an empty `ctx.Request.Query`.
- `GET /search/?q=metrics` → `308 Location: /search`, same loss.
- `GET /edit/Hello` → `308 Location: /edit/hello`, which loses nothing today because the editor has no
  query, but is the same code shape and will surprise whoever adds one.

Pages are handed the query (`PageRequest` carries it, and `SimpleContext` snapshots the request), so
this is a silent behaviour change for any page whose links are not already canonical — an uppercase
first letter or a trailing slash is enough. It is also invisible in testing unless the case is
deliberate: the redirect looks correct, the destination serves `200`, and only the page's own output
differs.

What is **not** wrong: dropping the fragment is correct (fragments are never sent to the server), the
`308` choice is right (it preserves the method, so a canonicalising `POST` is not turned into a `GET`),
and the path canonicalisation itself is what stops one page existing under several keys.

## Fix outline

1. Teach the redirect the difference between a path and a query. Concatenating them is not enough,
   because `Redirects.Permanent` sends the whole string through `PathString`, which escapes `?`
   (verified: `new PathString("/hello?q=1").ToUriComponent()` is `/hello%3Fq=1`, which is a 404, not a
   redirect with parameters). Give the helper the query explicitly:

   ```csharp
   public static void Permanent(HttpContext context, string path, QueryString query = default) {
       context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
       context.Response.Headers.Location = new PathString(path).ToUriComponent() + query.ToUriComponent();
   }
   ```

   `QueryString.ToUriComponent()` is empty when there is no query, keeps the leading `?`, and does not
   re-encode, so `/hello` stays `/hello` and `?q=a%20b` stays `?q=a%20b`.
2. Pass `context.Request.QueryString` at both call sites, page and editor, or state in a comment why
   the editor deliberately does not.
3. Extend `NonCanonicalPathRedirectsPermanentlyAsync` in `MainEndpointTests` with the cases that
   decide it: a query survives (`/Hello?q=1` → `/hello?q=1`), no query means no `?`
   (`/Hello` → `/hello`), and an encoded value is not re-encoded or misplaced
   (`/Hello?q=a%20b` → `/hello?q=a%20b`, which is what catches the `%3F` mistake above).
4. Leave the redirect relative, as it is today: the target is a canonical path plus an existing query,
   so it cannot acquire an authority and cannot become an open redirect.

## Notes

- The alternative is to drop known tracking parameters (`utm_*`) as a convenience. The server cannot
  know which parameters a page cares about, so preserving everything is the honest default; anything
  cleverer belongs in the page, which can read the query and redirect itself.
- The redirect happens before the reserved-path check and before storage is read, so a non-canonical
  URL costs one response and no query. Preserving the query does not change that.
