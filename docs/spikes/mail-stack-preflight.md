<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spike: the mail stack's five assumptions

Measured on 2026-11-23 on the dev box (Debian 13 trixie, podman 5.4.2, pasta from `passt`) and on the
host itself (`moosemorals.com`, Hetzner CAX21, Debian 13.8, kernel 6.12, podman 5.4.2), before [the
email plan](../plans/email.md) is built. Components: rspamd 4.3.1 and Dovecot 2.4.5, both from their
published container images.

[Phase 0](../plans/email.md) of the plan exists to turn five claims into measurements, because each one
either gates a decision or would change a decision that is expensive to change later. Two of the five
came back different from what the plan assumed, and one came back the same but for a different reason
than the plan gave.

**Verdict: the plan is buildable, with six corrections.** The host-loopback path works but not at
`127.0.0.1`; the published port reports the host's own address rather than the pod's gateway; per-account
Bayes works and needs `add_map` rather than the named lookup the plan implied; Dovecot 2.4's static
userdb takes `userdb_fields`, and `+detail` is an LMTP feature that authentication does not implement.

## Hypotheses

| # | Hypothesis | Result |
| --- | --- | --- |
| P1 | A container in `vmail`'s pod reaches the site's loopback listener | **pass, with a different address** — not `127.0.0.1`, which is the pod's own loopback; a mapped literal works, and the host sees `127.0.0.1` as the client |
| P2 | Rootless podman can publish 25 | **fail without one sysctl** — pasta binds host-side and is refused: `Failed to bind port 25 (Permission denied)` |
| P3 | A published port presents a pinned pod address in `mynetworks` | **fail** — the container sees the host's own address (`192.168.0.122` here), not the pod gateway |
| P4 | Bayes can keep one corpus per account from a map lookup | **pass** — with `rspamd_config:add_map`; the name-keyed `get_maps()['NAME']` silently yields nil |
| P5 | Dovecot 2.4 resolves `user+detail` to `user` on both sides | **half** — LMTP splits the detail, authentication does not; and the static userdb's fields are spelled differently |
| P6 | Rspamd's `external_map` can key settings on the SMTP recipient | **pass** — `id('rcpt');rcpts('smtp'):addr` sends `{"rcpt":["shopping@moosemorals.com"]}`, and the response is applied; but the response can also **reject**, which the plan forbids |

## P1 — reaching the site's loopback from the pod

Podman's default pasta command line, from `podman --log-level=debug run`:

```
pasta arguments: --config-net --dns-forward 169.254.1.1 -t none -u none -T none -U none
                 --no-map-gw --quiet --netns /run/user/1000/netns/… --map-guest-addr 169.254.1.2
```

`-T none --no-map-gw` is podman turning host-loopback mapping **off**, so nothing at the site's
`127.0.0.1:8080` is reachable from a container by default. Options passed after `pasta:` are placed
before those defaults and win:

```sh
podman pod create --name spikepod --network 'pasta:--map-host-loopback=169.254.42.42'
podman run -d --pod spikepod alpine …
podman exec … curl http://169.254.42.42:8080/      # 200, body served by the host's 127.0.0.1:8080
```

The echoing server on the host logged `client=127.0.0.1`: the translation is to loopback, as
`pasta(1)` says. Two consequences:

- **The address is a literal the mail side uses, and it is not `127.0.0.1`.** `--map-host-loopback`
  takes a *guest-routable* address; `127.0.0.1` is rejected (`Invalid address to remap to host`), and
  `-T` is the namespace-side port option in this pasta, not the mapping. One address from the
  documentation range is enough, written once in the pod unit and once in the two callers.
- **The pod carries the option, not each container**, so all four containers share the mapping.

## P2 — publishing 25 rootless

```sh
podman run -d -p 127.0.0.1:25:2525 …
Error: pasta failed with exit code 1:
Failed to bind port 25 (Permission denied) for option '-t 127.0.0.1/25-25:2525-2525'
```

The publisher is pasta, running in the host namespace as the user, so the host's
`net.ipv4.ip_unprivileged_port_start` applies, not the container's. It is `1024` here and the plan's one
`sysctl.d` file is the fix. Not changed on this box: it is host-wide and outlives the spike. **The
correction to the plan is only that the binder is pasta rather than `rootlessport`**, so the failure
appears at container start rather than at first connection.

## P3 — what a published connection looks like inside

```sh
# host: connect to 127.0.0.1:2525, published to a container listener that prints its peer
server said: peer was 192.168.0.122      # the host's own address on its LAN interface
```

`ss -ltnp` shows `pasta.avx2` holding `127.0.0.1:2525`, so forwarding is pasta's, and it preserves the
source address instead of NATing it to a gateway. That is good news for 25 and 587 — a remote client's
address reaches Postfix, so SPF and RBL are evaluating the real peer — and a correction for 2525: the
plan's `mynetworks` of "the pod's loopback plus the address the publication arrives from" is right about
the shape and wrong about the value. It is **the host's own address**, which `mail-deploy` knows and can
write, not a pod gateway that follows backend creation order.

