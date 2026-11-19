#!/bin/bash
# SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
# SPDX-License-Identifier: MIT

# Writes fail2ban's numbers as Prometheus text into the directory node_exporter serves. The timer
# uk.osric.asp-fail2ban-metrics.timer runs it as root because the fail2ban socket is root-only; the
# container that reads the file is rootless and never talks to fail2ban.
#
# A jail that cannot be asked fails the run and leaves the last file alone, so
# fail2ban_collected_timestamp_seconds stops moving: the age panel is how a collector that stopped is
# told apart from two quiet jails.
set -euo pipefail

jails=(sshd uk.osric.asp)
# The directory node-exporter mounts: deploy/node-exporter.container names the same path in its
# Volume=, and it has to be the same one for the file to be served at all.
directory=/var/lib/node-exporter/textfile
file=$directory/fail2ban.prom
temporary=$file.$$

trap 'rm -f "$temporary"' EXIT INT TERM

# One counter out of a jail's status. The tree's ornaments are deliberately not parsed: the label's
# colon is the anchor, and a jail that answers without a number is an error rather than a zero.
counter() {
    if ! status=$(fail2ban-client status "$1" 2>/dev/null); then
        echo "uk.osric.asp-fail2ban-metrics: cannot ask $1" >&2
        return 1
    fi

    value=$(sed -n "s/^.*$2:[[:space:]]*\([0-9][0-9]*\).*$/\1/p" <<<"$status" | head -n 1)
    if [ -z "$value" ]; then
        echo "uk.osric.asp-fail2ban-metrics: $1 answers no '$2'" >&2
        return 1
    fi

    echo "$value"
}

# Every family's HELP and TYPE come before its samples, and one family's samples do not interleave
# with another's: that is the exposition format, not tidiness.
{
    echo '# HELP fail2ban_jail_banned_current Addresses the jail is holding off right now.'
    echo '# TYPE fail2ban_jail_banned_current gauge'
    for jail in "${jails[@]}"; do
        banned=$(counter "$jail" 'Currently banned') || exit 1
        echo "fail2ban_jail_banned_current{jail=\"$jail\"} $banned"
    done

    echo '# HELP fail2ban_jail_failed_current Failures the jail is counting inside its window.'
    echo '# TYPE fail2ban_jail_failed_current gauge'
    for jail in "${jails[@]}"; do
        failed=$(counter "$jail" 'Currently failed') || exit 1
        echo "fail2ban_jail_failed_current{jail=\"$jail\"} $failed"
    done

    echo '# HELP fail2ban_jail_banned_total Bans the jail has made since it was last loaded.'
    echo '# TYPE fail2ban_jail_banned_total counter'
    for jail in "${jails[@]}"; do
        banned=$(counter "$jail" 'Total banned') || exit 1
        echo "fail2ban_jail_banned_total{jail=\"$jail\"} $banned"
    done

    echo '# HELP fail2ban_jail_failed_total Failures the jail has seen since it was last loaded.'
    echo '# TYPE fail2ban_jail_failed_total counter'
    for jail in "${jails[@]}"; do
        failed=$(counter "$jail" 'Total failed') || exit 1
        echo "fail2ban_jail_failed_total{jail=\"$jail\"} $failed"
    done

    echo '# HELP fail2ban_collected_timestamp_seconds When this file was last written, in Unix time.'
    echo '# TYPE fail2ban_collected_timestamp_seconds gauge'
    echo "fail2ban_collected_timestamp_seconds $(date +%s)"
} >"$temporary"

# World-readable, because the container that serves it runs as nobody; the swap is a rename, so a
# scrape never reads half a file.
chmod 0644 "$temporary"
mv "$temporary" "$file"
