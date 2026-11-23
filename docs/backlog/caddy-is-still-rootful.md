<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Caddy is the only rootful unit, and the mail plan's sysctl would free it

| | |
| --- | --- |
| Difficulty | caffeinated-squirrel |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Operations, deployment |
| Location | `deploy/caddy.container`, `deploy/deploy-host`, `deploy/host/` |
| Spike | — |

## Idea

[The email plan](../plans/email.md) ships `deploy/host/25-unprivileged-ports.conf`, one `sysctl.d`
file — `net.ipv4.ip_unprivileged_port_start=25` — that `make mail-deploy` installs so a rootless pod
can publish 25, 587 and 993 through `rootlessport`. The setting is host-wide and its floor covers 80
and 443 as well; binding those two is the only reason
[`caddy.container`](../../deploy/caddy.container) is still a system quadlet. Caddy has never needed
root for anything else: the image's own root inside a user namespace is enough once the floor is
lowered.

Caddy is the last rootful unit on the host, so this is what makes every container rootless. It is
also cheapest before the mail plan lands: its root `mail-cert-sync` timer exists only because Caddy's
store is root-owned, so a store that belongs to a user turns that timer into a user one (Decision 10),
and a mail plan built afterwards has one root dependency fewer.

What it costs is the one thing the mail plan already states: with the floor at 25 any local user can
bind a privileged port. One human, `deploy`, `vmail` and one proxy user are every local account there
is, which is bounded and visible; a second tenant is the moment to revisit it.

## What it touches

- **The first check.** A rootless container on host networking binding 80 with the floor lowered is
  the one part not already proved — the mail plan's spike proves `rootlessport`, not a direct bind. If
  it refuses, a socket-activated or capability-granted bind is the alternative, and the item is worth
  less.
- **The unit.** `caddy.container` moves from `/etc/containers/systemd` to a user's
  `~/.config/containers/systemd`, and its `[Install]` from `multi-user.target` to `default.target`
  with `loginctl enable-linger`, like the app's.
- **The user.** Its own account, not `deploy`: the public edge must not reach the app's SSH keys,
  registry auth file or quadlets. `make deploy` then writes that account's units as root, as
  `mail-deploy` writes `vmail`'s.
- **The Caddyfile.** `/etc/caddy/Caddyfile` stays root-owned, 0644 and mounted read-only. The
  container only reads it, and no other user can rewrite what the edge serves.
- **The store.** `caddy-data` and `caddy-config` live in root's store; copying them into the user's
  keeps the ACME account and the certificates, and reissuing instead meets Let's Encrypt's weekly
  limit. Ownership is fixed inside the user namespace (`podman unshare chown`).
- **The CI status.** `/var/lib/uk.osric.asp-ci` is 0755 with a 0644 file, so a rootless read-only
  mount sees it unchanged.
- **The rest.** Host networking, the app's loopback port and the fail2ban jail do not change; the
  deployment docs and [deploy-host](../../deploy/deploy-host) stop calling Caddy the rootful unit.

## What would prove it

`ss -ltnp` shows 80 and 443 held by a process in the user's namespace and no rootful container on the
host; the served certificate is the one copied from the old store; `/ci/status.json` still answers;
`sudo -iu <user> -- systemctl --user status caddy` is running after a reboot with nobody logged in;
and the mail plan, when it lands, has one root timer fewer.
