#!/bin/bash
# SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
# SPDX-License-Identifier: MIT

# Takes an online copy of the site's database for the backup host to pull. It runs as deploy because
# the storage volume belongs to that user's rootless podman, and the files inside it are owned by a
# mapped subordinate id, so the copy has to be made in the same user namespace. Nothing here reaches
# the backup host: the pull is theirs to make.
set -euo pipefail

volume=uk.osric.asp-storage
backupFolder=$HOME/backups

umask 077
mkdir -p "$backupFolder/conf"
cd "$backupFolder"

# Only a run that was SIGKILLed gets past its trap, and it leaves one scratch file per kill. Nothing
# else on this host is ever removed: the day names below replace themselves and the pull only reads.
find "$backupFolder" -maxdepth 1 -name 'storage.??????.db*' -mtime +1 -delete

# The only host files the repository does not carry; assistant.env holds the provider key.
for file in assistant.env appsettings.Production.json; do
    config=$HOME/.config/uk.osric.asp/$file
    if [ -e "$config" ]; then
        cp --preserve=mode,timestamps "$config" "conf/$file"
    fi
done

mountpoint=$(podman volume inspect --format '{{.Mountpoint}}' "$volume")
if [ -z "$mountpoint" ] || [ ! -d "$mountpoint/_data" ]; then
    echo "volume $volume has no mountpoint: has the app ever started?" >&2
    exit 1
fi

day=$(date +%d)
daily=storage.$day.db.gz
current=storage.db.gz
tmp=$(mktemp "$backupFolder/storage.XXXXXX.db")
trap 'rm -f "$tmp" "$tmp.gz" "$current.new"' EXIT

# SQLite's online backup, so the site keeps serving while the copy is taken. Deliberately not
# read-only: a container killed mid-write leaves a hot journal, and rolling it back is what a
# read-write open does before the copy starts. The timeout is for a write that lands at the same
# moment — the shell's default wait is far shorter than this.
podman unshare sqlite3 "$mountpoint/_data/storage.db" '.timeout 5000' ".backup '$tmp'"
podman unshare sqlite3 "$tmp" 'PRAGMA integrity_check;' | grep -qx ok

gzip -f "$tmp"
# SQLite creates its output 0644 whatever the umask, and this copy holds password hashes.
chmod 600 "$tmp.gz"

mv "$tmp.gz" "$daily"
# `storage.db.gz` is the name the backup host pulls. A hard link rather than a symlink, because its
# restricted rrsync refuses to follow one; the scratch name makes the replacement atomic for a pull
# that arrives at the wrong moment.
ln --force "$daily" "$current.new"
mv --force "$current.new" "$current"
echo "snapshot written to $backupFolder/$daily"
