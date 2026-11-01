<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# uk.osric.asp

A web server whose pages are C# and can edit themselves.

I have been trying to get back to the site I had in 1999, when I was doing BT support: a textarea
with the source of the page in it, and a save button that worked. That one was Perl and CGI. This
one is ASP.NET Core, compiles the page when you save it, and gives me the same trick with
autocomplete and real error messages.

## How it works

- Every page is a row in SQLite, keyed by its canonical path, so `/about` and `/About/` are the same
  page.
- A page is HTML with `<% %>` blocks of C# in it. Saving compiles it with Roslyn and stores the
  emitted assembly beside the source; a request loads that assembly into a collectible
  `AssemblyLoadContext` and runs it against `IContext`. Pages that have no usable assembly are
  compiled on their next request.
- `/edit/anything` is the editor: sign in with a local account, then a textarea, a save button, and
  a line telling you it worked. Accounts are created from the command line, not from the site.
- `uk.osric.asp.Core` is pure functions, `uk.osric.asp.Runtime` is Roslyn and assembly loading, and
  `uk.osric.asp.Web` is the endpoints. The core references nothing, not even another project.

## A page

```
<h1>Hello</h1>
<p>You asked for <% ctx.Write(ctx.Path); %>.</p>

<% for (int i = 1; i <= 3; i++) { %>
    <p>Line <% ctx.Write(i.ToString()); %></p>
<% } %>
```

`ctx.Write` escapes, `ctx.WriteRaw` does not, and `ctx.Log` writes to the log. The
[architecture notes](docs/architecture.md#page-contract) list the rest of the surface.

## Running it

```bash
make run
make user-add ACCOUNT=osric     # in another terminal: prompts for a password
```

The database starts empty, so every page answers 404 until you write something. Sign in at
`http://localhost:5000/login`, go to `/edit/` and make the front page, then `/edit/about` for the
next one. Saving is live: the save compiles the new source, so the next request runs it and there is
no build step in between. "Keep me signed in" is checked by default and the cookie lasts 30 days of
inactivity; `users passwd` revokes every existing login.

## The task runner

The `Makefile` is the only entry point; `make help` lists everything.

| Target | What it does |
| --- | --- |
| `make build` | Build the solution, but only when a source or project file changed (the default) |
| `make restore` | Force a NuGet restore; `build` only restores when an input changed |
| `make test` | All four suites: core, runtime, web, integration |
| `make test-core` | Core tests only |
| `make test-runtime` | Runtime tests only |
| `make test-web` | Web shell and storage tests only |
| `make test-integration` | The composed pipeline against a throwaway database |
| `make user-add ACCOUNT=...` | Create a local editor account |
| `make user-passwd ACCOUNT=...` | Change a password and sign out every existing session |
| `make user-list` | List local accounts |
| `make user-remove ACCOUNT=...` | Remove a local account |
| `make remote-user-add HOST=deploy@server ACCOUNT=...` | Create an account in the container on the server |
| `make remote-user-passwd HOST=... ACCOUNT=...` | Change a password on the server and sign out every session |
| `make remote-user-list HOST=...` | List the accounts on the server |
| `make remote-user-remove HOST=... ACCOUNT=...` | Remove an account on the server |
| `make run` | Run the site in Development on `http://localhost:5000` |
| `make publish` | Publish the app to `artifacts/publish` |
| `make format` | Apply formatting and style fixes |
| `make format-check` | Fail if the tree is not formatted |
| `make license-check` | Fail if the tree is not REUSE compliant |
| `make migration NAME=...` | Add a storage migration |
| `make image` | Build the OCI image into podman |
| `make deploy HOST=deploy@server` | Build, push and restart on the server |
| `make clean` | Delete `artifacts/` |

## Dependencies

Everything from NuGet is pinned in `Directory.Packages.props` and restored by `make build`. These
are the things NuGet cannot give you, and a fresh machine has none of them.

| Tool | Needed for | Notes |
| --- | --- | --- |
| .NET SDK 10.0.403 | Everything | Pinned in `global.json`, with roll-forward |
| make | Everything | The task runner; also uses `find`, `sed` and `printf` |
| git | Getting the source | |
| reuse | `make license-check` | `apt install reuse`, or `pipx install reuse` |
| podman | `make image`, `make deploy` | Builds the OCI image from `deploy/Containerfile` |
| ssh, scp | `make deploy` | Copies the image and the quadlets to the server |
| systemd | `make deploy` | The app and Caddy run as quadlets, which are systemd units |

The server side needs rootless podman with `uidmap`, `passt` and `fuse-overlayfs`;
`deploy/cloud-init.yaml` installs those on a fresh Debian 13 VM.

Optional, but they make life easier: the `hcloud` CLI if you are creating the VM, and VS Code with
the C# extension, which has a launch configuration in `.vscode/launch.json`.

## More detail

- [Architecture](docs/architecture.md) — the core/shell split and where new code belongs.
- [Testing](docs/testing.md) — the four suites and what each one is allowed to prove.
- [Deployment](deploy/README.md) — quadlets, Caddy, the storage volume and migrations.
