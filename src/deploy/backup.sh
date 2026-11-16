#!/bin/sh
# Nightly SQLite backup (run by crond in the compose "backup" service).
# Uses SQLite's online .backup so the live database is copied consistently; keeps 14 days.
set -eu
stamp=$(date +%Y%m%d-%H%M%S)
sqlite3 /data/bbs.db ".backup '/backups/bbs-$stamp.db'"
find /backups -name 'bbs-*.db' -mtime +14 -delete
