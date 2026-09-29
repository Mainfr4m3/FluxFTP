# RACE log

Open **RACE-Log** in the top toolbar to follow the current log or select an
archived log. The viewer refreshes every two seconds and displays the latest
512 KB; the full file is retained on disk.

The active file is `FluxFTP-RACE.log`, beside the executable. Its header records
the period's start in UTC. After 24 elapsed hours it moves to
`logs/RACE-<start timestamp>-<unique ID>.log`, and a fresh active log is created.
FluxFTP checks before each write and once a minute while idle. Restarting the
application does not reset the period. After a long shutdown, the expired log
is archived at startup; empty files for missed days are not generated.
Archives are retained until manually removed.

Entries include accepted IRC catch announcements, queued/restored transfers,
state changes, source/destination sites and paths, byte counts, XDUPE skips,
failure type/FTP status code, job removal, queue clearing and session start/stop.
Passwords and raw FTP/IRC conversations are not copied to this log.
Write errors are shown in the main status area and log window; they do not stop
transfers. The FluxFTP folder must be writable.

This records the existing transfer queue and IRC catches. It does not introduce
automatic race scheduling from announcements.
