<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# OPS-01 — No forwarded-headers support, so HSTS is a no-op behind Caddy

| | |
| --- | --- |
| Severity | Medium |
| Area | Deployment, web host |
| Location | `src/uk.osric.asp.Web/Program.cs:36-40`, `deploy/Caddyfile` |

## Problem

Caddy terminates TLS and proxies to the app over plain HTTP on `127.0.0.1:8080`. The app never calls
`UseForwardedHeaders`, so it believes every request is `http` and `Host: 127.0.0.1:8080`:

- `UseHsts()` does nothing: the HSTS middleware only emits `Strict-Transport-Security` for HTTPS
  requests. Verified — a request with `X-Forwarded-Proto: https` got no STS header.
- `Request.Scheme` and `Request.Host` are wrong, which breaks any absolute URL, redirect or
  `Secure` cookie decision made from them (relevant to SEC-01/SEC-02).
- `Request.IsHttps` is false, so security decisions silently take the insecure branch.

The deployment is therefore less protected than the presence of `UseHsts()` suggests.

## Fix outline

1. Register `ForwardedHeadersOptions` with `ForwardedHeaders.XForwardedProto | XForwardedHost`, and
   `KnownProxies`/`KnownNetworks` limited to loopback (or clear the defaults and document that only
   Caddy can reach the port; the quadlet already publishes loopback-only).
2. Call `app.UseForwardedHeaders()` before `UseHsts`, `UseHttpsRedirection` and anything that reads
   the scheme.
3. Set `Caddyfile` `header` or rely on Caddy's default `X-Forwarded-Proto`; verify with `curl -I`.
4. Ensure Caddy redirects HTTP→HTTPS (it does by default for a named site with TLS).
5. Add an integration test that sends forwarded headers and asserts the STS header and
   `Request.Scheme == "https"`.
6. Update `deploy/README.md` with the trust assumption.
