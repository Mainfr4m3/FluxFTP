# Backup and import

Open **Global Settings → Backup/Import**. Save configuration edits before exporting.

## Export

Enter a password (at least eight characters), confirm it, and choose **Export backup…**.
The `.fluxbackup` file uses AES-256-GCM with a password-derived key. Keep the password separately: it is required for import.

The saved configuration includes sites and credentials, global settings, skip/priority rules,
bookmarks, layout/theme preferences, transfer queue, statistics, API certificate, site rules,
RACE logs and linked VISIONARY rule files/text scripts. Externally linked files are bundled
and their references relocated into the restored installation. Downloaded/uploaded payloads,
application executables and external runtime installations are outside the configuration backup.

## Import

Enter the backup password and choose **Import backup…**. Flux validates the package before
asking to replace the saved configuration. Import is staged, so it does not change the running
session. Close Flux completely (including the tray) and start the same installation again.

At startup Flux saves the previous configuration under
`%LOCALAPPDATA%\FluxFTP\backup-restore-history`, applies the imported files and removes old
managed configuration files absent from the backup. A failed write rolls back the old files.
Use **Cancel pending import** before restarting to discard a staged import.

Normal backups can be moved to another Windows user/PC: saved credentials are carried inside
the password-encrypted package and protected for the receiving Windows user during import.
Automatic `.fluxrestore` recovery copies are protected for the original Windows user; select
them in **Import backup…** on that account to restore the prior configuration without a backup
password. Local download locations and external runtime paths may need adjustment on another PC.

Backup packages are limited to 10,000 files and 512 MB of configuration/history. Invalid paths,
duplicate file names and paths through symbolic links or junctions are rejected.
