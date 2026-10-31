<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Editor authentication with local accounts

Implementation plan for SEC-01 (critical) and SEC-02 (high), both closed by this work. This replaces
the abandoned OIDC design: the provider is not available, so accounts and passwords live in this
application. There is **no** user management in the site. A small command line in the app creates the
account, changes its password, lists accounts and removes one; the web surface only has a sign-in
form and a sign-out button.

## Status

Implemented on 2026-10-31. Test counts at the time: 87 core, 32 runtime, 226 web, 40 integration.
One review change landed after the first pass: user names are NFC-normalised after case-folding, so
composed and decomposed spellings of the same accented name are one account.

## Threat model

Before this work, `POST /edit/{**path}` wrote source that `MainEndpoint` compiles and runs
in-process, with no authentication and no request-forgery check. Anyone who could reach the site
could therefore run C# as the site and read or rewrite everything it could.

After this work:

- **Anonymous requests cannot reach the editor.** Both `GET` and `POST /edit/**` require an
  authenticated cookie, and the only way to obtain one is the sign-in form with a local account.
- **Only accounts created from the command line exist.** There is no registration endpoint, no
  invitation flow and no account UI; the CLI is the gate. Any account in the table can edit, so the
  table is the owner list.
- **A page the owner visits cannot forge a sign-in or a save.** The login, logout and editor POSTs
  all carry an antiforgery token; a POST without one is answered `400` and writes nothing.
- **The owner does not sign in every day.** The auth cookie slides for 30 days of inactivity, and
  the login form keeps it across browser restarts by default. A password change or account removal
  revokes every existing cookie immediately.
- **A restart does not log the owner out.** Data-protection keys (auth, antiforgery and session
  cookies) are persisted on a volume.

Still out of scope: [SEC-03](../issues/SEC-03.md) and [ARCH-03](../issues/ARCH-03.md). An
authenticated page is still arbitrary C# in the site's process with no execution budget.
Authentication removes the "anyone" threat; it does not shrink what the owner's own code can do.

## Decisions

### 1. Local accounts and cookie authentication, not the full Identity stack

Use `Microsoft.AspNetCore.Authentication.Cookies` with a `Users` table in the existing SQLite
database. Do not add ASP.NET Core Identity's user/role stores, its UI, or its EF schema: none of the
features that justify it are wanted here.

- `PasswordHasher<TUser>` from `Microsoft.Extensions.Identity.Core` supplies the password hashing
  (PBKDF2-HMAC-SHA256 with a random salt and an embedded iteration count). It is part of the
  ASP.NET Core shared framework, so this whole plan adds **no NuGet package**.
- A simple `Users` table is enough. The row is the authorisation: the command line is the only
  writer, so any row is an owner.
- The alternative — a username and a single hash in configuration — makes password changes a
  deployment and is worse for a database that is already there.

### 2. Accounts are managed by a CLI, never by the site

The Web project gains a `users` command that runs before the HTTP host starts:

```bash
make user-add ACCOUNT=osric
make user-passwd ACCOUNT=osric
make user-list
```

Underneath, the app answers `users add`, `users passwd`, `users list` and `users remove`. The same
binary and the same `StorageDbContext`/migrations are used locally and in the container, so the
account a script creates is the account the site authenticates.

- No registration endpoint, no "forgot password" endpoint, no user list in the UI.
- `add` refuses an existing name; `passwd` refuses a missing one; `remove` is deliberate and prints
  what it removed.
- Passwords are read with echo off, or from standard input for scripts (`--password-stdin`). They
  are never accepted on the command line, so they do not land in shell history or `ps`.
- The same code path runs in the container:
  `podman exec -it uk.osric.asp dotnet uk.osric.asp.Web.dll users add osric`.

### 3. Password handling

- New hashes use `PasswordHasher<LocalUser>` with `IterationCount = 600_000`. Verification reads the
  count from the stored hash, so a later increase does not break old hashes.
- On a successful sign-in that returns `SuccessRehashNeeded`, the endpoint rewrites the stored hash
  with the current settings.
- A minimum length of 12 characters is enforced when a password is set. No complexity rules: a
  passphrase is the point. The maximum is 1024 characters, so a login cannot spend unbounded time
  in the hasher.
- Unknown usernames still run a verification against a fixed dummy hash, and every failure returns
  the same `Invalid username or password.` message, so the response does not reveal which accounts
  exist.
