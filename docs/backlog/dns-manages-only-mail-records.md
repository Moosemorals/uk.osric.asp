<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The host manages only the mail records, so the site's names are still hand-edited

| | |
| --- | --- |
| Difficulty | eager-beaver |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Area | Operations, deployment |
| Location | `deploy/dns/` |
| Spike | — |

## Problem

[The email plan](../plans/email.md) gives the host a Mythic Beasts key and a reconcile step, but
scopes the key to the mail records on purpose: the apex `MX` and `TXT`, `neith`'s `A`, `AAAA` and
`TXT`, and the `_domainkey`, `_dmarc` and `_smtp._tls` records. The apex `A`/`AAAA`, the `CAA` record
and anything else the site depends on stay outside its reach, so they are still edited by hand and
still drift from anything in this repository.

The owner said yes to widening it, on the backlog rather than in the mail plan, because the narrow key
is what makes the mail plan safe to land and widening is the easy direction.

## Fix outline

1. Ask Mythic Beasts for a key (or a second key) that can write every record the site owns — the
   apex `A`/`AAAA`, `CAA`, and the names the CI status and any future service use.
2. Add those records to `deploy/dns/records`, with the addresses still read from the Hetzner API
   rather than typed, and keep the add-and-update-never-delete rule that protects verification
   records at the apex.
3. Run the reconcile in plan mode against the live zone first: the diff is the audit of everything
   the host would take over, and anything unexpected is the owner deciding whether it is still wanted.
4. Rotate the credential on the host, revoke the narrow key, and record the widened scope in
   `deploy/README.md` beside the other secrets.
5. Leave the PTR where it is: it is still a Hetzner setting and still only the mail host's.

Widening is cheap now and gets more expensive as more is added by hand, so the item is medium
utility rather than high: nothing is broken, and the drift it prevents is slow.
