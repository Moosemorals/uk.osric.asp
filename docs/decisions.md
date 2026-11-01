<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Decisions

The choices that shape this site, in one place. Each entry is a decision, not a plan: the linked
document is authoritative for the detail, and the entry exists so the *why* is findable without
reading every plan.

Most of these were settled in conversation and then recorded in a plan, a doc comment or a config
value. Where the only record used to be a chat message or a code comment, the entry below is now the
record. Cite the ID (`DEC-41`) in a commit or a plan when changing the behaviour it names.

A decision change is a deliberate edit to this file plus the code it names — not a silent drift. If
an entry stops being true, move it to [Superseded](#superseded) with the date and what replaced it.

## Product and scope

| ID | Decision | Recorded in |
| --- | --- | --- |
| DEC-01 | The product is a page that can edit itself: the 1999 textarea-and-save CGI experience, with compilation, autocomplete and real error messages instead of Perl. Everything else is in service of that. | [README](../README.md) |
| DEC-02 | Pages are public; only the editor is behind a sign-in. The site is a shop window, not a members' area. | [architecture](architecture.md#page-contract) |
| DEC-03 | One owner. The `Users` row **is** the authorisation: any account is an owner, and there are no roles. No registration, no forgot-password, no user UI. | [editor-authentication](plans/editor-authentication.md#2-accounts-are-managed-by-a-cli-never-by-the-site) |
| DEC-04 | Accounts are created and changed only from the command line (`users add/passwd/list/remove`, wrapped by the `user-*` and `remote-user-*` make targets), using the same binary, database and migrations as the site. | [editor-authentication](plans/editor-authentication.md#2-accounts-are-managed-by-a-cli-never-by-the-site) |
| DEC-05 | No contributions. The repository is published to be read and adapted, not collaborated on, and the GitHub Issues/PR tabs are off. | [CONTRIBUTING](../CONTRIBUTING.md) |
| DEC-06 | The root page is a page like any other: `/edit/` edits it, and nothing special-cases `/`. | conversation, 2026-10-31 |
| DEC-07 | Work is tracked as jobs: one file per job, `IDEA-` for opportunities and `SEC-`/`BUG-`/`OPS-`/`ARCH-` for defects. **Size** is the scheduling column; small and self-contained work goes first because it never gets scheduled otherwise. | [jobs](jobs/README.md) |
| DEC-08 | Speculative product work — AI authoring, page-declared types, `IContext` fetch and state, a richer editor — is backlog (IDEA-001 … IDEA-008), not a licence to expand the current task. | [jobs](jobs/README.md) |
| DEC-09 | A plan is written before a large change and kept as the record of the reasoning, including the options that were rejected. Plans are not rewritten into marketing copy afterwards. | [plans](plans/) |

## Architecture and code placement

| ID | Decision | Recorded in |
| --- | --- | --- |
| DEC-10 | Functional core, imperative shell. `Core` holds pure functions and values and references nothing; `Runtime` holds Roslyn and assembly loading; `Web` holds endpoints, hosting and storage. Data goes in and values come back — the core is never handed a service, logger, database context or `HttpContext`. | [architecture](architecture.md#functional-core-imperative-shell) |
| DEC-11 | Nothing is `public` unless another product assembly needs it; tests reach internals through `InternalsVisibleTo`. EF-generated migrations are edited back to `internal`. | [architecture](architecture.md#visibility) |
| DEC-12 | Minimal endpoints only: no MVC controllers and no Razor views. | [instructions](../.github/copilot-instructions.md) |
| DEC-13 | `record` for immutable values, `class` for behaviour, `struct` only for small immutable values where allocation or identity is measured to matter. | [architecture](architecture.md#shapes) |
| DEC-14 | `n` is small: prefer the obvious loop, the linear scan and the in-memory dictionary over clever structures or caches, and optimise only what a measurement shows is slow. | [architecture](architecture.md#simplicity-first) |
| DEC-15 | `System.*` and `Microsoft.*` first; write the rest ourselves. A third-party package needs a justification and a pinned version in `Directory.Packages.props`; NUnit and its adapter are the sanctioned exception. | [architecture](architecture.md#simplicity-first), [packages](../Directory.Packages.props) |
| DEC-16 | The `Makefile` is the only entry point. No parallel scripts, and no second definition of "green". | [instructions](../.github/copilot-instructions.md) |
| DEC-17 | `PagePath` normalises and nothing else. Reserved-name checks live in configuration and the editor, not in the path value. | conversation, 2026-10-31 |
| DEC-18 | A page key is a path, so it never contains `?` or `#`: the query string and fragment are not part of a page's identity. A request whose decoded route value contains one is answered `404` — the character is kept, never normalised away. Rewriting it to `_` would serve a different page than the URL asked for, and percent-encoding it would produce a key that can never match the decoded request path. | conversation, 2026-11-01; [PagePath](../src/uk.osric.asp.Core/PagePath.cs) |
| DEC-19 | Compile on save. A stored image is reused while its `ContentHash` and `RuntimeId` both match; anything else compiles lazily on its next request. There is deliberately no startup warm-up. | [architecture](architecture.md#compiled-pages) |
| DEC-20 | A diagnostic Roslyn reports in *generated* code, with none explaining it in *author* code, is a compiler fault: the compiler throws, the source is preserved and the host answers `500`. It is never shown to the author as a page error. | [architecture](architecture.md#functional-core-imperative-shell), conversation 2026-10-31 |
| DEC-21 | Author-facing diagnostic codes (`TEMPLATE001`, `PAGE001` … `PAGE004`) are allocated once and never reused, because the editor and the logs quote them. | [architecture](architecture.md#functional-core-imperative-shell) |
| DEC-22 | Positions are template space: the line and column the author typed. Generated-source coordinates never leave Roslyn, and an unknown position is `null`, never `(0, 0)`. | [compiler-diagnostics](plans/compiler-diagnostics.md#decisions) |
| DEC-23 | A warning never stops a page compiling. `Success` is "no diagnostic is an error", and a warning-only page is saved with its image and reported as "Saved with warnings." | [compiler-diagnostics](plans/compiler-diagnostics.md#decisions) |
| DEC-24 | A page's log category is `uk.osric.asp.Pages.<path>`. That spelling is operator-facing — filters and alerts are written against it. | [architecture](architecture.md#page-contract) |
| DEC-25 | `ContentFingerprint` is an opaque SHA-256 in lowercase hexadecimal, with no algorithm marker and no hash-name prefix. It is compared, never parsed; changing the algorithm changes the assembly, which changes `RuntimeId`. | [ContentFingerprint](../src/uk.osric.asp.Core/ContentFingerprint.cs) |
| DEC-26 | `RuntimeId` is `{format}:{policy}:{Core mvid}`, so a tighter page policy or a new page contract invalidates every stored image instead of running code it did not produce. | [TemplateCompiler](../src/uk.osric.asp.Runtime/TemplateCompiler.cs) |
| DEC-27 | No defensive code "just in case": `MethodImplOptions.NoInlining` was removed rather than kept, and a test pins the behaviour it was assumed to provide. | [page-runtime-hardening](plans/page-runtime-hardening.md#decisions-already-made) |
| DEC-28 | Redirects are one helper, not a family of ad-hoc status codes: `Redirects` takes the destination as a single relative-or-absolute string and encodes its path, query and fragment separately, so a query is never escaped into the path. `Permanent` (`308`) and `SeeOther` (`303`) are thin wrappers over it, and a canonical redirect is `308` so the method and body survive. | conversation, 2026-11-01; [Redirects](../src/uk.osric.asp.Web/Redirects.cs) |
| DEC-29 | Delete speculative code rather than carry it (`ListFilesAsync`), and defer known-but-unproven problems explicitly (route shadowing) instead of half-solving them. | conversation, 2026-10-31 |

## Page contract and threat model

| ID | Decision | Recorded in |
| --- | --- | --- |
| DEC-30 | A page is a runtime, not a renderer. `IContext` is the whole seam; every value crossing it is a plain record in `Core`, and no ASP.NET type reaches page code. | [architecture](architecture.md#page-contract) |
| DEC-31 | The response body is buffered. Status, content type and headers are applied after `Handle` returns, so a page that throws sends the error page rather than half a document. | [architecture](architecture.md#page-contract) |
| DEC-32 | Page headers are an allow-list: `Cache-Control`, `Content-Language`, `ETag`, `Expires`, `Vary` and any `X-` name. `Set-Cookie`, `Location`, `Content-Type` and framing headers are refused, so a page cannot clobber the editor's cookies; redirects go through `Redirect`, content type through `SetContentType`. | [architecture](architecture.md#page-contract) |
| DEC-33 | Pages answer `GET`, `POST` and `HEAD`; every other verb is `405` from routing. | [architecture](architecture.md#page-contract) |
| DEC-34 | A request form body over 1 MiB is refused with `413` before it is read; the buffered response is capped at 4 194 304 characters (`Pages:Render:MaxBodyChars`) so a loop fails quickly instead of growing the process. | [MainEndpoint](../src/uk.osric.asp.Web/Endpoints/MainEndpoint.cs), [appsettings](../src/uk.osric.asp.Web/appsettings.json) |
| DEC-35 | The page API is a default-deny allow-list held as data in `Core`. `System.Object` is deliberately absent because `GetType()` would hand a page reflection; `System.IO`, `System.Net`, reflection, threading beyond the cancellation token, regex and everything in `Microsoft.*` or our own assemblies are denied. `System.*Exception` is allowed by suffix so catching one needs no entry. | [architecture](architecture.md#page-api-policy), [PageApiPolicy](../src/uk.osric.asp.Core/PageApiPolicy.cs) |
| DEC-36 | Page shape is one type, `GeneratedPage`, with one method, `Handle`. `stackalloc`, `dynamic`, pointers, function pointers and `unsafe` are rejected, which is what stops a page closing the method to declare statics, its own entry point or a module initialiser. | [architecture](architecture.md#page-api-policy) |
| DEC-37 | The compiler's reference set is trimmed to the base class library, so naming the shell, Roslyn or `Microsoft.*` fails with CS02xx before the policy walk runs. | [architecture](architecture.md#page-api-policy) |
| DEC-38 | A post-emit scan of the image's type references was considered and dropped: it could only find what the semantic walk already reports, at the price of false rejections for compiler-emitted helpers. | [architecture](architecture.md#page-api-policy) |
| DEC-39 | `PageLoadContext` refuses the shell's assemblies and **throws** rather than returning `null`, because `null` means "ask the default context" — the exact escape the type exists to close. | [architecture](architecture.md#load-isolation) |
| DEC-40 | There is no sandbox claim. A page runs in-process with the site's identity and the whole BCL; isolation is fencing, not a boundary. The process boundary (SEC-03) is deliberately not built while the only page author is the owner. | [SEC-03](jobs/SEC-03.md) |
| DEC-41 | The threat model is "avoid obvious footguns", not "resist a determined adversary". The trims and the policy are worth their cost at that bar; an out-of-process worker is priced in the spike and stays unbuilt until the author model changes. | [page-runtime-hardening](plans/page-runtime-hardening.md), [aot spike](plans/aot-page-binary-spike.md) |
| DEC-42 | The execution budget bounds the *response*, not the page: a bounded asynchronous wait, then `504` with no body, `SimpleContext.Abandon`, and a counted, logged abandoned thread. Slots are released at the deadline because they bound concurrency, not work. | [page-execution-budget](plans/page-execution-budget.md#decisions) |
| DEC-43 | A render's deadline never cancels a compile. Throwing the work away would leave the page unservable, so the compile finishes and stores its image; the request answers `504` without starting the page. | [architecture](architecture.md#page-execution-budget) |
| DEC-44 | `504`, not `503`: the request was understood and the site failed to answer in time. `503` is left for "not ready", which this design has no state to report. | [page-execution-budget](plans/page-execution-budget.md#decisions) |
| DEC-45 | The health endpoint is `/heath` (deliberately not `/healthz`), returns detailed JSON, pings storage and reports abandoned renders — not CPU or memory, which belong to the container. | [OPS-02](jobs/OPS-02.md) |
| DEC-46 | The supervisor is the kill switch: the budget cannot reclaim a thread, so podman restarts a wedged container and `StartLimitBurst` stops a crash loop. That is the whole "hard boundary" in this design. | [architecture](architecture.md#health-and-restarts) |

## Editor authentication

| ID | Decision | Recorded in |
| --- | --- | --- |
| DEC-47 | Cookie authentication with local accounts: not ASP.NET Core Identity (no user/role stores, no UI, no EF schema) and not OIDC. `PasswordHasher<TUser>` comes from the shared framework, so the whole feature adds no NuGet package. | [editor-authentication](plans/editor-authentication.md#1-local-accounts-and-cookie-authentication-not-the-full-identity-stack) |
| DEC-48 | Password policy is a 12-character minimum and a 1024-character maximum, with no composition rules — a passphrase is the point, and the maximum bounds the hasher's work. New hashes use 600 000 PBKDF2-HMAC-SHA256 iterations; the stored count is honoured and rehashed on sign-in. | [editor-authentication](plans/editor-authentication.md#3-password-handling) |
| DEC-49 | Passwords are never accepted on the command line: echo-off prompt or standard input, so they do not reach shell history or `ps`. Plaintext exists only in the prompt, the request and the hasher call. | [editor-authentication](plans/editor-authentication.md#3-password-handling) |
| DEC-50 | Usernames are NFC-normalised, so the same name typed on different platforms is the same account. | conversation, 2026-10-31 |
| DEC-51 | Unknown usernames still verify against a fixed dummy hash and every failure returns one message, so the response does not reveal which accounts exist. | [editor-authentication](plans/editor-authentication.md#3-password-handling) |
| DEC-52 | The cookie is `Secure` only in production, so local development stays plain HTTP. | conversation, 2026-10-31; [AuthenticationExtensions](../src/uk.osric.asp.Web/Authentication/AuthenticationExtensions.cs) |
| DEC-53 | Sign-in lasts: a 30-day sliding ticket, and **Keep me signed in** checked by default (persistent cookie). Unchecked still carries the 30-day ticket but dies with the browser, for a shared machine. | [editor-authentication](plans/editor-authentication.md#4-long-term-sign-in-revocable-by-a-security-stamp) |
| DEC-54 | Revocation is a security stamp compared on every authenticated request. `users passwd` and `users remove` revoke every session; signing in again does **not**, so several devices can hold a valid session at once. | [editor-authentication](plans/editor-authentication.md#4-long-term-sign-in-revocable-by-a-security-stamp) |
| DEC-55 | Sign-in is throttled by one **global** fixed window of ten attempts a minute. Behind Caddy every request arrives from loopback, so a per-address partition would be one bucket with more moving parts; over-limit requests are answered `429` before any hashing. | [architecture](architecture.md#authentication-and-the-editor) |
| DEC-56 | Login, logout and editor POSTs require an antiforgery token (`400` before reading the form or touching storage). A successful save answers `303`, and the save message lives in the session, not a cookie. | [architecture](architecture.md#authentication-and-the-editor) |
| DEC-57 | The data-protection key ring is persisted on a volume, which is what lets a 30-day cookie and in-flight antiforgery tokens survive a redeploy. Deleting that volume signs everyone out. | [architecture](architecture.md#authentication-and-the-editor), [deploy](../deploy/README.md) |

## Operations and deployment

| ID | Decision | Recorded in |
| --- | --- | --- |
| DEC-58 | One Hetzner Cloud VM (`cpx02`, Debian 13) with rootless podman quadlets for the app and rootful Caddy as the only privileged unit. Caddy owns 80/443 and proxies to the app's loopback port. The host used to be a `cx22`; Hetzner no longer sells it. | [deploy](../deploy/README.md), [cloud-init](../deploy/cloud-init.yaml) |
| DEC-59 | Builds are reproducible: pinned base digest, `--timestamp` from the commit, the SDK never enters the image, and nothing in the image records when it was built. `make image-check` proves two builds of one commit match. | [deploy](../deploy/README.md) |
| DEC-60 | The app ships framework-dependent. A self-contained single file is smaller on disk but moves ~52 MB per release; trimming would reach ~29 MB and dies at startup, so trimming is deliberately off. | [deploy](../deploy/README.md#image-layout-and-size) |
| DEC-61 | The app publishes on the loopback only, and that is the control that makes trusting every proxy's forwarded headers safe. Do not widen `PublishPort` or add another proxy without revisiting it. | [deploy](../deploy/README.md#proxy-trust-and-forwarded-headers) |
| DEC-62 | No `UseHttpsRedirection` in the app: Caddy redirects HTTP to HTTPS, and doing it twice behind a proxy is a redirect loop waiting to happen. | [deploy](../deploy/README.md#proxy-trust-and-forwarded-headers) |
| DEC-63 | Releases travel through `registry.osric.uk` with an explicit `--authfile` on the server, because podman's default auth file lives under `/run/user` and is gone after a reboot. | [deploy](../deploy/README.md#the-registry) |
| DEC-64 | The container is capped (`--memory=768m`, `--pids-limit=256`, `--cpus=1`) and probed every 10 s with three failures allowed, so a runaway page takes the container and not the host, and a wedged process becomes a ~35 s outage. | [deploy](../deploy/README.md#resource-limits-and-restarts) |
| DEC-65 | **No cloud firewall and no automatic backups** — accepted policy choices for now, recorded rather than assumed. The host firewall is `ufw` default-deny; backups are a deliberate manual operation. Revisit deliberately rather than by accident. | conversation, 2026-10-31 |
| DEC-66 | No outbound alerting yet. Email (with SPF and friends) is OPS-05, so today a failure is discovered by visiting the site or reading the journal. | [OPS-05](jobs/OPS-05.md) |
| DEC-67 | `ufw` denies inbound by default and allows 22/80/443; unattended upgrades are enabled on the host. | [cloud-init](../deploy/cloud-init.yaml) |
| DEC-68 | The hostname and domain are public: they are in the repository and the Caddyfile. An earlier attempt to keep the domain out bought nothing, because robots find it anyway. | conversation, 2026-10-31 |
| DEC-69 | The quadlets and Caddyfile in `deploy/` are the source of truth. The host is a deployment target, never edited by hand. | [Caddyfile](../deploy/Caddyfile), [instructions](../.github/copilot-instructions.md) |
| DEC-70 | Backups use SQLite's online backup or a copy of the stopped volume. Copying live database files is refused because the WAL can hold committed data; a database backup is a credential store, because it holds password hashes and stamps. | [deploy](../deploy/README.md#backups) |
| DEC-71 | `make deploy` refuses a dirty tree and skips the image work when the host already runs the commit. `ALLOW_DIRTY=1` is the deliberate exception, not a default. | [Makefile](../Makefile) |
| DEC-72 | There is one gate: `make check`, run on demand and by `make deploy` and `make ci`. Nothing installs a pre-push hook: a minute-plus on every push cost more than it ever caught. No workflow files, no service, no secret — a machine with the repository, the SDK, `reuse` and podman can reproduce it. | [Makefile](../Makefile), [OPS-03](jobs/OPS-03.md) |
| DEC-73 | Commits are local; push only when asked. A finished task is a commit, not a push. | conversation, 2026-11-01 |

## Build, test and house style

| ID | Decision | Recorded in |
| --- | --- | --- |
| DEC-74 | Warnings are errors and code style is enforced in the build, not only in the gate, so what `make build` proves is what `make check` proves and a cached stamp can never mean a looser build. | [Directory.Build.props](../Directory.Build.props) |
| DEC-75 | `ContinuousIntegrationBuild` is on by default, so PDB paths do not record where the checkout happened to live. | [Directory.Build.props](../Directory.Build.props) |
| DEC-76 | Tests are NUnit. One behaviour per test, named for the behaviour, and written first. | [testing](testing.md) |
| DEC-77 | Anything a unit test can prove does not belong in a browser test; the integration suite exists for routing, middleware order, exception handling and startup migrations against the real host. | [testing](testing.md) |
| DEC-78 | A suite that wedges fails the gate rather than sitting in it: a generous whole-run ceiling (`--blame-hang`, 5 minutes), because the budget tests burn wall clock on purpose. | [Makefile](../Makefile) |
| DEC-79 | Every file carries SPDX tags (`make license-check`); files that cannot take a comment are listed in `REUSE.toml`. This repository is MIT, which is a deliberate departure from the ISC default in the shared C# instructions. | [REUSE.toml](../REUSE.toml), [LICENSE](../LICENSE) |

## Superseded

| Date | Was | Now | Why |
| --- | --- | --- | --- |
| 2026-10-31 | OIDC sign-in through a self-hosted provider | Local accounts and a cookie (DEC-47) | The provider was unreachable mid-plan; a single owner does not need federation |
| 2026-10-31 | Keep the domain and hostname out of the repository | Published (DEC-68) | It is discoverable anyway; hiding it only complicated the Caddyfile and the deploy |
| 2026-10-31 | `301` for a canonical redirect | `308` (DEC-28) | Permanence without dropping the method or body |
| 2026-10-31 | `PagePath` rejecting reserved names | Rejection moved to config and the editor (DEC-17) | A path value normalises; it does not arbitrate routing |
| 2026-10-31 | `MethodImplOptions.NoInlining` kept defensively | Removed, with a test pinning the stack frame (DEC-27) | It was not load-bearing; the compiler should do its job |
| 2026-10-31 | Suspicion that the security stamp allowed only one session | Multi-device by design (DEC-54) | Only a password change or account removal should revoke |
| 2026-11-01 | Hetzner `cx22` | Hetzner `cpx02` (DEC-58) | Hetzner no longer sells `cx22` |
| 2026-11-01 | A pre-push hook running `make check` on every push | The gate runs on demand (DEC-72) | The wait before every push was worse than the failures it caught; `make deploy` already gates a release |

## Open policy questions

Not decisions yet — recorded so they are not rediscovered:

- There is no `Content-Security-Policy` today. IDEA-001 notes that adding *any* script is the moment
  to add one, and that is a decision, not a default.
- Responses are buffered by DEC-31. Streaming (ARCH-13) would change that, and with it the "answer
  `504`, send nothing" guarantee.
- The process boundary (SEC-03 step 3) rests on the owner being the only author. If that premise
  changes, DEC-40 and DEC-41 change with it.
