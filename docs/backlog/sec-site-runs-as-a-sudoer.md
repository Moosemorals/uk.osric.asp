<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The site runs as the account that administers the host

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | high |
| Severity | Medium |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Security, deployment |
| Location | `deploy/cloud-init.yaml`, `deploy/deploy-host`, `Makefile` |
| Spike | — |

## Problem

[`cloud-init.yaml`](../../deploy/cloud-init.yaml) creates one account, `deploy`, with
`sudo: ALL=(ALL) NOPASSWD:ALL` and the owner's two SSH keys. Everything the site is and everything it
owns belongs to that account:

- the user quadlets in `~deploy/.config/containers/systemd` — the app, Prometheus, node-exporter and
  the network — and the user manager that starts them at boot;
- the per-user podman store: the `uk.osric.asp-storage` and `uk.osric.asp-keys` volumes, the image
  layers, and the registry credential at `~/.config/containers/auth.json`;
- the app's configuration and secrets: `~/.config/uk.osric.asp/assistant.env`, which holds the
  provider key, and `appsettings.Production.json`;
- the nightly snapshot, its script and the copies it keeps under `~/backups`, including a copy of
  `assistant.env` beside `storage.db`;
- the login the owner's machines use to deploy and to administer the host.

So the account with `NOPASSWD: ALL` is the account that runs internet-facing code whose whole premise
is that a page is arbitrary C#. The container is a real boundary — rootless, `User=1654:1654` inside a
user namespace, `NoNewPrivileges=true`, `DropCapability=all`, no socket and no host path — and a page
today cannot reach the host account. The item is not that it can. It is that there is one account on
the other side of the boundary, and everything the site needs sits on it: a container escape, a
runtime bug or a page that reaches a file it should not all end at the same place, and that place can
`sudo` to root without a password.

