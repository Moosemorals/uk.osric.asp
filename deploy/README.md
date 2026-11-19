<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Deployment

The app ships as an OCI image built by the .NET SDK's container targets and runs under **podman
quadlets** on a single Debian 13 (trixie) arm64 VM (Hetzner CAX21). The image contains the publish
output and nothing else: the SDK runs on the build machine, never in the image, and the container
layer is exactly the files the app runs from.

The build is reproducible. `SOURCE_DATE_EPOCH` — the commit's own timestamp — stamps the image's
created time, history entries and layer mtimes, and the base image is pinned by digest, so two builds
of one commit produce one image id. `make image-check` proves it by publishing twice, with the SDK's
layer cache emptied in between, and comparing the ids; it runs as part of `make ci`.

The app runs **rootless** as the `deploy` user, beside a second rootless container for **Prometheus**.
**Caddy** is the only rootful unit: it owns 80/443, uses host networking, and proxies to the app's
loopback port.

```
deploy/
  cloud-init.yaml           one-shot host bootstrap (rootless podman, user, firewall, SSH)
  uk.osric.asp.container    user quadlet: the app, rootless, publishing 127.0.0.1:8080
  uk.osric.asp.network      user network: the app and Prometheus talking by name
  prometheus.container      user quadlet: Prometheus, rootless, publishing 127.0.0.1:9090
  prometheus.yml            scrape config, installed to ~/.config/prometheus/prometheus.yml
  node-exporter.container   user quadlet: node_exporter serving the host's textfile metrics
  fail2ban-metrics.sh       root collector: fail2ban's counters as Prometheus text
  caddy.container           system quadlet: Caddy, host network, terminating TLS
  Caddyfile                 site config, installed to /etc/caddy/Caddyfile
  fail2ban/jail.local       the jails: Debian's sshd, and the site's own
  fail2ban/filter.d/        the app's refusal line, which the site's jail matches
  uk.osric.asp-fail2ban-metrics.service, .timer   the collector's clock, system units
  ci/                       the host's CI account: hooks, check and mirror scripts, slice and units;
                            `make ci-install` copies them up
```

