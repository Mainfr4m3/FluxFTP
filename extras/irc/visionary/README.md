# Visionary to FluxFTP bridge (Windows named pipe, no UDP)

This add-on receives Visionary's `FU_FXP_CHAINS` signal and starts a growing-release
watch for each explicit `source;target` chain. Communication uses the local
Windows named pipe `FluxFTP.Visionary.v1`: no UDP, TCP, HTTP, certificate, API
password, or listening network port is involved. Windows restricts the pipe to
the user account running FluxFTP.

## Install

1. Open **VISIONARY** in FluxFTP, enable the local bridge, and keep FluxFTP running.
   HTTPS API activation is not required. Use **Export mIRC bridge** to obtain this bundle.
2. Put `Visionary-FluxFTP.mrc`, `FluxFTPVisionary32.dll`, and
   `FluxFTPVisionary64.dll` in one folder.
3. Load this add-on after `Visionary.mrc`:

```text
/load -rs "C:\path\Visionary-FluxFTP.mrc"
/fluxvisi test
```

The wrapper selects the DLL matching the IRC client's `$bits`. Queueing results
are shown in `@FluxFTP`.

## Visionary setting

Visionary must emit this existing signal:

```text
FU_FXP_CHAINS <section> <release> <source;target source;target ...>
```

Enable its dispatch hook in `options.ini`:

```ini
[FU_FXP]
USE_FU_FXP=1
```

Do **not** add the old cbftp handler containing `sockudp`; this add-on replaces
that handler. Visionary's own internal connection to its Perl engine is separate
and remains unchanged.

## Requirements and behavior

- Visionary, the IRC client, and FluxFTP must run under the same Windows account.
- Site and section names in Visionary must match the saved FluxFTP names.
- FluxFTP resolves the saved section path independently for source and target.
- Section, release, and site names cannot contain spaces.
- `/fluxvisi transfer SECTION RELEASE SOURCE TARGET` starts a race manually.
- `OK WATCHING` acknowledges the watch, not a completed transfer. Progress and
  errors appear in the FluxFTP log and RACE-Log. `/fluxvisi test` tests DLL loading.
- The default watch rescans every three seconds for up to 60 minutes. Change the
  completion regex, interval and timeout in the VISIONARY panel before a new race.
- Growing and newly arriving files are queued while the source upload continues.
  A file is never queued twice concurrently by the same watch. When COMPLETE is
  observed, pre-completion files are copied again once, even if their listed size
  did not change (FTP servers can preallocate files). This costs additional traffic
  and requires the destination to permit overwriting those files. A rejected
  overwrite fails the race; XDUPE is not treated as proof of completion.
- NUKE / REASON markers stop the race and cancel its pending transfers. They must
  not be interpreted as successful completion even if an imported regex matches them.
- Only one source watch writes each section/release/target at a time; a duplicate
  chain returns `ALREADY WATCHING`. Up to 32 destination watches can run.
- Removing a watched transfer stops its watch on the next scan. Disabling the
  bridge stops all watches. Watches do not resume after restarting FluxFTP.

## Configuration panel

Import individual `PRiME.ini`, `section.cha`, `rushopt.ini`, `options.ini` or
SLFTP section files, or select VISIONARY's `User_Files` folder. This links the
original files for editing; it does not execute scripts or convert Perl rules
to FluxFTP rules. Filter by section, key or value. Saving writes only the selected
file, creates a timestamped backup, retains comments/encoding/order, and rejects
external changes until you reload the file. Reload the configuration in VISIONARY
after saving; FluxFTP does not guess or execute a VISIONARY reload command.

FTPRush import now includes **Sites**, **Sections and paths**, and **VISIONARY
rules** in one preview. Site-specific remote bookmarks from RushSite XML or
site.json become section mappings automatically, preserving the Rush caption,
site and remote path. Local bookmarks are not treated as FTP sections. Global
bookmarks require an explicit site; conflicting paths remain unselected until
you choose a path. Existing section prechecks are retained. Skip/Replace controls
whether an existing section path is overwritten.

Add your VISIONARY / User_Files folder in the **VISIONARY rules** tab to link the
complete configuration files in the same operation. PRiME-style site exports,
section.cha, rushopt.ini and options.ini remain intact and editable in the panel;
VISIONARY keeps evaluating its original trading/metadata/classification rules.
This is not a native conversion of the VISIONARY database or of all Rush transfer
options. Existing VISIONARY database rules remain in its running engine.

FluxFTP configurations are backed up in `Import-backups` before the import is
committed; a failed write restores the prior files. The source Rush/VISIONARY
files are not modified by import. Legacy Rush XML passwords may need re-entry;
replacing a site with an empty imported password retains its existing password.

## Sample and covers

Sample/covers directories now have their own payload completion rules. They work
inside a full release watch or as a separate job, for example:

```text
/fluxvisi transfer TV-HD Release.Name/sample SOURCE TARGET
/fluxvisi transfer TV-HD Release.Name/covers SOURCE TARGET
```

The full release still needs its own completion marker. A sample or image never
marks the parent release complete. Payload files remain in the transfer queue;
they are not discarded as marker files. Overlapping parent/subdirectory watches
on the same destination are rejected to avoid concurrent writes.

Built-in profiles recognize common video/image extensions. To use your existing
settings, open VISIONARY, choose **Choose rushopt.ini**, then **Save race settings**.
New watches load the `[sample]` and `[covers]` `completeflag`, `fileallow`,
`fileskip`, `retrycount`, and the `RS_EXPR` file-filter matching mode. `completeflag`
is a regex; file filters use regex with RS_EXPR, otherwise pipe-separated wildcards.
Simple `[detections]` entries such as `sample=.:/sample` and `covers=.:/covers`
can select other directory names. Unsupported detection syntax is rejected.
Other Rush options, including folder filters, sorting, subcount, repeatcount and
profile refresh values, are not implemented by this change. FluxFTP's configured
rescan interval and global skip rules still apply.

A payload job requires nonempty matching files, a stable accepted-file listing
for at least two rescan intervals (minimum two seconds), and successful final
transfers. New/changed files reset the quiet period. Zero-byte matching files keep
the job open. NUKE/REASON always overrides completion. Listing stability is an
additional check, not proof that an uploader has closed a preallocated file;
real FTP server behavior still needs live verification.

**Use selected completeflag** selects the profile file for sample/covers entries;
it does not replace the parent release's marker regex. Other entries still copy
the regex into the general race settings for review. Invalid .NET regexes and
completion patterns matching empty text are rejected.