The host's other account already follows the rule this one breaks: `ci` runs the check and has no
sudo. [`caddy-is-still-rootful`](caddy-is-still-rootful.md) proposes the same for the public edge —
its own account, so it cannot reach the app's keys or quadlets. The app is the remaining tenant on
the administrator's account, and [the security
review](../../SECURITY-REVIEW.md#hardening-not-vulnerabilities) lists the container's writable root
and Caddy's rootful unit as the hardening still to do without mentioning that the account running the
site is the account that administers it.

[`sec-page-runs-in-process`](sec-page-runs-in-process.md) is the process boundary between a page and
the site. This is the account boundary between the site and the host, and it is the cheaper of the
two: it needs no runtime work at all.

## The shape it would take

Two accounts, one job each: `deploy` stays the human and the automation, and a new account — `app`
below, named for the network alias the app already answers on — owns everything the site runs
from.

- `deploy` keeps the SSH keys and `sudo`. It stops owning a unit, a volume, an image or a secret, and
  starts nothing itself.
- `app` has no sudo, no login shell and no SSH key of its own, the subuid/subgid range rootless podman
  needs, and lingering so its units start at boot. Its home holds the quadlets, the volumes, the
  image store, `assistant.env`, `appsettings.Production.json` and `~/backups`.

`make deploy` keeps `HOST=deploy@server` and its one call to [`deploy/deploy-host`](../../deploy/deploy-host),
and every host step that runs as `deploy` today runs as `app` instead, through root: files are
installed with `install -o app -g app`, and `systemctl --user` becomes
`systemctl --machine=app@.host --user` (or `sudo -u app` with `XDG_RUNTIME_DIR=/run/user/$(id -u app)`).
`REMOTE_AUTHFILE` moves to `/home/app/.config/containers/auth.json` and the pull becomes
`sudo -H -u app podman pull`, so the image lands in the store whose units run it. The gate and the
control socket are unchanged (`code-one-gate`), and so is the rule that the quadlets are the source of
truth (`deploy-quadlets-are-the-source-of-truth`); only the home they are written into changes.

Three things move rather than being reinstalled:

- **The volumes.** A rootless store is per user, so `uk.osric.asp-storage` — every page revision and
  every account — and `uk.osric.asp-keys` — the data-protection keys, without which every editor is
  signed out and every open form fails — are exported as `deploy` and imported as `app`, with the
  ownership fixed inside the namespace by `podman unshare chown 1654:1654`.
- **The secrets.** `assistant.env` and `appsettings.Production.json` are written as `app`, 0600, and
  the deploy account keeps no copy. The registry credential is entered once as `app`.
- **The snapshot.** The timer, the script and `~/backups` move with the site, and with them the backup
  host's read-only key: `restrict,command="/usr/bin/rrsync -ro /home/app/backups"` belongs in `app`'s
  `authorized_keys` rather than `deploy`'s. The key still reads and does nothing else.

The one step cloud-init cannot do is on a host that already exists: cloud-init runs once, so the live
host gets the account, its ranges and its linger from a runbook in
[operations](../../deploy/operations.md), proved on a fresh host first. The old units are disabled,
not deleted, and the deploy store is kept until the app account has run a full day — the snapshot
already taken, and the copy the backup host pulls, are the rollback.

What it buys:

- A compromise of the site stops at an account with no sudo, no SSH login and no other account's
  secrets. The blast radius is the site's own data and the provider key it already holds.
- The host has one documented path from nothing to root, on an account that runs no service, instead
  of a path that begins with the web server.
- It makes the rule uniform: one account per job, as the `ci` account already does and
  [`caddy-is-still-rootful`](caddy-is-still-rootful.md) proposes.

What it does not do:

- It does not remove the container as the boundary. The app account shares the host kernel, so a
  kernel bug is still a kernel bug; what changes is where an escape lands.
- It does not hide the site's own secrets from the site. `assistant.env` is the app's file, and the
  app must read it.
- It does not take `sudo` away from `deploy`, which still installs Caddy, the fail2ban jails and the
  CI status directory. The split is about what the workload can reach, not about what the
  administrator can.

## What it touches

- [`deploy/cloud-init.yaml`](../../deploy/cloud-init.yaml): the account, its subuid/subgid ranges, its
  linger, and no sudo.
- `deploy/uk.osric.asp.container`, `deploy/prometheus.container`, `deploy/node-exporter.container`,
  `deploy/uk.osric.asp.network` and `deploy/uk.osric.asp-backup.{service,timer}`: installed into
  `app`'s user manager, where `%h` is app's home.
- [`deploy/deploy-host`](../../deploy/deploy-host): every user-level command becomes the app user's,
  and the registry login and each installed file name the account they belong to.
- [`Makefile`](../../Makefile): `REMOTE_AUTHFILE`, and the `remote-*` targets, whose `podman exec` runs
  in the app's store.
- `deploy/backup-snapshot.sh`: its comment says it runs as `deploy` because that user owns the volume,
  and the sentence becomes about `app`.
- `deploy/README.md`, `deploy/operations.md`: the registry path, `podman images` under `~app`, the
  journal commands, the backup host's key, the accounts, and the migration.
- [`SECURITY-REVIEW.md`](../../SECURITY-REVIEW.md): the hardening list gains the account boundary, and
  the host's accounts are written down.

## What would prove it

- `sudo -l -U app` refuses, `getent group sudo` does not list `app`, and `ssh app@<server> true` is
  refused from the owner's machines; the only key in app's `authorized_keys` is the backup host's,
  restricted to `rrsync -ro /home/app/backups`.
- `ps -eo user,command` shows the app, its `conmon`, Prometheus, node-exporter and the snapshot running
  as `app`, and `podman images` as `deploy` no longer shows the site's image.
- The site answers `/health` after a reboot with nobody logged in; the editor's sessions and an open
  form survive the move, which is the keys volume; `/ci/status.json` still answers.
- The assistant answers: its key moved with the site, and `~deploy` holds neither `assistant.env` nor
  `auth.json`.
- The nightly snapshot runs as `app`, the backup host pulls today's file, and its `rrsync` still
  refuses a path outside `~/backups`.
- `make deploy` is green from an empty control socket, and the `remote-user-*` targets still open a
  terminal on the app's container.

## Considered and rejected

- **Keep the account and remove its sudo.** [`make deploy`](../../deploy/deploy-host) needs root for
  Caddy, the fail2ban jails and the CI status directory, so the rule would have to name every command
  the host steps call — and the site's units, volumes and secrets would still sit in the account that
  administers the host. The split is where the boundary belongs; narrowing `deploy`'s rule to the
  commands the deploys use is a second, smaller step this item leaves room for.
- **Run the app as a root system quadlet.** That removes the account but puts the site in root's store,
  which is a larger grant than the app account, and loses the user namespace the container relies on.
- **Give every unit its own account.** Prometheus and node-exporter read only what the site publishes
  and the host's textfile metrics, so one account for the site's own stack is the boundary. A second
  one buys nothing until there is another tenant.
