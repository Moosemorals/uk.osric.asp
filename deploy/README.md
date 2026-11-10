<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Deployment

The app ships as an OCI image built by podman from `deploy/Containerfile` and runs under **podman
quadlets** on a single Debian 13 (trixie) arm64 VM (Hetzner CAX21). The image contains the publish
output and nothing else: the SDK runs on the build machine, never in the image, and the build
context is the published directory, so `COPY . /app` is exactly the files the app runs from.

The build is reproducible. `podman build --timestamp` normalises the created time and every file
mtime, the epoch is the commit's own timestamp, and the base image is pinned by digest, so two builds
of one commit produce one image id — on the same buildah version, which is recorded in the image's
`io.buildah.version` label. `make image-check` proves it by building twice and comparing, and it runs
as part of `make ci`.

The app runs **rootless** as the `deploy` user, beside a second rootless container for **Prometheus**.
**Caddy** is the only rootful unit: it owns 80/443, uses host networking, and proxies to the app's
loopback port.

```
deploy/
  Containerfile             the image: the publish output on the pinned aspnet base
  cloud-init.yaml           one-shot host bootstrap (rootless podman, user, firewall, SSH)
  uk.osric.asp.container    user quadlet: the app, rootless, publishing 127.0.0.1:8080
  uk.osric.asp.network      user network: the app and Prometheus talking by name
  prometheus.container      user quadlet: Prometheus, rootless, publishing 127.0.0.1:9090
  prometheus.yml            scrape config, installed to ~/.config/prometheus/prometheus.yml
  caddy.container           system quadlet: Caddy, host network, terminating TLS
  Caddyfile                 site config, installed to /etc/caddy/Caddyfile
```

The quadlets and the Caddyfile are the source of truth for the server: `make deploy` copies them up
and applies them.

## First deployment