## P4 — per-account Bayes

The classifier accepts a Lua string for `per_user`, compiled with `luaL_dostring` and called per task;
the value it returns is the corpus key (`rspamd_sqlite3_get_user`). Two findings:

- **`rspamd_config:get_maps()` returns an array, not a name-keyed table.** The first attempt,
  `get_maps()['ALIASES']`, returned nil, and because the extractor fell back to the recipient the learns
  landed under `shopping` — the failure mode the plan calls out, reproduced in one line.
- **`rspamd_config:add_map{ type = 'map', url = 'file:///etc/rspamd/aliases.map' }` inside the
  `per_user` chunk works**, and the map is loaded before the first message.

With that, and a synthetic corpus learned through two different alias spellings:

| Scan recipient | Corpus key | Result |
| --- | --- | --- |
| `osric@moosemorals.com` | osric | `BAYES_HAM` |
| `shopping@moosemorals.com` (alias) | osric | `BAYES_HAM` — same corpus |
| `osric+probe@moosemorals.com` (plus) | osric | `BAYES_HAM` — the detail is dropped before the lookup |
| `alice@moosemorals.com` (another account) | alice | no `BAYES_*` — a separate, empty corpus |

Two smaller things the run exposed: the image does not create `/var/lib/rspamd/bayes`, so the entrypoint
must (`cannot open sqlite directory`), and the shipped `learn_condition` refuses a learn the classifier
already agrees with — with a 1:19 class imbalance it skipped about 40% of the seeding learns, so
`make mail-learn` must report skips rather than assume every message landed.

## P5 — Dovecot 2.4's spellings and the detail

Three corrections, all from `doveconf` refusing the 2.3 form:

- **The static userdb's fields are not settings.** `userdb static { uid = vmail }` fails:
  `Unknown setting: uid`. They go in `userdb_fields { uid = vmail … }`, with
  `userdb_static_allow_all_users = yes` deciding whether a name that is not otherwise known resolves.
- **`auth_username_format` default is `%{user | lower}`**, the 2.4 variable syntax, not `%Ln`; `doveadm
  user OSRIC@…` lowercases and keeps the domain, so which component strips a domain is the passdb's
  business under this design rather than the config's.
- **The Lua passdb can name the account.** `return dovecot.auth.PASSDB_RESULT_OK, { user = "osric" }`
  authenticates `osric@moosemorals.com` as `user=osric`, `original_user=osric@moosemorals.com` — so the
  address presented and the user name used downstream are separate, which is what the plan needs.

**`recipient_delimiter` is a delivery feature only.** `recipient_delimiter` appears nowhere in
`src/auth`: the split is `smtp_address_detail_parse_temp` in `src/lmtp/lmtp-recipient.c`, which yields
the user name and the detail for delivery. So plus addresses are resolved by LMTP and are *not* an
identity: `doveadm auth test 'osric+shop' …` fails without consulting the passdb (and not because of
`recipient_delimiter`, which was tested empty, nor `auth_username_chars`, which was opened to `+`).
Untested further because the design does not need it — a human logs in as their account's address, and
the plan says so.

## On the host: what Phase 0 could not fake

Run as `deploy` over `ssh`, with `sudo`. Everything below is a measurement of the CAX21, not the dev box.

| Item | Value |
| --- | --- |
| Addresses | `91.98.155.87/32` and `2a01:4f8:c016:b770::1/64` on `eth0` |
| Disk | 75 G, 16 G used, 57 G available — 5 GB a mailbox is nothing |
| `ip_unprivileged_port_start` | was 1024; now 25, from `/etc/sysctl.d/25-unprivileged-ports.conf` |
| `vmail` | uid 988, `/home/vmail` `0700`, `/usr/sbin/nologin`, subuid/subgid `296608:65536`, `Linger=yes` |
| Published 25 as `vmail` | **works**, once the sysctl is in place |
| Peer a remote client presents | `81.141.12.20` (v4) and `2a00:23c7:…` (v6) — **preserved** |
| Peer a connection from the host to its own public address presents | `169.254.1.2`, i.e. pasta's `--map-guest-addr`; a hairpin, not a case that matters |
| Caddy's certificate | `/var/lib/containers/storage/volumes/caddy-data/_data/caddy/certificates/acme-v02.api.letsencrypt.org-directory/moosemorals.com/`, `root:root 0600`, **EC P-256**, SAN `moosemorals.com` only |
| Copy [Decision 11](../plans/email.md) describes | `0644` certificate and `0640` key, `vmail:vmail`, readable by the daemons — done by hand and removed again |
| A quadlet in `vmail`'s `~/.config/containers/systemd` | `generated`, `WantedBy=default.target`, active with no session open |
| Mythic Beasts token | present on the dev box at `~/.tokens/mythic-beasts.token` as `<key id>:<secret>`, but **the auth service refuses it** — `401 invalid_client` — so the key's scope is still untested |

