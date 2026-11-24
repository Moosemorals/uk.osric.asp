<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# No outbound notifications when the site breaks

| | |
| --- | --- |
| Difficulty | eager-beaver |
| Utility | medium |
| Severity | Medium |
| Status | open |
| Blocked by | — |
| Area | Operations, deployment |
| Location | `deploy/`, `src/uk.osric.asp.Web` |

## Problem

Nothing tells anyone when the site is down. The quadlets restart a crashing app up to three times and
then give up (see [deployment](../../deploy/operations.md#resource-limits-and-restarts)), and `Restart=always` is otherwise silent: a failed unit, a full disk, a
startup migration that stops the app, or Caddy losing its certificate all sit unnoticed until someone
happens to look. There is no outbound mail, so systemd's failure notifications have nowhere to go.

## Fix outline

The alert does not have to leave the host. [The email plan](../plans/email.md) delivers the host's own
mail to a mailbox on the same host, and that is the relay this item wanted — so there is no provider
to pick, no `SPF include:` and no credential.

1. Depend on the email plan's local corridor: the loopback listener on `127.0.0.1:2525`, `msmtp` as
   `/usr/sbin/sendmail`, and `root@` aliased to the owner's mailbox.
2. Nothing in DNS: a message that never leaves the host is never checked against SPF, DKIM or DMARC.
3. Point systemd at it:
   - `OnFailure=` (and `OnUnitInactiveSec=`) on the `uk.osric.asp` user unit and the `caddy` system
     unit;
   - a small timer that curls `/health` and alerts when it returns 503, tying the health check to a
     human.
4. Ship `msmtprc` and `mailname` through cloud-init and `make deploy`, exactly as the quadlets are
   shipped today.
5. Accept the blind spot deliberately: mail about a broken host travels through that host, so a dead
   pod or a full disk is still silent. Add an out-of-band channel (a push from an external monitor)
   if that matters more than the mail does.