The quadlets, the Caddyfile, the fail2ban configuration and the CI files under `deploy/ci` are the
source of truth for the server: `make deploy` copies the quadlets and the Caddyfile up and applies
them, and `make ci-install` installs the CI account, the hooks and the units.

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
   also installs `fail2ban`, `nftables` and `jq` — the package, not the ban: it stops the service the
   package starts for itself, and `make deploy` is what installs the host's one jail and starts it, so
   no scanner runs before the site does ([bans](operations.md#bans)). It creates a 4 GiB swap file and
   adds it to `/etc/fstab` — see [memory and swap](operations.md#memory-and-swap) for what it is for.
   It reboots once if the package upgrade installed a new kernel, and ends with a rootless `podman
   info` that must print `true` — check it with `cloud-init status --long` and `swapon --show` before
   relying on the host.

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
   quadlet and restarts that, installs the nightly snapshot script and its timer, then writes the
   Caddyfile to `/etc/caddy/` with sudo, installs the Caddy unit, reloads systemd and restarts Caddy.
   Caddy obtains a certificate on first request, so the site is reachable a few seconds later.

   The target refuses to run from a tree with uncommitted changes, because the image is tagged with
   its commit and that tag is only meaningful if the commit is the tree (`ALLOW_DIRTY=1` overrides,
   for a deliberate one-off). It runs the gate and the browser suite before the image is built, pushes
   the commit tag alongside `IMAGE_TAG`, and it does not report success until
   `http://127.0.0.1:8080/health` answers: a restart that leaves the unit failed is a failed deploy,
   not a quiet one.

   The quadlet's `Image=` names the commit, so the host answers "what is running?" without anyone
   guessing, and rolling back is a tag rather than a rebuild.

6. Prepare the CI account, once:

   ```bash
   make ci-install HOST=deploy@<server-ip>
   ```

   It creates the `ci` account, clones the host's bare repository from GitHub, installs the hooks,
   the scripts and the units, and prints the GitHub deploy key. Add that key to GitHub (Settings →
   Deploy keys) with **Allow write access**, then make the host the upstream:

   ```bash
   git remote rename origin github
   git remote add origin ci@<server-ip>:repo.git
   git fetch origin
   git branch --set-upstream-to=origin/trunk trunk
   ```

   The first `git push` after that is checked by the host; the result is at
   `https://moosemorals.com/ci/status.json`. See [CI on the host](#ci-on-the-host) for the status,
   the journal, the mirror and the failure shapes.

## Day-to-day

- **New code:** commit it, push it to the host (`git push`, once the remotes are switched), then
  `make deploy HOST=deploy@<server-ip>` once `/ci/status.json` is green. The push runs the gate in
  the host's `ci` account asynchronously ([CI on the host](#ci-on-the-host)); `make deploy` still runs
  the full gate and the browser suite, builds, pushes, pulls, restarts, waits for `/health`, and says
  so if the commit it deployed is not on the upstream yet. Take a snapshot first when the release
  could migrate: `ssh deploy@<server-ip> systemctl --user start uk.osric.asp-backup.service`. The daily
  timer covers every other night — [backups](operations.md#backups) has what that keeps and how it is
  pulled off the host.
- **The offsite copy:** it is not this host's job. The backup host pulls the newest snapshot on its own
  timer, and `make pull-install HOST=osric@<backuphost>` is how that side is installed or updated. A pull
  that has stopped shows up there, not here — [backups](operations.md#backups) has both halves.
- **The gate:** `make check` is the gate; `make ci` is the same target from a clean tree with the
  reproducibility proof; `make deploy` runs `check` and then the browser suite. The host runs `make ci`
  after a push to `trunk` in its own account ([CI on the host](#ci-on-the-host)), and `make deploy`
  remains the release command. The status, the journal and the mirror are below.
- **Domain or TLS settings:** edit `deploy/Caddyfile`, then `make deploy HOST=...`.
- **Page assistant:** the host requires a provider key at startup (`assistant-always-on`). `make deploy` keeps a mode `0600`
  `~/.config/uk.osric.asp/assistant.env` for the `deploy` user, because the container cannot start
  without that file — podman's env file, which is what the quadlet's `EnvironmentFile=` becomes, has
  no optional form. Put `Assistant__ApiKey=<provider-key>` in it, and optionally `Assistant__Model`
  or `Assistant__BaseUrl`, then redeploy. A missing key stops the host at startup with
  `Assistant:ApiKey is not set.`, and the deploy never overwrites an existing file. The configured
  provider must speak OpenAI-compatible streaming chat completions with tool calls. Local HTTP is
  allowed only for a loopback provider or when explicitly enabled.
  **The context budget is the provider's:** the model's window and output ceiling are read from the
  provider's model list (`Assistant:ModelsPath`, `/models` by default) at startup, so there is no
  `Assistant__MaxContextTokens`, `Assistant__ModelContextTokens`, `Assistant__MaxOutputTokens` or
  `Assistant__IncludeUsage` to set; a leftover override of one is not read, because a configured
  figure beside a fetched one is two answers. The prompt's share is that window less
  `Assistant__MinOutputTokens`, and a reply's allowance is computed per request. If the provider is
  unreachable when the host starts, every page still serves and the assistant refuses runs with
  `model_window_unknown` until the model list can be read; if the provider answers without the
  configured model, the deploy stops and says so. `Assistant__MaxMessageChars`,
  `Assistant__MaxToolResultChars`, `Assistant__MaxMemoryCharsPerRun` and `Assistant__MaxMemoriesPerRun`
  are gone too: those caps are now fractions of the one budget.
  The host sends the system prompt, conversation transcript, and any page source read by the assistant to
  that provider. Do not store content there that the provider must not receive. Prompts, page source,
  reasoning, credentials, and provider error bodies are not logged. The default provider is DeepSeek;
  compatible services and local Ollama can be selected by configuration. Restart the app after
  changing the environment file.
  **Upgrading from `Agent`:** the file and its keys were renamed when the feature was, so `make deploy`
  seeds a new, empty `assistant.env` beside the old `agent.env`, and the host will not start until the
  key is moved. Move the file and rename each key you still need (`Agent__ApiKey` →
  `Assistant__ApiKey`, and so on) rather than copying the contents; there is no `Enabled` key any
  more, so leave `Agent__Enabled` behind. The old file is ignored from that release on. The storage
  tables are renamed by a migration on first start, which keeps every conversation.
- **Streaming through Caddy:** a page that sends `text/event-stream` needs nothing from this proxy.
  The pinned Caddy flushes such a response as it is written — it sends the headers without waiting for
  its 512-byte minimum — and `flush_interval -1` stops it waiting for the app either, so events arrive
  one at a time. `encode` still compresses them, because `text/event-stream` matches its default
  `text/*` list, and the browser decompresses the stream as it arrives. That was measured, not
  assumed: six one-second events through this Caddyfile arrived a second apart with
  `Content-Encoding: zstd`. The exclusion below is therefore the assistant route's, not streaming's,
  and goes when that route becomes a page.
- **Assistant streaming through Caddy:** `/assistant/*/events` is excluded from compression and the
  reverse proxy flushes events immediately. Verify through the proxy with a real queued conversation
  using `curl -N`, not only against the Kestrel port. A run continues if its browser is closed; the
  `Stop` form cancels it. During graceful shutdown the host records unfinished runs as interrupted;
  the 300-second default run timeout is independent of the container's 35-second graceful stop.
- **Outbound fetch:** any `http` or `https` host is reachable, with no allow-list (`pages-outbound-fetch`).
  `Pages__Fetch__TimeoutSeconds` defaults to 4, deliberately below the 5-second render budget;
  `MaxResponseBytes`, `MaxRequestsPerRender` and `MaxRedirects` bound the rest. The container's egress
  and internal network are not otherwise restricted, and the assistant's `fetch` tool uses the same
  path, so a stored page or an approved tool call can reach anything the container can. That is the
  single-owner trust boundary, not a sandbox (`sec-page-runs-in-process`). Every request carries
  `moosemorals.com (page)` or `moosemorals.com (assistant)`, followed by
  `fetchmoose@fluffypeople.com`, as its `User-Agent`, set by the host.
  The site's own origin is reachable too: the quadlet's `AddHost=moosemorals.com:host-gateway` stops
  podman writing the host's hostname into `/etc/hosts` as `127.0.1.1`, which would otherwise point a
  page's fetch at the container's own loopback and refuse it. The entry sends the name to Caddy over
  its podman host-gateway address, so the certificate still matches the name.
- **The assistant's view:** the quadlet sets `Site__Origin=https://moosemorals.com`, which is where
  the assistant reaches this site over HTTP while a run is working. It is the public origin rather
  than the loopback publish on purpose: a view of a page that mints an antiforgery token — the editor
  is the page — needs an HTTPS request, because the antiforgery cookie is `Secure`-always in
  production. The name resolves through `AddHost` to the host gateway, so the request goes out
  through Caddy and back, with the certificate and the forwarded headers a visitor's request has.
  The token that authenticates it is minted per run, held in memory and dead when the run ends, so
  there is nothing to add to the environment file and nothing to rotate; a production host left on a
  plaintext origin logs one warning at startup.
  A page can name an operator-configured credential instead of carrying a token itself; put it in
  the same environment file and restart:
  `Pages__Fetch__Credentials__assistant__Host=api.deepseek.com`,
  `__Header=Authorization`, `__Scheme=Bearer`, `__Value=<token>` and `__Page=/assistant`. `Page` takes
  one page key, or a key ending in `/*` for that page and everything under it, which is how a page
  family and its worker share one token. The host sends the header only to that host, only from a page
  inside the binding, and only over HTTPS unless the host is loopback; a redirect to another host or
  to plaintext drops it. `Header` defaults to
  `Authorization` and may also name an `X-` header, and `Scheme` is optional. The value never
  enters the database, an export archive or a page, and a bad entry stops the app at startup.
- **Page state:** a page reaches state through `ctx.Features.GetRequired<IPageStorage>()`; it is
  stored in `PageState` in the same database, so `storage.db` carries it and a backup includes
  it. Deleting a page clears its state; editing or restoring it
  keeps it. The caps live under `Pages:Storage` (`Pages__Storage__MaxValueBytes`, and so on).
- **Page session:** a page can keep per-visitor state through
  `ctx.Capabilities.GetRequired<IPageSession>()`, stored in `PageSessions` and `PageSessionValues` in the
  same database. A visitor is identified by the `s` cookie, which is minted on the page's first write
  and cleared on sign-out; a row lives 30 minutes without a visit, or 8 hours signed in. The caps,
  lifetimes and live-session cap live under `Pages:Session` (`Pages__Session__Anonymous__MaxKeys`,
  `Pages__Session__MaxLiveSessions`, and so on), and the table is swept every five minutes. A
  session is a visitor's scratch space rather than content: an export archive never carries one.
- **Reserved page keys are not configuration.** The keys the site's own routes answer for are the
  shell's route table in code (`Web/Shell/ShellCatalogue.cs`), so a route and its reserved key cannot
  disagree. The release that removed `Pages:ReservedPaths` refuses to start while the section is
  still present, so delete it from any override rather than leaving it to look as though it does
  something.
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
- **Health:** `ssh deploy@<server-ip> curl -s http://127.0.0.1:8080/health` returns detailed JSON —
  200 when the app can reach its database and no page renders have been abandoned, 503 otherwise. The
  same probe runs inside the container as podman's health check, so a failing check restarts it; see
  [resource limits and restarts](operations.md#resource-limits-and-restarts).
- **Container health:** `ssh deploy@<server-ip> podman healthcheck run uk.osric.asp` forces a check,
  and `podman inspect uk.osric.asp --format '{{.State.Health.Status}}'` shows the current one.
- **Logs:** `ssh deploy@<server-ip> journalctl --user -u uk.osric.asp.service -f`. Each entry is one
  single-line JSON object with its scopes as fields; [the logging notes](../docs/logging.md#working-with-the-journal)
  have the levels and the `jq` recipes for reading one run or every warning.
- **Status:** `ssh deploy@<server-ip> systemctl --user status uk.osric.asp.service`
- **Bans:** the host runs two fail2ban jails: Debian's `sshd`, and `uk.osric.asp`, which bans for an
  hour an address that fails authentication ten times inside a minute — a sign-in, a stale view
  token, a page's redirect to the sign-in form ([bans](operations.md#bans)). Read and undo it from
  the host:
  `sudo fail2ban-client status uk.osric.asp` and
  `sudo fail2ban-client set uk.osric.asp unbanip 203.0.113.7`.
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

## CI on the host

`make ci-install HOST=deploy@<server>` prepares a separate `ci` account on the same host. A push to
`trunk` is accepted, a `post-receive` hook starts `make ci` in a transient systemd unit, and the
result is written to `/var/lib/uk.osric.asp-ci/status.json` and served by Caddy at
`https://moosemorals.com/ci/status.json`. The app has no route or mount for it, and the status still
answers when the app is down.

- **The address:** `ci@<server>:repo.git` is the bare repository. The account's login shell is
  `git-shell` and it has no sudo; the owner's unrestricted keys are copied, so the same SSH identity
  that reaches `deploy` can push, and that is all it can do. The backup host's restricted read key is
  not copied.
- **The gate:** `make ci` is the same target as always. `uk.osric.asp-ci.slice` caps it at 2 vCPU and
  4 GiB and deprioritizes it against the app. The full output is the journal of the unit the push
  names; `sudo journalctl _SYSTEMD_USER_UNIT=uk.osric.asp-ci-<short>-<pid>` reads it.
- **The deploy key:** `ci-install` generates `~ci/.ssh/id_ed25519_github` and prints its public half.
  Add it to GitHub with **Allow write access**. It is the only GitHub credential on the host, and it
  can write this repository and nothing else.
- **The mirror:** `post-receive` starts `uk.osric.asp-mirror.service`, so the mirror copies what was
  pushed and runs on no clock at all. A failed run restarts itself every minute for ten minutes, and
  the next push starts a fresh window. It pushes the host's branches and tags with `--prune`, so a
  branch deleted on the host is deleted on GitHub. The bare repository is cloned from GitHub at
  install, so the first mirror has nothing to delete.
- **The status:** `never-run` before the first push, `running` while `make ci` is in the unit, then
  `passed` or `failed` with the commit, the times and the failing target. The file is written with an
  atomic rename; Caddy sends `Cache-Control: no-store`.
- **The remote switch:** after `ci-install`, make the host the upstream:

  ```bash
  git remote rename origin github
  git remote add origin ci@<server>:repo.git
  git fetch origin
  git branch --set-upstream-to=origin/trunk trunk
  ```

  `git push` is then a push to the host; `git push github trunk` is the deliberate, ungated
  exception when the host is unreachable.
- **A red status:** trunk is accepted before it is checked. A failed status is fixed forward; there
  is no automatic revert. `make deploy` still runs the full gate and the browser suite, so a red
  status is a signal to look, not a lock on the release.

`ci-install` checks for `git`, `make`, `jq`, `reuse`, `curl`, `libicu-dev` and the rootless podman
pieces, installs what `apt` has, adds Microsoft's apt repository and installs `dotnet-sdk-11.0` — the
major.minor in `global.json`; `global.json`'s `rollForward` picks the patch. `deploy/ci/` is the source
of truth for the hooks, the scripts and the units, as `deploy/` is for the quadlets.

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

The target restores the repository's pinned local `dotnet-ef` tool (11.0.0), builds using the normal
Makefile target, and writes migration sources to the Web project's `Storage/Migrations` directory.
EF 11 reads the evaluated MSBuild output paths, including `artifacts/obj/uk.osric.asp.Web/`. Commit and review the generated migration
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