1. Create the VM (Hetzner `cax21`: 4 Arm64 vCPUs, 8 GB of RAM, 80 GB of SSD; Debian 13) with the
   official CLI image — nothing is installed locally except podman. `--ssh-key` takes the *name* of a key registered in the Hetzner project, not a path
   to a local file, so register it first. `cloud-init.yaml` already lists the two keys `deploy` will
   accept (the ones on `lucy` and `Carrot`); edit that list if you deploy from another machine. The
   Hetzner key is a fallback for root if user creation ever fails.

   ```bash
   export HCLOUD_TOKEN=<token from the Hetzner console>

   podman run --rm -e HCLOUD_TOKEN \
     -v "$HOME/.ssh/id_ed25519.pub:/keys/id_ed25519.pub:ro" \
     docker.io/hetznercloud/cli:latest \
     ssh-key create --name lucy --public-key-from-file /keys/id_ed25519.pub

   podman run --rm -e HCLOUD_TOKEN \
     -v "$PWD/deploy:/deploy:ro" \
     docker.io/hetznercloud/cli:latest \
     server create --name uk.osric.asp --type cax21 --image debian-13 \
       --location fsn1 --ssh-key lucy \
       --user-data-from-file /deploy/cloud-init.yaml
   ```

   The image is a distroless binary whose entrypoint is `hcloud`, so arguments are passed straight
   through; add `:z` to the mounts on a host with SELinux enforcing.

   `cloud-init.yaml` installs podman with the rootless pieces (`uidmap`, `passt`, `fuse-overlayfs`),
   creates the `deploy` user with subuid/subgid ranges and its own SSH keys, enables lingering so its
   user manager outlives the login, hardens SSH, and opens 22, 80 and 443 (TCP and UDP) in ufw. It
   also creates a 4 GiB swap file and adds it to `/etc/fstab` — see [memory and
   swap](operations.md#memory-and-swap) for what it is for. It reboots once if the package upgrade
   installed a new kernel, and ends with a rootless `podman info` that must print `true` — check it
   with `cloud-init status --long` and `swapon --show` before relying on the host.

2. Point an `A` (and `AAAA`, if you use IPv6) record at the server.

3. Put your domain and email in `deploy/Caddyfile`. `make deploy` refuses to run while the
   `example.com` placeholder is still there, because Caddy would start, fail its ACME challenge, and
   leave the site unreachable while every command reports success.

4. Log in to the registry locally and on the server — see [The registry](#the-registry). `make deploy`
   pushes as your local user and pulls as `deploy`, so both need credentials that outlive a reboot.

5. Ship it:

   ```bash
   make deploy HOST=deploy@<server-ip>
   ```

   That runs the same gate as a commit (`make check`: formatting, licences, package audit, all four
   suites and a Release publish), then the browser suite in Firefox — two minutes, and `make browsers`
   installs Firefox the first time — before it builds and pushes the image, pulls it as `deploy`,
   installs the network and the app's user quadlet, restarts the app, writes the Prometheus config and
   quadlet and restarts that, then writes the Caddyfile to `/etc/caddy/` with sudo, installs the Caddy
   unit, reloads systemd and restarts Caddy. Caddy obtains a certificate on first request, so the site
   is reachable a few seconds later.

   The target refuses to run from a tree with uncommitted changes, because the image is tagged with
   its commit and that tag is only meaningful if the commit is the tree (`ALLOW_DIRTY=1` overrides,
   for a deliberate one-off). It runs the gate and the browser suite before the image is built, pushes
   the commit tag alongside `IMAGE_TAG`, and it does not report success until
   `http://127.0.0.1:8080/heath` answers: a restart that leaves the unit failed is a failed deploy,
   not a quiet one.

   The quadlet's `Image=` names the commit, so the host answers "what is running?" without anyone
   guessing, and rolling back is a tag rather than a rebuild.

## Day-to-day

- **New code:** commit it, then back up storage, then `make deploy HOST=deploy@<server-ip>`
  (guards, checks, runs the browser suite, builds, pushes, pulls, restarts, waits for `/heath`, then
  says so if the commit it deployed is not on the remote yet).
- **The gate:** `make check`, run by hand when you want it — `make deploy` runs it anyway, with the
  browser suite after it. `make ci` is the slower version that starts from `make clean` and fails if
  the build dirties the tree; run it before a release, or from any machine that is asked to prove the
  tree. Neither needs a CI service, an account or a secret, and nothing runs them behind a `git push`.
- **Domain or TLS settings:** edit `deploy/Caddyfile`, then `make deploy HOST=...`.
- **Page assistant:** disabled by default. `make deploy` keeps an empty, mode `0600`
  `~/.config/uk.osric.asp/assistant.env` for the `deploy` user, because the container cannot start
  without that file — podman's env file, which is what the quadlet's `EnvironmentFile=` becomes, has
  no optional form. Fill it in and redeploy to enable the assistant: `Assistant__Enabled=true`,
  `Assistant__ApiKey=<provider-key>`, and optionally `Assistant__Model` or `Assistant__BaseUrl`. The deploy never
  overwrites an existing file. The configured provider must speak OpenAI-compatible streaming chat
  completions with tool calls. Local HTTP is allowed only for a loopback provider or when explicitly
  enabled.
  **Upgrading the context budget:** `Assistant__MaxContextChars` and `Assistant__MaxRequestBytes` are
  both now `Assistant__MaxContextTokens`, and the old keys are not aliased. The shipped 90,000 tokens
  plus the 32,768 output reserve fits the default `Assistant__ModelContextTokens` of 131,072; set both
  numbers if the provider's window differs.
  The host sends the system prompt, conversation transcript, and any page source read by the assistant to
  that provider. Do not store content there that the provider must not receive. Prompts, page source,
  reasoning, credentials, and provider error bodies are not logged. The default provider is DeepSeek;
  compatible services and local Ollama can be selected by configuration. Restart the app after
  changing the environment file.
  **Upgrading from `Agent`:** the file and its keys were renamed when the feature was, so `make deploy`
  seeds a new, empty `assistant.env` beside the old `agent.env` and the assistant comes up disabled.
  Move the file and rename each key (`Agent__Enabled` → `Assistant__Enabled`, and so on) rather than
  copying the contents; the old file is ignored from that release on. The storage tables are renamed
  by a migration on first start, which keeps every conversation.
- **Assistant streaming through Caddy:** `/assistant/*/events` is excluded from compression and the
  reverse proxy flushes events immediately. Verify through the proxy with a real queued conversation
  using `curl -N`, not only against the Kestrel port. A run continues if its browser is closed; the
  `Stop` form cancels it. During graceful shutdown the host records unfinished runs as interrupted;
  the 300-second default run timeout is independent of the container's 35-second graceful stop.
- **Outbound fetch:** any `http` or `https` host is reachable, with no allow-list (DEC-216).
  `Pages__Fetch__TimeoutSeconds` defaults to 4, deliberately below the 5-second render budget;
  `MaxResponseBytes`, `MaxRequestsPerRender` and `MaxRedirects` bound the rest. The container's egress
  and internal network are not otherwise restricted, and the assistant's `fetch` tool uses the same
  path, so a stored page or an approved tool call can reach anything the container can. That is the
  single-owner trust boundary, not a sandbox (SEC-03). Every request carries
  `moosemorals.com (page)` or `moosemorals.com (assistant)` as its `User-Agent`, set by the host.
- **Page state:** a page reaches state through `ctx.Features.GetRequired<IPageStorage>()`; it is
  stored in `PageState` in the same database, so `storage.db` carries it and a backup includes
  it. Deleting a page clears its state; editing or restoring it
  keeps it. The caps live under `Pages:Storage` (`Pages__Storage__MaxValueBytes`, and so on).
- **Reserved page keys:** `Pages:ReservedPaths` (environment form
  `Pages__ReservedPaths__0`, `__1`, ...) lists the keys the site's own routes answer for. The default
  covers `/admin/export`, `/admin/import`, `/assistant`, `/edit`, `/favicon.ico`, `/heath`, `/login`,
  `/logout`, `/metrics` and `/status`. `/admin` itself is a seeded page, so only the tools beneath it
  are reserved. Change the list only when the route table changes.
- **Layout:** `Pages:Layout` (environment form `Pages__Layout`) names the page every HTML page is
  wrapped in; it is seeded as `/_layout` and is edited and restored from the page list like any other
  page. An empty value turns wrapping off. `Pages__Render__MaxLayoutDepth` bounds how deep a chain of
  layouts may nest (four by default, clamped 1–16), and a missing or uncompilable *default* layout
  degrades to unwrapped pages with a warning and a `page.layout.fallback` count rather than taking
  the site down.
- **Import and export:** `/admin/export` and `/admin/import` move pages between sites as a `.sqlite`
  archive of source and state. The caps live under `Pages:Archive` (`Pages__Archive__MaxUploadBytes`,
  `MaxPages`, `MaxCompiledBytes`, `StageTtlMinutes`, `MaxStagedImports`); a reviewed archive is held
  in memory only and expires, so raising `MaxUploadBytes` is the answer for a site whose export is
  larger than 16 MiB.
- **Health:** `ssh deploy@<server-ip> curl -s http://127.0.0.1:8080/heath` returns detailed JSON —
  200 when the app can reach its database and no page renders have been abandoned, 503 otherwise. The
  same probe runs inside the container as podman's health check, so a failing check restarts it; see
  [resource limits and restarts](operations.md#resource-limits-and-restarts).
- **Container health:** `ssh deploy@<server-ip> podman healthcheck run uk.osric.asp` forces a check,
  and `podman inspect uk.osric.asp --format '{{.State.Health.Status}}'` shows the current one.
- **Logs:** `ssh deploy@<server-ip> journalctl --user -u uk.osric.asp.service -f`. Each entry is one
  single-line JSON object with its scopes as fields; [the logging notes](../docs/logging.md#working-with-the-journal)
  have the levels and the `jq` recipes for reading one run or every warning.
- **Status:** `ssh deploy@<server-ip> systemctl --user status uk.osric.asp.service`
- **Edge:** `ssh deploy@<server-ip> sudo systemctl status caddy`
- **Rollback:** the quadlet's `Image=` already names the commit the host is running, so going back is
  editing that line to the commit you want and restarting: `podman pull` the
  `registry.osric.uk/uk.osric.asp:<sha>` tag on the host, rewrite `~/.config/containers/systemd/uk.osric.asp.container`,
  `systemctl --user daemon-reload`, `systemctl --user restart uk.osric.asp.service`. Checking out
  the earlier commit and running `make deploy` does the same thing through the gate. A previous image
  may not support the migrated schema; restore its matching backup with the app stopped when
  necessary.

The limits, storage, backups, metrics and proxy trust are in [running it](operations.md): they are
read when something is wrong rather than when a release ships.

## Local accounts

Create the first account before using the editor. Locally that is `make user-add ACCOUNT=osric` and
its siblings — [the README](../README.md#the-task-runner) lists them — and the command runs the same
binary and migrations as the site, prompting for the password with echo off (or reading one line from
standard input when `--password-stdin` is passed). On the server, `make` reaches the same command in
the running container with `podman exec`, so it uses the deployment's own storage volume and
connection string:

```bash
make remote-user-add HOST=deploy@<server-ip> ACCOUNT=osric
make remote-user-passwd HOST=deploy@<server-ip> ACCOUNT=osric
make remote-user-list HOST=deploy@<server-ip>
make remote-user-remove HOST=deploy@<server-ip> ACCOUNT=osric
```

`remote-user-add` and `remote-user-passwd` need a terminal at both ends, because the password is
read a key at a time with echo off. The container must be running; if the unit has stopped, use a
one-off container with the same storage volume instead:

```bash
podman run --rm -it \
  -v uk.osric.asp-storage:/data \
  -e 'ConnectionStrings__Storage=Data Source=/data/storage.db' \
  registry.osric.uk/uk.osric.asp:latest users add osric
```

The site starts with no accounts. Until one is created, `/edit` redirects to `/login` and no
credentials work.

Passwords are hashed with PBKDF2-HMAC-SHA256 at 600,000 iterations. There is no password recovery
flow: use `users passwd` from the host. That command rotates the security stamp, so it also signs out
every browser that is already logged in.

The mount's `:U` option recursively changes the volume's ownership to the host UID/GID mapped from
the container's `User=1654:1654`. This lets the unprivileged app create the database and SQLite
journal/WAL files without relaxing permissions. It modifies ownership on the host and may slow
startup on a volume with many files. Keep this volume dedicated to the app; do not substitute a
shared host directory without accepting those ownership changes. See Podman's
[volume ownership documentation](https://docs.podman.io/en/latest/markdown/podman-run.1.html#volume-v).

The app applies pending migrations asynchronously at startup, before serving requests. **Take a
verified backup before starting a new release with migrations**, including unattended updates.
Review generated migrations before deployment; startup migration does not make a backup for you.
In particular, take and verify an off-volume backup before deploying the retained-revisions
migration.

To add a migration locally from the repository root:

```bash
make migration NAME=DescribeTheSchemaChange
make test-web
```

The target restores the repository's pinned local `dotnet-ef` tool (10.0.14), builds using the normal
Makefile target, and writes migration sources to the Web project's `Storage/Migrations` directory.
EF 10 reads the evaluated MSBuild output paths, including `artifacts/obj/uk.osric.asp.Web/`. Commit and review the generated migration
with the model change. `make test` includes the Web storage and integration tests as well as the core
and runtime tests.

## Why rootless

The app is a user quadlet in `~deploy/.config/containers/systemd`, so podman runs it in a user
namespace: the container's uid 1654 maps to a subuid on the host, not to a bare host uid, and no part
of it needs root. Caddy is the exception because it binds 80/443; it runs rootful on the host network
and reaches the app at `127.0.0.1:8080`.

A rootless container cannot join a rootful podman network, which is why the app publishes to the
loopback and Caddy uses `Network=host` instead of a shared bridge. The app and Prometheus are both
rootless, so they *can* share `uk.osric.asp.network`; container names on that network resolve through
`aardvark-dns`, which `cloud-init.yaml` installs.

## The registry

`REGISTRY` defaults to `registry.osric.uk`; `make deploy` tags the locally built image, pushes it, and
tells the server to pull:

```bash
podman login --authfile ~/.config/containers/auth.json registry.osric.uk            # locally
ssh -t deploy@<server-ip> podman login --authfile /home/deploy/.config/containers/auth.json registry.osric.uk
make deploy HOST=deploy@<server-ip>
```

The server path is written out in full: an unquoted `~` in an `ssh` command is expanded by the
**local** shell, so it would point at the wrong home.

The `--authfile` on the server is not optional: podman's default is
`${XDG_RUNTIME_DIR}/containers/auth.json`, which lives under `/run/user/` and is gone after a reboot,
so a pull would fail on the first deploy after every restart. `make deploy` reads the same file
(`AUTHFILE` locally, `REMOTE_AUTHFILE` on the server), so override those variables if you keep the
credentials somewhere else. If the registry serves a certificate the host does not trust, install the
CA at `/etc/containers/certs.d/registry.osric.uk/ca.crt` (or the same path under
`~/.config/containers/certs.d/`).

The first push uploads the base layers (~160 MB); later pushes send only the app layer. Rootless
images live in `~deploy/.local/share/containers`, so `podman images` as root will not show them.

For unattended updates, add `AutoUpdate=registry` to the app quadlet and run
`systemctl --user enable --now podman-auto-update.timer` on the server.

## Notes

- The image is built for `linux-musl-arm64`, matching the CAX21 host. On an x86 machine use
  `make deploy CONTAINER_RID=linux-musl-x64 CONTAINER_PLATFORM=linux/amd64`; the two must agree.
- The app container runs as uid 1654 with all capabilities dropped and `no-new-privileges`; only
  Caddy publishes ports that the outside world can reach.
- Data-protection keys are persistent in `uk.osric.asp-keys`. Deleting that volume signs every
  account out and invalidates in-flight forms; it is also the bluntest way to revoke a login if the
  CLI is unavailable.