- The plaintext password exists only in the request, the CLI prompt and the hasher call. It is
  never logged, stored or echoed.

### 4. Long-term sign-in, revocable by a security stamp

The owner should not have to sign in every day, so the auth cookie is long-lived:

- `ExpireTimeSpan = TimeSpan.FromDays(30)` with `SlidingExpiration = true`. Any visit inside the
  window renews it, so routine use keeps the session alive indefinitely without a daily login.
- The login form has a **Keep me signed in** checkbox, checked by default. Checked, the cookie is
  persistent (`IsPersistent = true`) and survives browser restarts. Unchecked, it is a session
  cookie that still carries the 30-day ticket but dies when the browser closes, which is the right
  choice for a shared machine.
- A long-lived cookie needs revocation. `LocalUser` gains a `SecurityStamp`; the signed-in
  principal carries it as a claim, and `OnValidatePrincipal` compares it with the row on every
  authenticated request. `users passwd` rotates the stamp and `users remove` deletes the row, so the
  next request with an old cookie is rejected and the cookie is deleted. That is one small SQLite
  read per authenticated request; for a single owner it is cheaper than keeping a cache coherent.
- Signing in does **not** rotate the stamp. A second browser or device receives the same stamp as
  the first, so concurrent sessions are supported; only `users passwd` and `users remove` revoke
  every session.
- The cookie stays `HttpOnly`, `SameSite=Lax`, and `Secure` in production, and the sign-out button
  deletes it. The persisted data-protection keys are what let the 30-day window survive a redeploy.

### 5. Login is throttled

`POST /login` is behind a fixed-window rate limiter (10 attempts per minute). A rejected request gets
`429 Too Many Requests`. The partition is global rather than per IP: behind Caddy every request
arrives from `127.0.0.1`, and a single-owner site gains nothing from pretending otherwise. The
limiter is built in (`Microsoft.AspNetCore.RateLimiting`), so it is not a new dependency. The limit
is a code default registered as an options value; tests replace the options object (lower for the
throttling test, higher for the shared fixture), so no appsettings key is involved.

### 6. Antiforgery on every state-changing form

`AddAntiforgery` + `UseAntiforgery` validate the `login`, `logout` and editor POSTs. The editor and
login pages render the token with `IAntiforgery.GetAndStoreTokens`.

In .NET 10 the middleware does **not** short-circuit on failure: it records the result in
`IAntiforgeryValidationFeature` and calls the endpoint. Each POST handler checks that feature first
and returns `400` before reading the form, so "rejected and writes nothing" is a real test rather
than a 500.

### 7. The editor's flash message becomes session state

The `message` cookie goes away. A save stores the message in the session and answers
`303 See Other`; the next `GET /edit/{path}` reads and removes it. Login errors are rendered
directly on the login page, so they need no flash state at all.

### 8. Persist data-protection keys

The auth, antiforgery and session cookies all use data protection. Mount a volume at
`/home/app/.aspnet/DataProtection-Keys` and call
`AddDataProtection().SetApplicationName("uk.osric.asp")`, so a redeploy does not invalidate every
session and every in-flight form.

### 9. No new dependencies

Cookie authentication, `PasswordHasher`, antiforgery, session and rate limiting are all in the
ASP.NET Core shared framework. This plan touches `Web` only; `Core` and `Runtime` stay untouched.

## Data model

New file `src/uk.osric.asp.Web/Users/LocalUser.cs`:

```csharp
public sealed class LocalUser {
    public required string UserName { get; init; }   // normalized, primary key
    public required string PasswordHash { get; init; }
    public required string SecurityStamp { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
```

`StorageDbContext` gains:

```csharp
modelBuilder.Entity<LocalUser>().ToTable("Users");
modelBuilder.Entity<LocalUser>().HasKey(user => user.UserName);
modelBuilder.Entity<LocalUser>().Property(user => user.UserName).HasMaxLength(64).UseCollation("BINARY");
modelBuilder.Entity<LocalUser>().Property(user => user.PasswordHash).HasMaxLength(256);
modelBuilder.Entity<LocalUser>().Property(user => user.SecurityStamp).HasMaxLength(128);
```

`SecurityStamp` is 32 random bytes, base64. `SecurityStamp.Create()` in `Users` is the only
generator; `users add` and `users passwd` call it, and nothing displays it.

- The primary key is the normalized user name, and the collation is `BINARY` because normalization
  already case-folds it. There is no separate display name.
