<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-03 — The 404 response has no `Content-Type`

| | |
| --- | --- |
| Severity | Low |
| Area | Web |
| Location | `src/uk.osric.asp.Web/MainEndpoint.cs:17-19` |

## Problem

For a missing page, `MainEndpoint` sets only the status code and writes the string "File not found.":

```csharp
context.Response.StatusCode = 404;
await context.Response.WriteAsync("File not found.");
```

No `Content-Type` is set. Kestrel sends the body with no media type, so browsers sniff it and the
response is inconsistent with every other response from the site (which send `text/html; charset=utf-8`).

Verified: `curl -i /nope` shows no `Content-Type` header.

## Fix outline

1. Emit the 404 as HTML like every other page: `ContentType = "text/html; charset=utf-8"`, then the
   framework header/footer and a message.
2. Or use `Results.NotFound("File not found.")`, which sets `text/plain`.
3. Add a header assertion to `MainEndpointTests`.
