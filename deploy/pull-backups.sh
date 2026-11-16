#!/bin/bash
# SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
# SPDX-License-Identifier: MIT

# Pulls the site's newest snapshot onto the backup host, where it is checked and kept by date. It runs
# here rather than on the site because the site is the machine that might be wrong: the copy is
# verified somewhere the failing host does not control.
#
#   pull-backups.sh [deploy@moosemorals.com:backups] [/srv/backup/uk.osric.asp]
#
# The key named by SSH_KEY is the one in the site's authorized_keys, restricted with `rrsync -ro`.
# KEEP_DAYS is how many days of dated directories are kept.
set -euo pipefail

remote=${1:-deploy@moosemorals.com:backups}
destination=${2:-/srv/backup/uk.osric.asp}
keepDays=${KEEP_DAYS:-90}
key=${SSH_KEY:-$HOME/.ssh/id_backup_uk_osric_asp}

day=$(date +%F)
today=$destination/$day
staging=$destination/.$day.partial
verify=$staging/.verify.db

umask 077
mkdir -p "$destination"
rm -rf "$staging"
mkdir -p "$staging"
trap 'rm -rf "$staging"' EXIT

# The key has no passphrase because it runs unattended; BatchMode keeps a cron run from waiting on a
# prompt it can never answer, and IdentitiesOnly stops it offering every key the agent happens to hold.
rsh="ssh -i $key -o BatchMode=yes -o IdentitiesOnly=yes"
rsync --archive --rsh="$rsh" "$remote/storage.db.gz" "$staging/"
rsync --archive --rsh="$rsh" "$remote/conf/" "$staging/conf/"

# A snapshot that failed on the site leaves yesterday's file under the same name. Filing that under
# today's date would turn a dead backup into a quiet one, so too old is a failed pull.
age=$(( $(date +%s) - $(stat --format %Y "$staging/storage.db.gz") ))
if [ "$age" -ge 93600 ]; then
    echo "the snapshot is $((age / 3600))h old: the snapshot timer on the site has stopped" >&2
    exit 1
fi

gzip -t "$staging/storage.db.gz"
gunzip -c "$staging/storage.db.gz" >"$verify"
sqlite3 "$verify" 'PRAGMA integrity_check;' | grep -qx ok
tables=$(sqlite3 "$verify" \
    "select count(*) from sqlite_master where type = 'table' and name in ('PageHeads', 'Users', '__EFMigrationsHistory');")
if [ "$tables" -ne 3 ]; then
    echo "the snapshot is not this site's database" >&2
    exit 1
fi
rm -f "$verify"

# Only now does the day's copy replace an earlier one, so a bad pull leaves the previous day alone.
rm -rf "$today"
mv "$staging" "$today"

find "$destination" -mindepth 1 -maxdepth 1 -type d -mtime "+$keepDays" -exec rm -rf -- {} +
echo "pulled $today ($(du -h "$today/storage.db.gz" | cut --fields=1), $((age / 3600))h old)"