- `UserName.Normalize` is a pure rule: trim, `ToLowerInvariant`, then Unicode NFC, and reject
  empty/whitespace, over 64 characters or control characters. The CLI and the login endpoint both
  call it, so `Osric`, `osric`, ` osric ` and the composed and decomposed spellings of the same
  accented name are one account.
- The migration is generated with `make migration NAME=AddLocalUsers` and applied by both the web
  host and the CLI.

## Password and account services

New files under `src/uk.osric.asp.Web/Users/`:

- `PasswordPolicy.cs` — the 12/1024 length rule and its message. Pure, so it is tested directly.
- `IPasswordService.cs` / `PasswordService.cs` — `Hash(LocalUser, string)`,
  `Verify(LocalUser, string)` and `VerifyDummy(string)`; wraps `PasswordHasher<LocalUser>`,
  configures the iteration count, and holds the dummy hash used for unknown users.
- `SecurityStamp.cs` — `Create()`, the single generator for the revocation stamp.
- `IUserStore.cs` / `SqliteUserStore.cs` — `FindAsync`, `AddAsync`, `UpdateHashAsync` (a rehash,
  keeps the stamp), `ChangePasswordAsync` (hash plus a fresh stamp, which signs out every other
  session), `ListAsync` and `RemoveAsync` over `StorageDbContext`. Every method takes a
  `CancellationToken`; reads are `AsNoTracking`.
- Register `IPasswordService` as a singleton and `IUserStore` as scoped next to `IStorage`.

`SqliteUserStore` is tested against a real SQLite file, the way `SqliteStorageTests` is: add and
find, case-insensitive lookup, duplicate rejection, password replacement, list ordering and remove.

## Configuration

There are no new configuration keys. The storage connection string is the only input, and the CLI
and the web host resolve it through the same code. The login rate limit has a code default (10
attempts per minute) and a test-only options override. There is no provider or client secret to
configure; the password hash lives in the database and the password only ever exists at the CLI
prompt.

## The `users` command

New file `src/uk.osric.asp.Web/Users/UserCommands.cs`, plus a tiny console seam so the command logic
is testable without a terminal.

`Program.Main` recognises the command before the web host is configured, and strips the arguments so
the command line configuration provider never sees a bare `users`:

```csharp
bool isUserCommand = UserCommands.IsCommand(args);
WebApplicationBuilder builder = WebApplication.CreateBuilder(
    isUserCommand ? Array.Empty<string>() : args);
```

After the existing migrate-on-startup block:

```csharp
if (isUserCommand) {
    await using (app) {
        Environment.ExitCode = await UserCommands.RunAsync(
            args[1..], app.Services, app.Lifetime.ApplicationStopping);
        return;
    }
}
```

`Main` stays `async Task`; the command's exit code goes through `Environment.ExitCode`.

- The command resolves `IUserStore` and `IPasswordService` from a fresh scope; migrations have
  already run, so a brand-new database works on the first command.
- `add <name>`: normalize, refuse a duplicate, read the password twice with echo off (or once from
  stdin with `--password-stdin`), enforce `PasswordPolicy`, hash, create a `SecurityStamp` and
  insert.
- `passwd <name>`: same password flow, change the hash **and** rotate the stamp, so every existing
  long-term cookie for that account stops validating on its next request.
- `list`: print normalized names and `CreatedAt`; never print hashes or stamps.
- `remove <name>`: delete the row and print the result; existing cookies stop validating because
  the row is gone.
- Exit codes: `0` success, `1` a user-facing failure (duplicate, missing, mismatched passwords,
  policy rejection), `2` bad usage. Errors go to stderr; no stack traces.
- The console seam is an interface with `ReadLine`, `ReadPassword` and `Write`/`WriteError`; the real
  implementation wraps `Console`, and tests use a scripted fake.

Makefile targets, each depending on `build` and forwarding the Development environment:

```make
user-add: build
	ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) \
		$(DOTNET) run --project $(WEB_PROJECT) --no-build -- users add "$(ACCOUNT)"
```

`user-passwd`, `user-list` and `user-remove` follow the same shape. `ACCOUNT` is checked for
emptiness first, like `migration` does for `NAME`.

## Authentication and authorisation

### Cookie

