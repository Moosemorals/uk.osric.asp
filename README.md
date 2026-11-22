<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# uk.osric.asp

A web server whose pages are C# and can edit themselves.

I have been trying to get back to the site I had in 1999, when I was doing BT support: a textarea
with the source of the page in it, and a save button that worked. That one was Perl and CGI. This
one is ASP.NET Core, compiles the page when you save it, and gives me the same trick with real error
messages instead of a blank page.

## The whole idea, in one file

The smallest version of this site is a C# file that serves its own source, writes the posted source
over itself, and asks the thing that runs it to start again. .NET 10 and later run a single file, so
the file needs no project, no build step and no database:

```csharp
#!/usr/bin/env -S dotnet run
#:sdk Microsoft.NET.Sdk.Web
using System.Net;
using System.Runtime.CompilerServices;

// EX_TEMPFAIL: the only exit code the unit is configured to restart on.
const int RestartMe = 75;
string self = Here();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5099");
var app = builder.Build();

app.MapGet("/", (HttpContext ctx) => {
    string source = File.ReadAllText(self);
    ctx.Response.ContentType = "text/html; charset=utf-8";
    return Results.Text($$"""
        <!doctype html>
        <h1>Edit me</h1>
        <form method="post">
        <textarea name="source" rows="24" cols="100">{{WebUtility.HtmlEncode(source)}}</textarea>
        <p><button>save over me</button></p>
        </form>
        """, "text/html");
});

app.MapPost("/", async (HttpContext ctx) => {
    var form = await ctx.Request.ReadFormAsync();
    string posted = form["source"].ToString();
    string current = File.ReadAllText(self);
    await ctx.Response.WriteAsync(
        string.Equals(posted, current, StringComparison.Ordinal)
            ? "unchanged — not restarting.\n"
            : "saved — exiting 75 so the unit builds and starts the new one.\n");
    await ctx.Response.CompleteAsync();
    if (string.Equals(posted, current, StringComparison.Ordinal)) {
        return;
    }
    File.WriteAllText(self, posted);
    Environment.Exit(RestartMe);
});

app.Run();

static string Here([CallerFilePath] string path = "") => path;
```

Something outside the file has to decide that it runs; in Perl that was Apache, and here it is a
systemd user unit. The script is `chmod +x`, `ExecStart` points at it, and the script chooses the one
moment it is restarted by exiting `75`:

```ini
[Unit]
Description=A C# file that serves and replaces itself
StartLimitIntervalSec=10
StartLimitBurst=3

[Service]
Type=exec
WorkingDirectory=/tmp/quine
ExecStart=/tmp/quine/quine.cs
Restart=no
RestartForceExitStatus=75
RestartSec=1
```

Four details are the whole trick:

- **`#!/usr/bin/env -S dotnet run` needs the `-S`.** Without it, `env` looks for a program named
  `dotnet run` and the shebang fails. The shebang is the deployment.
- **The first line is not a problem for the compiler.** `#!` is ignored, and `#:sdk` is the
  file-based-app directive that makes the file a web app rather than a console program.
- **`[CallerFilePath]` is how the file finds itself.** `Environment.GetCommandLineArgs()` and
  `/proc/self/cmdline` both name the cached build under `~/.local/share/dotnet/runfile`, not the
  source. A compile-time constant is the only thing that still says where the file lives.
- **`Restart=no` with `RestartForceExitStatus=75` is the policy.** A compile error, a crash or a busy
  port leaves the unit failed with the reason in the journal, and a person decides what to do; only
  the script's own exit `75` asks for another start. The start limit is the backstop for a script that
  always asks to restart.

That is the floor, and it is worth being honest about what a floor is. It has one page, no history and
no undo, and no gate between a post and the running code: post something that is not valid C#, and
the site is down until someone edits the file by hand. The process that answers the post is the old
code — it writes the new source and exits for it — and the next start is whatever the post left. The
page store in the rest of this repository keeps the one property that matters from the quine —
**saving the source is the deployment, and the next request runs it** — and spends the rest of the
code making that safe enough to do from a browser.

## What the rest of the code buys

Measured with cloc on 2026-11-22, code lines only (blanks and comments excluded):

