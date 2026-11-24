#!/bin/bash
# SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
# SPDX-License-Identifier: MIT

# Creates the host user the mail pod runs as. Idempotent: a second run changes nothing.
#
# `vmail` is a service account, not a person: no shell, no password, and a home that holds the mail
# volumes, the DKIM keys and the certificate copy. It is deliberately not the deploy user, so a
# compromised Postfix or Dovecot lands on a uid that owns mail and cannot read the site's database or
# the deploy user's keys. Rootless podman needs a subuid range even for a user that never logs in.
#
# The range is fixed rather than allocated, because a subuid mapping that moved between runs would
# reinterpret every file the containers had already written.
set -euo pipefail

user=vmail
subid_start=296608
subid_count=65536

if id "$user" >/dev/null 2>&1; then
    echo "$user exists"
else
    useradd --system --create-home --home-dir "/home/$user" \
        --shell /usr/sbin/nologin --comment "mail stack (rootless podman)" "$user"
    echo "$user created"
fi

# A range already present is left as it is; a second `usermod --add-subuids` would add a second one.
if ! grep -q "^$user:" /etc/subuid; then
    usermod --add-subuids "$subid_start-$((subid_start + subid_count - 1))" \
            --add-subgids "$subid_start-$((subid_start + subid_count - 1))" "$user"
    echo "subuid range added"
fi

# Linger is what makes the user manager, and so the pod, start at boot with nobody logged in.
loginctl enable-linger "$user"

id "$user"
grep "^$user:" /etc/subuid /etc/subgid
loginctl show-user "$user" --property=Linger,State
