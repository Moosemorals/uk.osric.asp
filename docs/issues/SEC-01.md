<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# SEC-01 — The editor is unauthenticated, so anyone can execute arbitrary code

| | |
| --- | --- |
| Severity | Critical |
| Area | Web, security |
| Location | `src/uk.osric.asp.Web/Program.cs:43`, `src/uk.osric.asp.Web/EditorEndpoint.cs` |

## Problem

`/edit/{**path}` is mapped with no authentication or authorisation. A `POST` writes the request
body to storage, and `MainEndpoint` compiles that text as C# and runs it in-process. Verified by
hand:

```bash
curl -X POST -d 'content=Hello from a page' http://host/edit/hello   # 302 Saved successfully.
curl http://host/hello                                               # Hello from a page
```

Anyone who can reach the site can therefore run code with the application's identity: read
`/data/storage.db`, environment variables, files, and the network, and rewrite every page. The
editor is the entire authorisation boundary for a runtime that executes uploaded code.

## Fix outline

1. Decide the trust model first. If only the owner writes content (the current intent), require
   authentication for both `GET` and `POST /edit/**`; do not rely on obscurity.
2. Cheapest option behind Caddy: `basic_auth` on the `/edit*` path in `deploy/Caddyfile`, with a
   hashed password in the Caddyfile. This is outside the app, so it cannot be bypassed by a routing
   mistake.
3. Better option, testable in the app: add authentication (for one user, a cookie scheme with a
   configured password hash) and `RequireAuthorization()` on the editor endpoint, plus a login
   endpoint. Keep the policy in the shell, not in `Core`.
4. Add a `[Authorize]`-equivalent test: anonymous `GET`/`POST /edit/**` returns 401/redirect and
   writes nothing.
5. If authentication is added, persist data-protection keys (mount a volume at
   `/home/app/.aspnet`); the deployment notes currently rely on ephemeral keys, which would
   invalidate every session on restart.
6. If content from anyone other than the owner is ever accepted, in-process compilation is the
   wrong design: move execution to a separate, restricted process or drop the feature.