| | Lines | Why it exists |
| --- | --- | --- |
| The page platform, with the seeded site and every capability | 14,700 | writes, history, sessions, fetch, jobs, checks, layouts, composition |
| — of which the line you cross before one page can be served: the contract, template to C#, the API policy, Roslyn, loading, rendering, reading the store | ~6,600 | the quine's `dotnet run`, owned instead of borrowed |
| The assistant | 9,600 | a product built on the platform, not part of it |
| Storage and its migrations | 6,347 | revisions, page state, sessions, runs and memory |
| The admin views and the archive | 2,394 | the tools a page cannot do for itself, and import/export |
| The status dashboard | 1,909 | operations, not the keel |
| Authentication | 931 | the price of an editor at a URL |
| Everything else: the document shell, error responses, work, users, the reference renderer, the editor's glue | ~6,900 | |
| **Product code** | **42,775** | |
| Tests | 46,858 | the premium on a platform that can rewrite itself while it serves |
| Docs | 43,290 | the decisions, the backlog and the author manual |

Each row above the product line is a property the quine does not have, and each one is a decision
rather than a law:

- **The compile gate.** A bad save is a diagnostic in the editor and the previous image keeps
  serving, because a page is compiled before it is stored. The quine has no such thing, which is
  exactly why its bad save is fatal.
- **History.** Every save is a revision, so a page can be restored, and the seed pack means the site
  can be repaired when a page no longer compiles.
- **Identity.** The editor is a URL, so it needs a sign-in, an antiforgery token and an account made
  from the command line. A page is handed a `PageUser` and can never mint one.
- **The budget.** Two renders at once, a deadline, a body cap, a fetch cap and a compile slot: the
  price of running code someone else may have written.
- **More than one page.** A store, a key rule, content types and assets — and then a page that lists
  the pages, and one that edits them, which is `/pages` itself.