**The API's authentication is a two step, and the plan assumed one.** The key is not used directly:
it is exchanged at `https://auth.mythic-beasts.com/login` with `grant_type=client_credentials` for an
`access_token`, which then goes in a `Bearer` header to `api.mythic-beasts.com`. Two properties of that
token matter to `dns-sync`:

- **it is bound to the IP that obtained it**, so a tool that logs in from one machine and writes from
  another fails;
- **it expires in 300 seconds**, extended by each use, so a long reconcile run must either keep using
  it or log in again.

`dns-sync` therefore logs in at the start of every run rather than holding a token, which is also why
the credential on the host is the key and not a token.

**The credential found does not authenticate.** Both documented paths reject it — the auth service
answers `401 invalid_client` for `Basic <key id>:<secret>` and for the reversed pair, and the DNS API
answers `401 Invalid username or password` for Basic directly. The file holds `<12 chars>:<17 chars>`;
the bash history of an earlier attempt used a different key id, so a re-created key whose secret did not
survive an edit is the likeliest explanation. Nothing was written to the zone, and the scope question
stays open until a key that logs in exists.

Three things the host taught that no local spike could:

- **`sudo -iu vmail` does not work**, and the plan's make targets say it does. `This account is
  currently not available` is `nologin` refusing `-i`. The working invocation names the runtime
  directory, because a user with no session still has one courtesy of linger:
  `sudo -u vmail -- env HOME=/home/vmail XDG_RUNTIME_DIR=/run/user/988 …`, and the process starts in
  the caller's directory, so `cd` first or podman fails with `cannot chdir to /home/deploy`.
- **A v4 `PublishPort=25:25` serves IPv6 as well**, but only to a listener that accepts it: the socket
  is dual-stack, and the first v6 attempt returned nothing because the test listener bound `0.0.0.0`.
  Postfix binds `inet_protocols = all`, so inbound mail over v6 works with no change.
- **The certificate is EC, not RSA.** Both daemons accept it, and nothing in the plan assumed RSA, but
  a `ssl_dh` or cipher list copied from an RSA tutorial would be wrong.

Not done, and why: no reboot, so "starts at boot" is proven as far as a generated unit plus `Linger=yes`
allows, not by pulling the power. The ufw rules opened for the peer test (`25`, `587`) were removed
again, and the test containers, the quadlet, the certificate copy and the images are gone: the host now
carries the sysctl and the user and nothing else.

## P6 — the settings door

The plan lets the site answer Rspamd's per-recipient settings, and left one question for a spike: can a
selector name the SMTP recipient? The answer is yes, but not in the spelling the plan used.

- **`rcpt` is not a selector.** `selector = "id('rcpt');rcpt;…"` fails at startup with `selector rcpt is
  unknown`, and the whole `external_map` is dropped — leaving a site that looks wired up and is never
  asked.
- **`to('smtp')` returns a table**, which the pair-building post-processor turns into nonsense
  (`{"table: 0x7f…":[…]}`).
- **`id('rcpt');rcpts('smtp'):addr` is the shape that works**, and the request body is
  `{"rcpt":["shopping@moosemorals.com"]}` — a JSON array even for one recipient, because a message can
  have several.
- **The response is applied.** `{"symbols":{"SPIKE_SETTINGS_SYMBOL":5.0}}` adds the symbol, and
  `{"actions":{"reject":0.05,"greylist":1000.0}}` turns `Action: no action` into `Action: reject`. An
  answer of `500` is logged and ignored, so the site being down costs settings, not mail.

**That last one is a hole in an invariant the plan treats as absolute.** "Rspamd may never reject" is
enforced by `reject = 1000` in the config, and a settings response can lower it. The endpoint must
therefore never emit an `actions` block, and `mail-check` must assert the shape of what it answers —
otherwise the site, or anything that can answer for it, can start rejecting mail.

## What this rules out

- **`127.0.0.1:8080` as the mail side's view of the site.** It is the pod's own loopback.
- **A pinned pod gateway in `mynetworks`.** The value is the host's address.
- **`get_maps()['NAME']` as the way to reach a map from a classifier callback.**
- **`userdb static { uid = … }`, `auth_username_format = %Ln` and `recipient_delimiter` as an auth
  setting** — all three are 2.3 or simply wrong for 2.4.
- **Plus addresses as a login identity.** They are for senders.
- **`sudo -iu vmail` in a make target**, and any mail command that runs from the caller's directory.
- **`rcpt` as a selector name**, and `to('smtp')` as the way to name a recipient.
- **An `actions` block in the settings endpoint's answer.** It is the one thing that can make Rspamd
  reject.

## Recommendation

Adopt the plan with the six corrections above, which [the plan](../plans/email.md) now carries, and
with the sysctl and the `vmail` user installed on the host by `deploy/host/`. What is left of Phase 0: a
Mythic Beasts key that authenticates, so that the scope test (writing the apex `A` and expecting `403`)
can be run at all, and a reboot to see a `vmail` quadlet come up on its own. The settings door's shape is settled; the
per-account Bayes fallback this spike was going to ask about is not needed, because the extractor reads
a cached map.