New file `src/uk.osric.asp.Web/Authentication/AuthenticationExtensions.cs`:

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.Cookie.Name = ".uk.osric.asp.auth";
        options.Cookie.HttpOnly = true;
        // Lax, so following a link from elsewhere still arrives signed in. Strict would not.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.ReturnUrlParameter = "returnUrl";
        // The owner does not want a daily sign-in. Visiting once inside the window renews it.
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        // A policy failure is a 403, not a redirect loop through the login page.
        options.Events.OnRedirectToAccessDenied = context => {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        // A long-lived cookie must be revocable: a password change or a deleted user stamps it out.
        options.Events.OnValidatePrincipal = async context => {
            string? userName = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            LocalUser? user = userName is null ? null : await context.HttpContext.RequestServices
                .GetRequiredService<IUserStore>()
                .FindAsync(userName, context.HttpContext.RequestAborted);
            string? stamp = context.Principal?.FindFirstValue(SecurityStampClaim);

            if (user is null || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)) {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
```

`SecurityStampClaim` is a `const string` next to `EditorPolicy` (for example
`"uk.osric.asp.security_stamp"`), so the login endpoint and the cookie event cannot drift. The
default authenticate, challenge and sign-in schemes are all the cookie scheme, so
`RequireAuthorization()` produces a `302` to `/login?returnUrl=<current path>` for an anonymous
browser.

### Login and logout endpoints

New file `src/uk.osric.asp.Web/Endpoints/LoginEndpoint.cs`:

- `GET /login` is anonymous. If the request is already authenticated, redirect to the safe return
  URL or `/edit`. Otherwise render the form with `Framework.GetHeader`/`GetFooter`, a hidden
  antiforgery field, the `returnUrl`, and a **Keep me signed in** checkbox checked by default (the
  long-term cookie is the normal case; unchecking is for a shared machine).
- `POST /login` is anonymous and rate limited:
  1. Check `IAntiforgeryValidationFeature`; `400` if invalid.
  2. Reject a body that is not a form (`HasFormContentType`) with `400`.
  3. Read `username`, `password` and `returnUrl`.
  4. Normalize the name and look it up. A name that fails normalization (empty, too long, control
     characters) takes the same generic failure path as an unknown user; it must not throw. If the
     user is missing, verify against the dummy hash.
  5. On success, sign in a principal carrying `ClaimTypes.Name`, `ClaimTypes.NameIdentifier` and
     the row's `SecurityStamp`. Set `IsPersistent` from the checkbox: checked gives a persistent
     30-day sliding cookie, unchecked gives a session cookie with the same ticket window. Rehash
     with `UpdateHashAsync` if `SuccessRehashNeeded`; that keeps the stamp, so it does not sign the
     current session out.
  6. Redirect `303` to a safe local `returnUrl`, else `/edit`.
  7. On failure, re-render the form with `Invalid username or password.` and `200`. A malformed
     stored hash counts as a failed verification, not a `500`. Never put the password in the URL or
     the log.
- `POST /logout`: antiforgery, `SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)`,
  `303` to `/`. The editor page carries a small sign-out form using the same page token.
- The login form and the editor both answer `Cache-Control: no-store`: the first can contain an
  attempt's error, the second contains source that only the owner may see.

`LocalRedirect.IsSafe` is a small pure helper: only a single leading `/`, no `//`, no backslash, no
control characters, so `https://evil`, `//evil` and `/\evil` all fall back to `/edit`. It is tested
directly and used by both the GET and POST paths.

### Policy and routes

```csharp
public static class EditorPolicy {
    public const string Name = "Editor";
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(EditorPolicy.Name, policy => policy.RequireAuthenticatedUser());

app.MapGet("/edit/{**path}", EditorEndpoint.Handler).RequireAuthorization(EditorPolicy.Name);
app.MapPost("/edit/{**path}", EditorEndpoint.Handler)
    .RequireAuthorization(EditorPolicy.Name)
    .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
app.MapGet("/login", LoginEndpoint.GetAsync).AllowAnonymous();
app.MapPost("/login", LoginEndpoint.PostAsync).AllowAnonymous().RequireRateLimiting("login");
app.MapPost("/logout", LoginEndpoint.LogoutAsync).WithMetadata(new RequireAntiforgeryTokenAttribute(true));
```

- `ReservedPaths.Default` gains `/login` and `/logout`; `appsettings.json` is updated to match.
- Pages remain public. Only `/edit/**`, `/login` and `/logout` are outside the page catch-all, and
  `/login`/`/logout` are reserved so a page can never shadow them.
- The `Editor` policy is authentication only. The account table is the owner list; a second account
  created with `users add` is intentionally a second editor.
- `EditorEndpoint` drops the `IWebHostEnvironment` parameter (no more cookie construction) and gains
  `IAntiforgery`. Its signature becomes
  `(HttpContext, PageService, ReservedPaths, IAntiforgery)`.

### Pipeline order

```csharp
app.UseForwardedHeaders();
app.UseSession();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
```

`UseSession` and `UseAntiforgery` arrive with their registrations in Phase 3; leave them out until
then. Forwarded headers stay first so the cookie `Secure` decision sees the browser's scheme. The
rate limiter needs the endpoint metadata, so it comes after `UseRouting`.

## Antiforgery and the flash message

Phase 3 is SEC-02. The endpoint parts are already listed; the remaining work is:

- `AddAntiforgery(options => ...)`: cookie name `.uk.osric.asp.antiforgery`,
  `SameSite = Strict`, `HttpOnly = true`, and the same production-`Always` /
  development-`SameAsRequest` secure policy as the auth cookie.
- `app.UseAntiforgery()` after authentication and authorisation.
- `LoginEndpoint` and `EditorEndpoint` render the token with
  `GetAndStoreTokens(context)` and check `IAntiforgeryValidationFeature` on POST.
- `AddDistributedMemoryCache()` and `AddSession` with `.uk.osric.asp.session`, `HttpOnly`,
  `IsEssential`, `SameSite = Strict`, `IdleTimeout = 20 minutes` and the same secure policy.
- The editor POST stores its message in the session and answers `303`; the GET reads and removes it.
  Remove `CreateMessageCookie` and the whole `message` cookie path.

The in-memory session store is deliberate: one process, one owner, and losing a flash message on
restart costs nothing. The auth cookie survives restart because the data-protection keys do.

## Data protection and deployment

- `builder.Services.AddDataProtection().SetApplicationName("uk.osric.asp")`.
- `deploy/uk.osric.asp.container` gains
  `Volume=uk.osric.asp-keys:/home/app/.aspnet/DataProtection-Keys:U`.
- There is no provider URL, client ID or client secret to add; the only new deployment concern is
  the key volume.
- The storage volume now contains password hashes. `deploy/README.md` says so, and says to treat a
  backup of `storage.db` as a credential store.
- `deploy/README.md` gains "Creating the first account":
  - local: `make user-add ACCOUNT=osric`;
  - server, container running:
    `ssh deploy@host podman exec -it uk.osric.asp dotnet uk.osric.asp.Web.dll users add osric`;
  - server, container stopped:
    `podman run --rm -it -v uk.osric.asp-storage:/data -e 'ConnectionStrings__Storage=Data Source=/data/storage.db' registry.osric.uk/uk.osric.asp:latest users add osric`.
  - password reset is the same shape with `users passwd`.
- The site starts with no accounts. Until `users add` runs, `/edit` redirects to `/login` and no
  credentials work; that is the intended safe default, and the README says it.

## Tests

### Unit (`Web.Tests`)

- `UserNameTests`: trim, case-fold, reject empty/whitespace/too long/control characters; `Osric` and
  ` osric ` normalize to the same value.
- `PasswordPolicyTests`: rejects short passwords, accepts a 12-character passphrase and a long one,
  rejects over 1024.
- `PasswordServiceTests`: a hash verifies with the right password, fails with the wrong one, two
  hashes of the same password differ (salt), and a dummy verification never succeeds.
- `SecurityStampTests`: `Create` returns a non-empty value and two calls differ.
- `SqliteUserStoreTests`: the store behaviours listed above, including that `ChangePasswordAsync`
  replaces the hash and the stamp while `UpdateHashAsync` leaves the stamp alone.
- `UserCommandsTests` with a scripted console: add, duplicate, passwd, list, remove, mismatched
  confirmation, `--password-stdin`, and exit codes.
- `LocalRedirectTests`: `/edit` and `/edit/a` safe; `//evil`, `https://evil`, `/\evil`, empty and
  control characters unsafe.
- `LoginEndpointTests` with a fake `IAuthenticationService`: invalid antiforgery `400`; missing
  username/password; unknown user and wrong password produce the same message; a valid account signs
  in and redirects `303`; the **Keep me signed in** checkbox sets `IsPersistent`; an unsafe
  `returnUrl` falls back to `/edit`; rehash-needed updates the hash without changing the stamp.
- `EditorEndpointTests`: the existing cases lose `IWebHostEnvironment`, gain `IAntiforgery`, assert
  the hidden token, the session message read-once and the `303`, and assert no `message` cookie.

### Integration (`Web.IntegrationTests`)

`AspApplication` seeds one known account through `IUserStore`/`IPasswordService` after startup
migrations (a helper on `IntegrationTest`), and raises the login rate limit so unrelated tests do
not throttle each other. There is no test authentication scheme: every test signs in through the
real form, which is the point.

Add a `SignInAsync(client)` helper: `GET /login`, parse
`__RequestVerificationToken`, POST the credentials, assert `303`. Tests:

- `AnonymousGetEditorRedirectsToLogin` — `302` to `/login?returnUrl=%2Fedit%2Fhello`, no editor body.
- `AnonymousPostEditorWritesNothing` — `302` to login; a direct storage read shows no row.
- `LoginPageRendersTheForm` — `200`, username/password fields and the token.
- `ValidCredentialsSignInAndRedirectToTheEditor` — `303`, then `GET /edit/hello` is `200`.
- `InvalidCredentialsLeaveTheRequestSignedOut` — `200` with the generic message; `GET /edit` still
  redirects.
- `LoginWithoutAntiforgeryReturns400`.
- `LoginIsRateLimited` — a dedicated factory with a limit of 3; the fourth attempt is `429`.
- `RememberMeCookieIsPersistent` — signing in with the box checked gives a `Set-Cookie` with an
  expiry; unchecked gives a session cookie with none.
- `PasswordChangeInvalidatesAnExistingCookie` — sign in, change the password (new hash and stamp)
  through `IUserStore`, then `GET /edit` redirects to `/login`.
- `RemovedUserInvalidatesAnExistingCookie` — sign in, remove the account, then `GET /edit`
  redirects to `/login`.
- `LogoutClearsTheCookie` — signed-in client posts `/logout`, then `GET /edit` redirects.
- SEC-02: `SaveWithoutATokenReturns400AndWritesNothing`, `SaveWithATokenStoresThePage` and
  `SaveMessageIsShownOnce`, all with a signed-in client.

## Documentation and bookkeeping

- `docs/architecture.md`: replace the "future auth cookie" wording and add **Authentication and the
  editor** — local accounts, the CLI-only creation rule, PBKDF2 hashes, the cookie, antiforgery,
  the session and the protected routes.
- `docs/testing.md`: the integration suite now signs in through `/login`; the flash cookie is gone.
- `README.md`: the editor requires sign-in; `make user-add ACCOUNT=...` creates the first account.
- `docs/issues/README.md`: move SEC-01 and SEC-02 to the Closed table, delete their issue files, and
  remove the "401/403 cases wait for SEC-01" clause from the ARCH-08 entry.
- Mark this plan **Implemented** with test counts when it lands.

## Sequencing

1. **Accounts first** — `LocalUser`, `UserName`, `PasswordPolicy`, `PasswordService`,
   `IUserStore`/`SqliteUserStore`, the migration, the CLI and the Makefile targets. Test-driven;
   the app still serves pages and the editor is still open at the end of this step.
2. **Cookie auth** — `AddAuthentication`/`AddCookie`, the login and logout handlers, the `Editor`
   policy, `RequireAuthorization` on `/edit`, reserved paths and the rate limiter. Update the
   existing editor integration tests to sign in. This closes SEC-01.
3. **Antiforgery and session** — `AddAntiforgery`/`UseAntiforgery`, token rendering and the feature
   checks, `AddSession`, and the flash-message replacement. This closes SEC-02.
4. **Deployment and docs** — data-protection volume, first-account instructions, architecture and
   testing notes, issue bookkeeping.

Each step leaves the build and the suites green. The site is unusable for editing between steps 2
and the first `make user-add`, which is the safe direction.

## Open questions

None blocking. Two choices worth recording:

- **Accounts are the owner list.** Adding a second account is a deliberate grant of editor access,
  not a bug. If that changes, the CLI or the policy needs a single-owner rule.
- **The rate limiter is global.** If the site ever sits behind a proxy that forwards the client
  address, partition by `X-Forwarded-For` instead; today every request is `127.0.0.1` and a global
  window is honest.
- **The long-term window is 30 days of inactivity.** It is one constant in the cookie options; if
  the trade-off changes, move it there rather than adding a second expiry path.