The rule that keeps the list honest is in [where a view lives](docs/architecture.md#where-a-view-lives):
**anything a page can serve is a page** ([`pages-page-first`](docs/decisions.md)), and a route stays
in the shell only because it is the runtime pages run on, the repair path for a page that no longer
compiles, or identity a page must not be able to mint. Everything else is a view with a named
blocker or a job — [the keel spike](docs/spikes/keel.md) re-reads that audit with the numbers above,
and says what each remaining route needs before it can leave.

What is next — every defect and idea, with its difficulty, utility, blockers and plan — is
[the backlog](docs/README.md#the-order); what has been decided and why is [the decision
record](docs/decisions.md).

## How it works

- Every page is a row in SQLite, keyed by its canonical path, so `/about` and `/About/` are the same
  page.
- A page is HTML with `<% %>` blocks of C# in it. Saving compiles it with Roslyn and stores the
  emitted assembly beside the source; a request loads that assembly into a collectible
  `AssemblyLoadContext` and runs it against `IContext`. Pages that have no usable assembly are
  compiled on their next request.
- `/pages?edit=<key>` is the editor: sign in with a local account, then a textarea with a
  line-number gutter, a save button, and a line telling you it worked. Diagnostics link to the line
  that caused them, and the caret survives a save. Accounts are created from the command line, not
  from the site.
- `uk.osric.asp.Sdk` is the page contract, `uk.osric.asp.Core` is the host's pure functions,
  `uk.osric.asp.Runtime` is Roslyn and assembly loading, and `uk.osric.asp.Web` is the endpoints.
  `Sdk` and `Core` each reference nothing, not even another project.

## A page

```
<h1>Hello</h1>
<p>You asked for <%= ctx.Path %>.</p>

<% for (int i = 1; i <= 3; i++) { %>
    <p>Line <%= i.ToString() %></p>
<% } %>
<%== "<hr>" %>
```

`<%= expression %>` writes a string through `ctx.Response.Write` and HTML-encodes it;
`<%== expression %>` writes through `ctx.Response.WriteRaw` without encoding. Expressions must
produce a string, so convert other values explicitly, for example with `.ToString()`. Statement
blocks can still call `ctx.Response.Write`, `ctx.Response.WriteRaw` or `ctx.Log` directly. The handler
is asynchronous, so `await` is an ordinary expression in a block and inside `<%= %>`:
`ctx.Capabilities.GetRequired<IPageStorage>()` keeps a
page's own key/value state, and `ctx.Capabilities.GetRequired<IHttpFetch>()` reaches any `http` or
`https` host from the server. The [page contract](docs/pages.md#page-contract) lists the rest of the
surface.

`ctx.Capabilities.GetRequired<IMarkdownRenderer>()` renders Markdown to HTML for a page; write the
result with `<%== %>`. It passes raw HTML through, so it is the author's renderer rather than a
sanitiser. The [help page](src/uk.osric.asp.Web/Pages/Seeds/help) has the detail.

A page that wants to answer before it has finished can call `await ctx.Response.FlushAsync()`. The
first flush commits the status, content type and headers and sends the body written so far; later
writes are sent at the next flush, and the last of them when the handler returns. A flushing page is
served alone: it declares no wrapper and writes its own document. Once the response is committed an
exception or the render deadline truncates it instead of showing an error page or a `504`. The
[page contract](docs/pages.md#streaming) has the full rules.

A page is compiled with nullable analysis on, so a reference that may be null says so: `ctx.User` is
a `PageUser?`, and a guard binds it to a local (`ctx.User is not { } user`) before the page reads it.
A page that compiles with warnings is served normally; it is listed as "Compiled with warnings" until
the warnings are fixed, and it is not a failure.

A `<%! %>` block holds declarations rather than statements: `using` directives and types of the
page's own. They are hoisted above the handler and are usable from every `<% %>` block, before or
after the block that declares them.

```
<%!
using System.Globalization;

sealed class Amount {
    public static string Format(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
%>
<p>Total: <% ctx.Response.Write(Amount.Format(42.5m)); %></p>
```

Declaration blocks keep their template order, which is also the order of the generated `using`
directives: a `using` written after a type is a compiler error on the line it is written.

The editor's Help link opens `/help`, the author manual: layouts, template syntax, capabilities,
private pages, assets and the editor's own actions. It is itself a seeded page under
`Pages/Seeds/help`, so it can be edited and restored like any other page.

## Running it

```bash
make run
make user-add ACCOUNT=osric     # in another terminal: prompts for a password
```

The database starts empty, so every page answers 404 until you write something. Sign in at
`http://127.0.0.1:8080/login`, go to the editor at `/pages` and make the front page, then edit
`/about` for the next one. Saving is live: the save compiles the new source, so the next request runs it and there is
no build step in between. "Keep me signed in" is checked by default and the cookie lasts 30 days of
inactivity; `users passwd` revokes every existing login.

## The task runner

The `Makefile` is the only entry point; `make help` lists everything.

| Target | What it does |
| --- | --- |
| `make build` | Build the solution, but only when a source or project file changed (the default) |
| `make restore` | Force a NuGet restore; `build` only restores when an input changed |
| `make test` | All five fast suites: core, runtime, language server, web, integration |
| `make check` | The gate: formatting, licences, package audit, the seeded pages, the fast suites and a Release publish |
| `make ci` | `make check` from a clean tree, plus the reproducible-image check |
| `make test-core` | Core tests only |
| `make test-runtime` | Runtime tests only |
| `make test-language-server` | The language server's protocol tests only |
| `make test-web` | Web shell and storage tests only (the wall-clock half excluded) |
| `make test-web-slow` | The web deadlines, stream clocks and real migrations; `make deploy` runs them |
| `make test-integration` | The composed pipeline against a throwaway database |
| `make test-browser` | The real host in Firefox, with a scripted provider (`make browsers` installs Firefox once) |
| `make test-browser-live` | The browser tests that call the configured provider, which cost tokens |
| `make audit` | Fail if any NuGet package is vulnerable or deprecated |
| `make decisions` | Check the decision record: names, areas, duplicates and citations |
| `make backlog` | Check the backlog record: slugs, fields, the order, plans and citations |
| `make check-pages` | Compile the seeded pages and print the compiler's diagnostics |
| `make vscode-extension` | Link the template grammar into VS Code, then reload the window |
| `make user-add ACCOUNT=...` | Create a local editor account |
| `make user-passwd ACCOUNT=...` | Change a password and sign out every existing session |
| `make user-list` | List local accounts |
| `make user-remove ACCOUNT=...` | Remove a local account |
| `make remote-user-add HOST=deploy@server ACCOUNT=...` | Create an account in the container on the server |
| `make remote-user-passwd HOST=... ACCOUNT=...` | Change a password on the server and sign out every session |
| `make remote-user-list HOST=...` | List the accounts on the server |
| `make remote-user-remove HOST=... ACCOUNT=...` | Remove an account on the server |
| `make run` | Run the site in Development on `http://127.0.0.1:8080` |
| `make publish` | Publish the app to `artifacts/publish` |
| `make format` | Apply formatting and style fixes |
| `make format-check` | Fail if the tree is not formatted |
| `make license-check` | Fail if the tree is not REUSE compliant |
| `make migration NAME=...` | Add a storage migration |
| `make image` | Build the OCI image into podman |
| `make image-check` | Build it twice and fail if the two builds are not the same image |
| `make deploy HOST=deploy@server` | The gate plus the browser suite, build, push and restart on the server |
| `make ci-install HOST=deploy@server` | Prepare the host's CI account, bare repo, hooks, units and status file |
| `make changelist` | Write the deployed commit's history to `artifacts/changes.txt`, which `/admin/changes` serves |
| `make worktree BRANCH=...` | Create the branch and the worktree a job or plan is worked in |
| `make worktrees` | List every worktree, the main one included |
| `make worktree-remove BRANCH=...` | Remove a worktree once its work has merged |
| `make clean` | Delete `artifacts/` |

## Dependencies

Everything from NuGet is pinned in `Directory.Packages.props` and restored by `make build`. These
are the things NuGet cannot give you, and a fresh machine has none of them.

| Tool | Needed for | Notes |
| --- | --- | --- |
| .NET SDK 11.0.100 | Everything | Pinned in `global.json`, with roll-forward |
| make | Everything | The task runner; also uses `find`, `sed` and `printf` |
| git | Getting the source | |
| reuse | `make license-check` | `apt install reuse`, or `pipx install reuse` |
| podman | `make image`, `make deploy` | Receives, tags, pushes and runs the SDK-built OCI image |
| ssh, scp | `make deploy` | Copies the image and the quadlets to the server |
| systemd | `make deploy` | The app, Prometheus and Caddy run as quadlets, which are systemd units |

The server side needs rootless podman with `uidmap`, `passt`, `fuse-overlayfs` and `aardvark-dns`
(container-name resolution for the shared network); `deploy/cloud-init.yaml` installs those on a fresh
Debian 13 arm64 VM (Hetzner CAX21). The host's separate `ci` account also needs `git`, `make`,
`reuse`, `jq`, `curl`, `libicu-dev` and the SDK the tree pins; `make ci-install` checks and installs
them, adding Microsoft's apt repository for `dotnet-sdk-11.0`, and
[the deployment notes](deploy/README.md#ci-on-the-host) describe the account.

Optional, but they make life easier: the `hcloud` CLI if you are creating the VM, and VS Code with
the C# extension, which has a launch configuration in `.vscode/launch.json`.

## More detail

- [Architecture](docs/architecture.md) — the core/shell split, where new code belongs, and the map of
  the rest.
- [Pages](docs/pages.md) — the page contract, the API policy, the execution budget and compiled pages.
- [The shell](docs/shell.md) — the document shell, layouts, error responses and authentication.
- [Storage](docs/storage.md) — revisions, page state, and import and export.
- [Metrics](docs/metrics.md) — the instruments and the `/admin/status` dashboard.
- [Assistant](docs/assistant.md) — the `/assistant` subsystem.
- [Glossary](docs/glossary.md) — the site's own words, each with the neighbouring term it is not.
- [Logging](docs/logging.md) — what gets logged, at which level, and with which scope.
- [Decisions](docs/decisions.md) — the policy choices behind the site, and what each one replaced.
- [Testing](docs/testing.md) — the five suites and what each one is allowed to prove.
- [Spikes](docs/spikes/README.md) — questions answered with measurements, and what they ruled out.
- [Deployment](deploy/README.md) — quadlets, Caddy, the first release and rollback.
- [Running it](deploy/operations.md) — limits, storage, backups, metrics and proxy trust.
- [Working on it](CONTRIBUTING.md) — the branch and worktree policy, and how work merges.
