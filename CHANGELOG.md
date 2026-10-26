# Changelog

All notable FluxFTP changes are documented here.

## Bridge metrics reader 2 — 2026-10-26 (VM test)

- Reads the metrics heartbeat directly before parsing timestamp/TAB fields, avoids quoting identifier filenames, and handles file errors within that reader. Adds local `/fluxbridge metrics status` diagnostics and a 15-second freshness window for VM scheduling delays. Restarting metrics clears a previously saved "FluxFTP offline" title prefix. No EXE or DLL change is required for this script fix.

## 2.1.52 — 2026-10-26 (Windows)

- Tools > SITE STATUS now toggles the status panel both open and closed, using the same behavior as the Metrics status button.

## 2.1.51 — 2026-10-26 (Windows)

- Saves irc-metrics.txt beside FluxFTP.exe and publishes an automatic folder-discovery pointer for the mIRC/AdiIRC bridge. The reader supports explicit folder selection, script/parent-folder discovery and legacy AppData metrics during upgrades, and handles missing/locked/replaced files without flooding IRC with $read errors. Update/reload FluxFTP-Bridge.mrc with the EXE update; DLLs are unchanged.

- Adds optional FTP/FTPS site health monitoring in Global Settings > Site monitor: reuse idle control sessions for NOOP, pause new trade after repeated connection failures, recheck and restore scheduling after recovery. Default intervals are 300 seconds healthy / 60 seconds offline, with two failures before pausing; login/TLS rejections wait at least 10 minutes. Busy/full logins and file errors do not mark a server offline.
- Health suspension is independent of saved site permissions and manual job pauses, preserves running transfers and survives site policy refreshes. Tools > Site status shows health, last/next check and a manual check action. Monitoring is off by default; enabled checks are limited to two concurrent probes and available local login capacity.

- COMMANDS preserves line breaks, indentation and ASCII table borders in automatic SERVER STATS/DETAILS reports, including transfer completion replies and sample/media information. Manually issued SITE command/reply entries keep their existing format. New reports retain their layout in the live view, TXT export and archives.

## Linux 0.1.0-preview.4 — 2026-10-26

- Adds the familiar Sites, Tools, Metrics, Manager, Queue, Log, Global Settings and About toolbar with fixed-size wrapping buttons and saved panel visibility.
- Adds Global Settings for the local folder and slots, serialized local transfers, connection/runtime timeouts, log history, automatic cleanup of completed manual transfers, global proxy, Windows backup import and API configuration. Independent API settings are preserved when saving other settings or importing a Windows backup.
- Adds Sites with connection credentials, slots, priority, directory listing/TLS and FXP settings; connection to either pane; and section mappings that follow site renames. Unedited imported site settings are retained. Tools includes a section/path editor, API settings and Windows backup import.
- Adds basic session Metrics, themed controls, compact file rows, collapsible Quick Connect fields and log copy/clear controls. File transfer buttons move into the panes. Native Debian rendering was inspected for the main window, Global Settings and Sites.
- Validation: 39 settings/UI checks, 32 HTTPS/RaceTrade checks, 19 Windows backup import checks and local copy/resume checks passed on Debian 13/WSL.

## Linux 0.1.0-preview.3 — 2026-10-26

- Adds an optional HTTPS API compatible with RaceTrade/CBFTP clients. Configure the listen address, port, password, concurrent API job limit, recheck interval and job lifetime from the Linux toolbar. The API is disabled by default and binds to localhost by default.
- Adds compatible site and section management, remote path/file queries, raw FTP commands, transfer jobs and monitored spread jobs with status, abort and reset operations. API jobs share the UI's login slots and transfer scheduler limits.
- Spread jobs watch for late files in growing releases, honor source priority and section mappings, resume smaller partial targets, and verify source completion plus target sizes before reporting completion.
- Adds HTTPS certificate persistence/fingerprint display, encrypted API credentials, input validation and safer API lifecycle/reconfiguration.
- Validation on Debian 13/WSL: 32 HTTPS/RaceTrade checks passed, including real FTP FXP, recursive release files, growing releases, abort, active-job caps, atomic section updates, certificate persistence and busy-port recovery. RaceTrade's HTTPS request format was exercised; no real site race was run.

## Linux 0.1.0-preview.2 — 2026-10-26

- Adds a separate Linux x64 Avalonia desktop preview with two local/remote panes and the shared FTP/FTPS/SFTP transport and transfer engine. File download/upload, local copy, direct FXP, pause/resume, protocol logging and progress are available; full Windows parity and Visi integration are not included.
- Imports password-protected Windows .fluxbackup exports, including portable site/proxy credentials, per-site connection options, section navigation paths and compatible local slot/timeout/proxy settings. Sites are merged by ID, sections by name; local Windows paths are replaced with a chosen Linux directory. Unsupported settings, scripts and Visi are not activated.
- Saved Linux configuration is AES-GCM encrypted with owner-only state/key files. Import rejects wrong passwords, tampering, duplicate/unsafe paths and direct Windows DPAPI data. Portable credentials are converted to Linux encryption before temporary INI parsing.
- Linux native OpenSSL loading and TLS upload shutdown are adapted for Linux; Windows behavior is preserved. Packages include a self-contained .deb and tar.gz, an import guide and separate Linux checksums.
- Validation on Debian 13/WSL: 18 Windows-export-to-Linux import checks, GUI rendering, local copy/resume and FTP/FTPS/OpenSSL upload/download/resume plus plain FTP FXP payload verification. Windows build, 26 backup checks and OpenSSL regression checks passed. Ubuntu and SFTP integration still need real-environment verification.

## 2.1.50 — 2026-10-26

- Transfer Queue context menu adds Move up, Move down, Move to top and Move to bottom, supporting multiple selected files while preserving their relative order.
- Manual queue order is saved and used by the scheduler for pending jobs ahead of automatic priorities. Running transfers continue; slot limits still apply. Moving jobs clears column sorting so the displayed queue reflects the manual order.
- New manual jobs scheduled after a reordered queue are appended to its manual order. Automatic race entries retain their existing priorities unless an explicit manual queue order is present on another job.
- Validation: 25 file priority, queue movement, persistence and scheduler checks plus 77 global settings checks passed. Local test build; no bridge update is required.

## 2.1.49 — 2026-10-22

- Main toolbar order and labels are now Sites, Tools, Metrics, Manager, Queue, Log, Global Settings and About.
- Panel buttons keep their short labels; Show/Hide actions are described in tooltips.
- Includes the centered title/status startup messages and all changes from 2.1.48. Local test build; no bridge update is required.

## 2.1.48 — 2026-10-22

- Centers the startup message across the full status bar, including when scrolling status is selected. The selected scrolling mode resumes after the first transfer starts.
- Adds a themed title bar with the startup message centered, FluxFTP name/icon on the left and minimize, maximize/restore and close buttons on the right.
- Includes the Jobs defaults, restored FXP rate calculation, right-aligned status progress bar, queue context actions, TOOLS menu and tray shortcuts from 2.1.47.
- Local test build; no bridge script or DLL update is required.

## 2.1.47 — 2026-10-22

- Restores the completed FXP/relay bytes over the last 60 seconds calculation for the FXP RATE card while retaining the CURRENT label. The tooltip explains the calculation.
- Jobs defaults now use history limits 2000/2000/4096, prepared expiration 45 seconds, starter timeout 15 seconds and maximum runtime 15 minutes. Automatic hard reset defaults off, with cooldown 120 seconds, two retries and five-minute lookback. Spread jobs and Visionary scheduling default to 150, with a one-second race recheck. Explicit saved settings remain unchanged.
- The bottom status progress bar is fixed at the right edge, including scrolling mode, and stays hidden during the startup message.
- Transfer queue context menu adds Start queue, Stop queue, Resume selected, Remove selected and Reset failed jobs. Resume/remove support multiple selections; reset retains existing recovery eligibility and retry limits.
- TOOLS groups IRC ADD-ONS, SCRIPTS and SECTIONS in the main toolbar.
- Tray menu adds Show Manager, Show Tools and Show Global Settings, restoring the main window before opening the requested view.
- Local test build; no bridge script or DLL update is required.

## 2.1.46 — 2026-10-22

- The FXP rate card in Metrics uses the same current aggregate Speed value as the status bar, including its estimated-speed indicator and units. The card is labelled CURRENT instead of LAST 60 S.
- The separate last-60-second completed FXP rate remains available in detailed statistics.
- Local test build; no bridge script or DLL update is required.

## 2.1.45 — 2026-10-21

- The main window title displays FluxFTP followed by the same randomly selected startup message shown in the status bar. The title message stays for the session.
- The status progress bar is hidden while the startup message is shown and appears after the first transfer starts.
- No bridge script or DLL update is required.

## 2.1.44 — 2026-10-21

- Adds COMMANDS immediately after FTP-LOG for raw SITE commands and their replies, including server user/stat reports.
- FTP-LOG timestamps use local yyyy.MM.dd-HH:mm:ss. Transfer failures include the site where known; overwrite recovery uses concise OVERWRITE messages while resumed retries remain labelled RESUMING.
- Resumed transfers use a separate configurable Resume progress theme color in the bottom status bar, including local downloads/uploads and FXP.
- Metrics shows session and all-time measured peak speeds beside FXP rate. Disk-copy speeds and estimated FXP speeds do not set these records.
- New settings default to 300 active spread jobs, 300 Visionary scheduling slots and a 3-second race recheck. Recovery defaults to enabled, 120-second cooldown, five retries and a 15-minute lookback. Starter timeout is 200 seconds and transfer history is 5000. Explicit saved settings remain unchanged.
- Local test build for verification before GitHub publication. No bridge script or DLL update is required.

## 2.1.43 — 2026-10-20

- Race source inventory excludes paired drFTPD information markers [SUB]-...-[SUB], [AUDIO]-...-[AUDIO] and [VIDEO]-...-[VIDEO] when represented as directories or zero-byte entries. They are not descended into or copied. Nonempty files and real Subs/Sample/CD folders remain eligible.
- Verification metadata filtering now also applies while the Visionary bridge is enabled. This avoids the observed TIS metadata-directory 550 errors that aborted source inventory and triggered a 30-second source retry delay. Genuine permission/listing errors retain existing handling.
- Validation: 169 shared race/verification checks and desktop build passed. EXE-only change; no bridge script or DLL update is required. Local test build, not published to GitHub.

## 2.1.42 — 2026-10-20

- Log tabs now appear in order: FTP-LOG, BRIDGE-INFO, RACE-LOG, SPREAD-JOBS, TRANSFER-JOBS. RACE-Log and Transfer Jobs buttons are removed from Metrics; existing entry points select the corresponding embedded tab.
- Transfer Jobs retains live refresh, progress, remove/clear and hard reset actions in a reusable panel. Refresh stops when the panel is unloaded. Includes all previous local test changes. Not published to GitHub.

## 2.1.41 — 2026-10-20

- The log area uses themed FTP-LOG, BRIDGE-INFO and SPREAD-JOBS tabs. Existing FTP text remains in FTP-LOG; bridge diagnostics retain live following, copy and TXT export; spread editing and presets move into the embedded jobs tab. Bridge-Info and Spread Jobs buttons are removed from Metrics.
- Log viewing and spread editing are reusable embedded controls. RACE-Log remains available in its existing window. Switching tabs does not start or stop transfers; live log polling pauses while the view is unloaded.
- About displays the existing program artwork at 96 x 96 beside the FluxFTP wordmark and hides the small title-bar icon.
- Includes temp-folder downloads, automatic completed-queue cleanup, concise errors and startup messages from previous local test builds. Local test build, not published to GitHub.

## 2.1.40 — 2026-10-20

- Completed queue items are removed automatically after successful transfer/final destination placement, including staged downloads. Old completed rows are not restored at startup. Session/all-time statistics and diagnostic logs remain available.
- Connection Log shows concise transfer-failure summaries. Actual file-lock failures use FILE LOCK FAILED; other FTP 550 errors remain general transfer failures. 550 details stay out of Bridge-Info; Debug displays original details in Connection Log. RACE-Log retains diagnostics.
- About shows the FluxFTP wordmark and a clickable GitHub repository link.
- A random Flux-themed startup message appears in the bottom status until the first transfer executor starts, then normal status takes over for the rest of the session. Applies to downloads, uploads, FXP, bridge jobs and local copies.
- Includes optional temp-folder downloads and all previous fixes. Local test build; not published to GitHub yet.

## 2.1.39 — 2026-10-20

- Local settings adds an optional Download temp folder for local SSD/RAM-disk staging. Empty retains direct-to-destination downloads. Local FTP/SFTP downloads first finish a temp file, then copy to a sibling file beside the destination and rename only after the copy completes. Existing final files are preserved on download/copy failure or cancellation.
- The transfer queue and status bar show a separate Saving phase with copy bytes, percentage, speed and remaining time. Completion is recorded only after final placement. Disk-copy speeds do not set network speed records.
- Completed temp files survive a failed destination copy for retry of the same queued job without downloading again; partial temp downloads can resume. Temp names isolate queued jobs, source identity and final destination. Temp data is removed after successful placement. RAM-disk contents may be lost on restart; the temp volume needs space for each whole file.
- The final copy still consumes the local transfer slot and is included in overall runtime. Automatic transfer to the next file while an earlier file is copying is not added.

## 2.1.38 — 2026-10-20

- Connection Log shortens 553 upload-busy replies to UPLOADED by <user>, retaining full details in Bridge-Info and leaving other 553 errors unchanged. Repeated summaries for the same uploading user are limited to one per ten seconds.
- Failed job retirement uses RELEASE FAILED, RETRYING : <release> or RELEASE FAILED : <release>; complete failure details remain in RACE-Log. FXP average displays only filename and measured speed; RACE-Log retains byte count, duration and mode.
- Bottom status removes UP/DN, the duplicate queue Transferred value and duplicate percentage text. Session transferred total remains; file progress percentage stays in the progress bar. Clear separators group status fields, and idle hides empty timing placeholders.
- Includes the previous Bridge-Info, layout, download progress and Visi slot improvements. No additional bridge update is required from 2.1.37.

## 2.1.37 — 2026-10-20

- Metrics keeps its summary cards and action buttons above the scrolling detail area. Site Manager now wraps with the other toolbar buttons at its normal height. The main Quick Connect button is removed; Show/Hide Status moves to Metrics beside Spread Jobs.
- Bridge-Info beside RACE-Log shows bridge diagnostics, source failures, slot warnings and status in a separate live log with copy and TXT export. Detailed bridge messages no longer fill Connection Log. The bridge log has its own active file and archive filter; RACE-Log retains diagnostic history.
- Bridge login denials with FTP 530 on USER/PASS/ACCT show a concise LOGIN FAILED / ACCESS DENIED / connection BLOCKED summary in Connection Log, at most once per site per minute. Full details remain in Bridge-Info. BLOCKED describes the denied connection, not a confirmed account ban; LIST/CWD permission errors are not classified as login denials.
- Includes the existing download progress and cached Visi race slot improvements. No additional bridge script or DLL change is required when updating from 2.1.36.

## 2.1.36 — 2026-10-19

- Visi-provided directional slots now determine race transfer capacity, with room for listing and open panes; a low imported total login limit no longer silently throttles those race values. Directions without Visi values retain Flux settings, including fallback until the helper first replies.
- Last known Visi slots are saved locally and retained across helper timeouts and Flux restarts. Explicit zero values are respected. Metrics detailed statistics identifies Visi and Flux fallback values per site and direction. Cache write errors are reported without dropping a chain.
- Manual jobs keep their configured Flux slot limits through per-work engine limits and continue sharing actual occupied site counters with races. Saved site settings are not overwritten.
- Includes 2.1.35 local-download live progress fixes. Update the EXE and bridge script; DLLs remain compatible. Actual Visionary/mIRC callback timing still requires live verification.

## 2.1.35 — 2026-10-19

- Local downloads refresh live bytes, speed and percentage in the status bar and take display precedence over background races. Unknown file sizes are queried before downloading; resumed transfers reset speed sampling if the byte offset restarts. Late progress callbacks cannot overwrite completed or cancelled status.
- Includes the refreshed 2.1.34 change disabling SITE <username> and SITE USER statistics queries for races and Metrics; SITE STATS remains available.

## 2.1.34 — 2026-10-19

- The mIRC Visionary bridge now forwards pending chains with existing Flux limits if the custom live-slot helper does not reply within one second. A warning identifies the missing helper callback; a late valid response still updates the same watch's live limits. /fluxvisi status shows pending helper replies and the loaded script path. This requires updating/reloading the bridge script; existing DLLs remain compatible.
- This addresses the silent helper-wait path; the screenshot alone does not establish why Visionary stopped returning callbacks. Actual mIRC timer/callback behavior needs live verification.

## 2.1.33 — 2026-10-19

- Permanent CWD/MKD/CWD failures, including MKD File exists followed by a failed final CWD, stop the destination release once across all source states. A shared directory gate and failure state prevent per-file MKD storms; other destination releases continue. Full requested paths and final CWD replies are retained; shortened server reply names are never guessed as transfer targets.
- FTP exceptions identify the replying site and credential-safe command (USER/PASS arguments excluded), including responses from the other FXP peer. 530 during CWD/MKD/SIZE remains an authentication/permission error rather than being mistaken for a missing path or file.
- A 530 listing reply on a reused shared connection triggers one fresh login/list attempt with unchanged TLS/proxy settings. Fresh login denial imposes a shared 30-second listing-login cooldown; changing profile credentials clears it. Authentication denials do not trigger automatic MLSD/LIST/STAT protocol fallback. Persistent server denials still require investigating the reported site and command.

## 2.1.32 — 2026-10-19

- Metrics adds session race counts by source/destination from unique Visionary announcements. Repeated section/release/source/destination chains count once; counts are not wins and reset on restart. Duplicate detection retains 100,000 announcements.
- Confirmed first-place results are shown separately per destination. The bridge forwards a mapped bot's COMPLETE/DONE plus its final STATS [1] username line within 20 seconds; duplicate, unobserved or missing results do not count as wins. Configure /fluxvisi winbot NETWORK #CHANNEL BOT FLUX_SITE and per-site race account names in Metrics if different from FTP logins. A bridge script update/reload is required; the existing DLL is compatible.
- Server stats now show SITE STATS fallback when custom user-info commands are unsupported. Metrics provides an explicit refresh button and visible unavailable/busy status, reusing existing idle control sessions only. Unsupported SITE username commands also try SITE USER username.
- One failed source no longer terminates healthy alternative sources for the destination. Failed sources retry after 30 seconds within the original race lifetime; directory-rule blocks and cancellations retain their stop behavior. Destination ownership remains protected across active sources.
- Listing failures identify the site, path and connect/login versus LIST phase. These diagnostics do not claim to resolve server-side 530 permissions or guarantee equal throughput across FTP daemons.

## 2.1.31 — 2026-10-19

- Publish the IRC metrics heartbeat independently of tray-menu initialization. The bridge discards saved title prefixes containing previous FluxFTP metrics so fresh values cannot be hidden behind a stale title.
- Replacing a loaded .mrc file still requires reloading the script in mIRC. /fluxbridge metrics on restarts its metrics timer.

## 2.1.29 — 2026-10-19

- Source site priority now precedes file category in queue display and transfer selection: higher source priority first, then SFV/file rank, destination priority and queue age. Existing transfers are not interrupted.
- Visionary sources receive reservation/refill passes in descending source priority. Round-robin ordering applies only between equal-priority sources. Site limits and per-file reservations still apply.

## 2.1.28 — 2026-10-19

- Transfer selection and queue display order now apply descending destination site priority, then descending source site priority, before queue age. File-category priority remains first (SFV first for races), and running transfers are not interrupted. Site slot limits still apply.
- Regression checks cover a newer high-priority destination beating an older low-priority destination, source priority for equal destinations, and agreement between displayed and executed order.

## 2.1.27 — 2026-10-18

- Multi-source scheduling uses a fair first pass followed by a refill pass that lets sources with eligible files borrow unused destination capacity. The source count no longer permanently reserves slots for sources without work.
- Queue capacity accounts for existing queued/running work on both the source and destination across races, within configured upload/download budgets. Destination file reservations remain in force.
- FTP sessions send PRET when advertised in FEAT, as well as when explicitly enabled in Site Options. Servers that require PRET without advertising it still require the site option.

## 2.1.26 — 2026-10-18

- All Visionary-announced sources for a destination release receive scheduling passes while other sources have active transfers. Downloads can run concurrently; the engine still enforces shared site upload/download limits.
- Each source receives a bounded queue window based on live slot budgets and source count, with SFV first and then largest files. Admission order rotates between passes so the first source does not reserve the whole release.
- Queue publication reserves each destination site/path across sources without yielding between the ownership check and job creation. Deferred files are retried when reservations or slots become available. Completion waits for active jobs across the destination group, and shutdown cleans all source states.

## 2.1.25 — 2026-10-18

- Visionary watches retain alternative sources announced for the same destination release. Once the current source has no queued/running transfers, subsequent passes rotate through those sources rather than remaining pinned to the first one.
- Source-specific retry/stability state and download slots are preserved. Sources share destination ownership and are scheduled serially; active file jobs are not interrupted to change source. Switching sources does not restart the watch lifetime. Source-switch diagnostics require Debug.

## 2.1.24 — 2026-10-18

- Previously skipped race files receive a direct destination SIZE check before requeueing, using the shared listing connection. An incomplete directory listing no longer requires transfer workers merely to rediscover the existing file. Removed or incomplete targets still follow normal retry/resume policy.

## 2.1.23 — 2026-10-18

- Connection-log race prequeue and watch-wait summaries require Debug. Full RACE logs retain these diagnostics; transfer errors and completions remain visible normally.

## 2.1.22 — 2026-10-18

- Skipped race transfers remain busy until their engine tasks release workers. Terminal skips invalidate destination listings and use periodic rechecks instead of immediately waking another pass against stale target data.
- Engine state notifications coalesce into fresh UI snapshots to avoid accumulating Dispatcher callbacks during heavy transfer activity.
- Unhandled managed exceptions are recorded, best effort, in %LOCALAPPDATA%\FluxFTP\crash.log with one rotated previous log. Fatal exceptions are not suppressed. The reported 2.1.21 crash cause remains unconfirmed without its exception details.

## 2.1.21 — 2026-10-18

- Jobs & history > Advanced adds Race recheck interval (1–60 seconds). It applies to Visionary watches, spread polling and temporary-file retry cooldowns; incoming notifications still wake immediately. Existing installations retain their saved race interval until this setting is saved.
- One-second rechecks do not shorten source-file stability checks or the independent maximum race lifetime. The watch-start log reports the effective recheck interval and lifetime.
- Exceptions wrapping cancellation at the watch lifetime boundary are reported as lifetime expiry instead of misleading LIST/transfer failure messages.

## 2.1.20 — 2026-10-17

- Transfer worker rentals synchronize per site instead of waiting behind a global pool-maintenance lock. A slow site's login/NOOP no longer blocks worker acquisition on unrelated sites.
- Pool maintenance skips busy site gates and sites with queued/running work. Pool disposal shares the site gate, and rental health-check NOOP calls are bounded to five seconds.

## 2.1.19 — 2026-10-17

- Visionary watches now wake on incoming updates and periodically recheck using their refresh interval (default 3 seconds), bounded by the existing race lifetime. Missing bot updates no longer leave a watch sleeping until timeout.
- Upload-in-progress/file-lock and transient connection failures are retained as race retries with a cooldown. One exhausted non-transient file no longer terminates all other files at the destination. Directory-rule blocks remain excluded.
- Previously skipped targets are checked again. Incomplete or changing targets cannot prove completion. A final binary source can resume a smaller target with default race policies; an explicitly configured Skip is preserved. TLS and site slots remain in effect.
- Race queues prioritize SFV first, then descending known file size. Other transfer queues retain their configured priorities.
- Race logs distinguish temporary retry, pending source/target checks and matching targets; repeated identical queue summaries are suppressed.

## 2.1.18 — 2026-10-17

- Rebuild of the current layout and user-statistics changes. Backup regression tests now cover 80 sections and 160 site mappings restored to an empty installation; no backup data-loss fix is claimed. Site-name corrections remain saved configuration, not executable defaults.

## 2.1.17 — 2026-10-17

- Metrics Top stats now displays only the latest `SITE <logged-in username>` reply per site. Its tooltip no longer refers to WKUP; separate SITE STATS replies remain in the connection log.

## 2.1.16 — 2026-10-17

- Site Options shows checked checkboxes in bold and unchecked checkboxes in normal weight. Connection behavior groups special protocol, FXP-role, TLS-backend and encoding options behind Advanced, preserving their saved values.
- Jobs & history groups timing, hard reset and concurrency settings behind Advanced; history controls remain directly visible. Opening or closing Advanced does not change any values.

## 2.1.15 — 2026-10-17

- Workspace panels now follow the order in which they are shown. Hiding and reopening a panel moves it to the end; the panel order, visibility and sizes are restored on restart.
- Metrics-only windows can shrink to 240 pixels high with the existing Metrics scroll area. Splitters follow the actual adjacent panels, including Manager in a custom position.

## 2.1.14 — 2026-10-17

- Completed races now query `SITE <logged-in username>` (for example `SITE khaz` or `SITE PAiD`) instead of `SITE WKUP`. Successful replies remain visible without Debug and appear as User info in Metrics. The separate `SITE STATS` query is retained.

## 2.1.13 — 2026-10-16

- A server directory-rule rejection now cancels remaining file jobs for that destination race with one clear message. Parallel files serialize directory checks so they cannot repeatedly retry the same denied MKD. Other destinations continue; a new race can try again. Existing directories remain usable when CWD succeeds after a rejected MKD.

## 2.1.12 — 2026-10-16

- Jobs & history adds Visionary concurrent scheduling (1–2048, default 32), controlling simultaneous inventory/scheduling steps. Watches waiting for updates release their slots; per-site FTP slot limits and the watch backlog remain unchanged.
- The bridge startup log reports the configured scheduling limit. Saving global settings restarts the bridge; change the limit between races.

## 2.1.11 — 2026-10-16

- Jobs & history adds Maximum active spread jobs (1–2048, default 32) for HTTPS `/spreadjobs`. Per-site transfer slots still apply; Visionary chains use their separate watch queue.
- Rejected spread requests now return HTTP 429 with an explanatory error and the configured limit, and write a diagnostic log entry.
- Adds Swedish API documentation covering requests, acknowledgements, progress, abort, completion, the Visionary bridge and current headless limitations.

## 2.1.10 — 2026-10-16

- Channel sharing now includes transferred bytes, maximum measured speed and completed files; session sharing also includes uptime. Session speed records reset on restart; all-time records persist with total statistics. Records start with this version and exclude display-only FXP speed estimates; measured direct FXP completion averages are included.

## 2.1.9 — 2026-10-16

- The mIRC metrics titlebar adds a 20-character progress bar and percentage for the same current transfer as FluxFTP's status bar, for example `[::::::::::..........] 50% Complete`. Estimated FXP progress is marked with `~` and capped at 99%. Idle, queued and unknown-size transfers show their status instead.

## 2.1.8 — 2026-10-16

- The mIRC bridge displays traffic, session and all-time totals, FXP rate, files, jobs, idle pool and uptime in the titlebar. Use `/fluxbridge metrics on` or `/fluxbridge metrics off` to control it.
- FluxFTP publishes a local metrics heartbeat even when idle or minimized. The bridge shows offline when the heartbeat is missing or stale and recovers automatically, without blocking API or DLL calls. Both the updated application and bridge script are required.

## 2.1.7 — 2026-10-16

- Transfer Jobs adds Hard reset selected / Hard reset eligible for recent connection and timeout failures, including failed files from still-active Visionary races.
- Jobs & history adds optional automatic hard reset (off by default), cooldown (30 seconds), maximum retries (2) and lookback (10 minutes). Both modes share the limits. Candidates are bounded to 1024 records in the current session.
- Reset jobs use fresh control sessions for the original sites, replace only idle pool sessions, and retain current TLS, slot and target-file policies. Paused jobs, expired races, permanent FTP rejections, DUPE, file locks and CRC failures are excluded; removed/replaced jobs and completed races clear their recovery records.

## 2.1.6 — 2026-10-16

- The FXP speed setting now enables cbftp-style display estimates: directional site-pair history (initially 1 MiB/s), destination-size observations and asymmetric speed smoothing, with display interpolation every 50 ms. Sampling reuses existing idle shared listing sessions/cache, at most once per second per active file, without opening extra logins.
- Estimated speed/progress is marked with ≈ and held below displayed 100% until successful completion. Resume offsets are excluded from speed calculations; full-size/preallocated targets do not count as measurements. Estimated bytes are separate from saved queue bytes and transfer statistics.
- Successful direct binary FXP retains its measured final byte/time average and updates site-pair history. Retry/fallback attempts reset their display estimates. Existing FXP measurement preferences are retained; enable the renamed speed/progress option in Global Settings to use estimates.

## 2.1.5 — 2026-10-15

- Verified completed races also request `SITE WKUP <logged-in username>` for weekly upload statistics, alongside SITE STATS. Successful replies appear in Connection Log without Debug.
- Metrics retains separate Stats and Weekly upload reports for each site, so weekly results do not overwrite account statistics. Optional queries continue to use idle existing control sessions; unsupported or unavailable reports remain under Debug.

## 2.1.4 — 2026-10-15

- Skiplist matches, existing-target skip notices, Smart resume status and the complete FXP diagnostic block now require Debug in Connection Log. Detailed diagnostics remain in RACE-Log; transfer errors, queued transfers and user statistics stay visible without Debug.

## 2.1.3 — 2026-10-15

- VISIONARY TIMING messages in Connection Log now require Debug. Queued transfers, errors and user statistics remain visible without Debug. Full timing diagnostics remain in RACE-Log and the Metrics timing card is unchanged.

## 2.1.2 — 2026-10-15

- Server CRC/size verification failures retain Failed status and show a concise integrity error. The full server reply is available under Debug and in RACE-Log.
- User statistics and server-provided ranking tables are displayed in Connection Log at race completion, independently of Debug. Metrics adds a Top lists / server stats section, initially empty, with the latest successful SITE STATS response per site and logged-in user.
- Completed races request `SITE STATS <username>` for the account logged into each existing idle shared control session. Busy sessions are skipped, no additional logins are opened, and unsupported/failed statistics requests remain under Debug and cannot change the race result. Ranking availability and length depend on the server's response.

## 2.1.1 — 2026-10-15

- Metrics adds a saved Debug checkbox between Transfer Jobs and RACE-Log. Visionary timing, queued transfers and errors remain visible; other Visionary status messages require Debug. Full diagnostics remain in RACE-Log.
- A matching FXP timings card between Idle Pool and Session Running shows the latest direct FXP attempt's PRET, CPSV, STOR and DATA times. Unmeasured stages show a dash and values reset per attempt instead of mixing transfers.
- Destination STOR is measured separately from the parallel STOR/RETR setup stage without changing command ordering or TLS settings.

## 2.1.0 — 2026-10-15

- Bridge 2.0 reads live Visionary slot settings for each incoming chain through the supplied read-only `custom/fluxftp_chains.pl` module. Section overrides take precedence over site values; active watches receive changes without reimporting INI exports. Flux total connection caps and disabled directions still apply.
- Waiting RACE watches release scheduler admission slots, allowing further destinations to start instead of blocking behind 32 idle watches.
- In-app bridge export and the release ZIP both include the unified script, four DLLs, live-slot module and installation instructions.
- Bridge and Visionary DLL versions are 2.0; the application and release package version is 2.1.0.

## 1.0.84 — 2026-10-12

- OpenSSL TLS retries WANT_READ/WANT_WRITE during handshake and I/O using socket readiness instead of treating temporary wait states as fatal unknown errors. Per-thread error queues are cleared before operations and SSL_get_error receives the actual result immediately on the same thread.
- OpenSSL operations are serialized per TLS session; cancellation interrupts socket I/O and disposal waits for operations to exit before freeing native handles. Genuine TLS/socket failures remain fatal, with SSL/Winsock codes when the OpenSSL error queue is empty.
- Site Options renders FXP protection/data-role labels instead of record debug strings.

## 1.0.83 — 2026-10-12

- VISIONARY accepts additional releases into a bounded backlog instead of rejecting every request once 32 watches are active. Up to 32 watches run concurrently; waiting watches start as slots are released, with at most 2048 active/waiting watches retained.
- Watch timeouts begin on admission rather than while waiting. Shutdown cancels pending watches, and duplicate/overlapping destination protection remains active.

## 1.0.82 — 2026-10-12

- Fix a RACE regression: failed hidden jobs retain Failed state for bounded retry/final recopy instead of being permanently skipped. Failed jobs remain absent from the visible queue.
- VISIONARY RACE bypasses existing-target size skip rules so partial or growing targets can be repaired and verified.
- VISIONARY bridge DLL 1.03 retries missing/busy pipes for up to 10 seconds. Pipe listener creation failures are caught and retried; clients that fail to send a request cannot hold the sole pipe indefinitely. Request read timeout does not cancel background races.

## 1.0.81 — 2026-10-12

- Global Settings > Skiplist adds enable/disable, zero-byte file skipping, empty-directory skipping, folder rule matching, automatic/manual/both modes and skipped-entry color selection. New optional zero/empty filters default to off; existing pattern behavior remains enabled.
- File panes highlight matching entries. Manual recursive transfers and API/PRE/VISIONARY filtering honor the selected mode; unfinished zero-byte RACE sources are not prematurely skipped.
- Skiplist JSON import/export includes patterns, advanced rules and options. Advanced provides Add/Edit/Delete and validates regex; runtime regex matching has a timeout.

## 1.0.80 — 2026-10-12

- RACE pool limits refresh from current FluxFTP Site Options and reserve capacity for a shared listing/verifier connection, open panes and enabled monitoring. Directional limits cap prewarming; smaller budgets close excess idle sessions.
- RACE destination verification shares the serialized listing service instead of opening an additional connection for every check.
- Failed RACE slot creation waits 60 seconds before reconnecting. Worker rental observes this backoff instead of repeatedly opening rejected logins.

## 1.0.79 — 2026-10-12

- VISIONARY bridge races prewarm each participating site's configured total transfer slots on first use. Idle connections receive NOOP keepalives every 15 seconds and failed idle slots are replenished; active transfers retain exclusive ownership of their connections.
- Bridge race jobs run internally without appearing in Transfer queue or Transfer Jobs. RACE logs, scheduling, verification and session/all-time counters remain active. Clearing or starting the visible queue does not control hidden races.
- Disabling the bridge releases its idle transfer pools; shutdown cancels pool maintenance. Completed internal jobs retain only temporary state until their race ends.

## 1.0.78 — 2026-10-12

- RACE-Log updates every second and follows new events after text layout completes. Follow live defaults to enabled for the active log.
- Scrolling up pauses following while live updates continue; scrolling back to the bottom or enabling Follow live resumes it. Incremental appends preserve text selection and scroll position while reading history.

## 1.0.77 — 2026-10-12

- Removed Kill ghost login from Connection details and Quick Connect. Server-specific username prefixes such as drFTPD's `!` are entered directly and preserved; existing saved usernames are not rewritten.

## 1.0.76 — 2026-10-12

- Failed jobs are logged and removed from the transfer queue after execution ends. RACE retains hidden skipped state so removed failures are not retried or mistaken for user cancellation.
- Global Settings > FXP can automatically skip existing targets smaller than, equal to or larger than the source. All three default to enabled and apply to new FXP/relay attempts, including RACE; unknown sizes retain normal transfer behavior.
- Skipped targets are logged, removed from the queue and excluded from completed-transfer byte totals. RACE does not verify skipped payloads as downloaded files.

## 1.0.75 — 2026-10-12

- IRC admin verification recognizes the standard ioFTPD SITE USER table with UserName and Flags on the same row. Successful replies still require exactly one recognized flags field containing 1 or M; taglines, ambiguous fields and error replies remain rejected.

## 1.0.74 — 2026-10-12

- IRC reports specific configuration errors for missing admin sites, non-FTPS admin sites and enabled invalid-certificate exceptions, rather than a generic SITE USER error. Existing verification requirements are preserved.

## 1.0.73 — 2026-10-12

- IRC supports `!flux stats`, `!flux stats total` and `!flux stats pool`, using the same session, saved totals and transfer-connection pool counters as Metrics.
- Statistics requests work in private messages or the configured control channel over TLS, retain existing FTP admin verification and identity checks, and reply privately by NOTICE.

## 1.0.72 — 2026-10-12

- Metrics adds saved all-time totals across all sites: uploads, downloads, FXP/relay, local copies, combined bytes and completed files. Tracking starts with this version, remains separate from session/queue counters and survives application restarts.
- Totals save after successful transfers using atomic file replacement and a backup. Failed saves and backup recovery are reported; resumed existing bytes and failed/partial attempts are excluded.

## 1.0.71 — 2026-10-12

- Metrics tracks process uptime, successfully uploaded/downloaded/FXP-relayed/local-copy bytes and completed files independently of queue history. Resume offsets are excluded from client-transfer totals; partial or failed attempts are not included. Session counters reset on application restart.
- Metrics also shows connected transfer workers created/closed, current idle connection pool size, tracked live workers and successful pool reuse hits. Browsing and monitoring connections are excluded.
- Global Settings > FXP can enable Download measurement for new downloads. Diagnostics default to off, bypassing detailed read/write timing and per-file measurement logging.

## 1.0.70 — 2026-10-12

- Download diagnostics record per-file elapsed time, newly downloaded bytes, resume offset, average MB/s, flush and rename time in Connection Log and RACE log (`DOWNLOAD-MEASURE`).
- FTP/FTPS diagnostics also measure network reads (including TLS processing), destination writes, progress reporting and setup/completion overhead. SFTP reports total timing; SSH.NET owns its internal data loop. Timings include OS buffering and do not measure physical disk throughput or isolate TLS CPU time.
- Completed local downloads update the final byte counter before recording completion.

## 1.0.69 — 2026-10-12

- Transfer skip defaults also exclude ioNiNJA video metadata markers matching `[ioNiNJA]-(V_*)-[ioNiNJA]`, regardless of codec, bitrate or resolution. Existing settings receive the new editable pattern once.

## 1.0.68 — 2026-10-12

- Default transfer skip patterns exclude `.ioFTPD.verified` and ioNiNJA COMPLETE marker folders such as `[ioNiNJA]-(2.7GB 20F-COMPLETE)-[ioNiNJA]` from recursive folder transfers, including FXP and PRE/race jobs.
- Existing settings receive these patterns once while retaining custom rules. Patterns remain editable under Global Settings > Skiplist.

## 1.0.67 — 2026-10-12

- Global Settings > Layout configures visible columns, sorting column/direction and font family, size and weight independently for both file panes and Transfer queue.
- Layout changes apply immediately after Save and persist across restarts. Header sorting and column context menus update the same settings; existing column preferences are retained.
- File and queue typography can inherit the theme, and queue sorting can return to the original queue order.

## 1.0.66 — 2026-10-12

- Right-click column headers in either file pane or Transfer queue to show or hide columns. The existing row context menus also provide a Columns submenu and Show all columns.
- Column visibility is saved independently for each pane and the queue. At least one column remains visible, and automatic sizing allocates available space to the remaining columns.

## 1.0.65 — 2026-10-12

- Connection Log omits directory rows from successful multiline STAT replies. Reply headers, completion lines and errors remain visible; full listing data remains available to the directory parser and protocol details.

## 1.0.64 — 2026-10-12

- Local FTP, FTPS and SFTP downloads run their data loop and filesystem operations on a background thread to keep the interface responsive.
- Local downloads use `filename.FLUXFTP.PART` until completion. Interrupted downloads retain this file for resuming; successful downloads replace the final destination only after the stream is closed.
- FTP binary mode is reaffirmed before each RETR/STOR file transfer.

## 1.0.63 — 2026-10-09

- IRC Add-ons collects IRC settings, VISIONARY and bridge integrations in one window. IRC settings can be saved directly from this window.
- Metrics now provides access to Transfer Jobs, RACE-Log and Spread Jobs, reducing the number of buttons in the main toolbar.

## 1.0.62 — 2026-10-08

- Delete processes every selected file/folder with one confirmation, supports the Delete key in both panes, checks server replies and verifies removal. Recursive deletion removes files and subfolders before the selected folder.
- Path history displays the current directory and supports typing/pasting a path followed by Enter. Noneditable selectors retain their existing behavior.
- Live FXP speed monitoring defaults to off to reduce race overhead. Global Settings > FXP can enable it; explicitly saved preferences are preserved.

## 1.0.61 — 2026-10-08

- Global Settings > FXP can disable live speed/progress monitoring to reduce additional FTP connections and status queries during VISIONARY races. Saving stops active monitoring; enabling applies to new files. Completion and final average speed remain available.
- FXP progress samples both servers independently with bounded monitoring operations, reconnects failed connections and skips unsupported monitoring commands.
- Brief measurement gaps retain the last observed speed. After six seconds without fresh data, speed is marked unavailable while monitoring continues. Stale local telemetry snapshots are rejected.
- Actual zero-speed samples remain distinct from missing measurements. Destination SIZE fallback ignores preallocated complete files and resets its baseline after reconnecting.

## 1.0.60 — 2026-10-08

- Configurable SFV → main files → Sample → Proof → NFO transfer priority in Global Settings > Prio List. Imported and global wildcard rules apply within each category; changes update waiting jobs without interrupting running files.
- Restored FXP progress monitoring by querying the source first with a timeout and reading SITE FLUXWHO directly. Monitoring failures include diagnostics and destination SIZE failures no longer end monitoring.

## 1.0.59 — 2026-10-08

### Fixed

- Transfer queue heading uses the same text color as Connection Log.
- Buttons use white text, including Quick Connect. Local/Remote selectors inherit the correct foreground color in their dropdown toggle.

## 1.0.58 — 2026-10-06

- Each pane now has one Connect/Disconnect button that follows connection state and prevents duplicate connection attempts.

- VISIONARY watches share one serialized source-listing connection per site and immutable directory snapshots for up to one second. Idle connections close automatically; changed profiles and failed operations discard the session and cached listings.
- VISIONARY import reads consistent slotsup/slotsdn values from each site's configuration, capped by the existing Flux total slot limit. Missing, invalid or differing section values retain the existing directional limit. Live bridge calls do not synchronize configuration.
- Includes the bridge compatibility and FXP speed measurement fixes from the 1.0.57 test builds. Monitoring/browsing connections are not yet included in the transfer scheduler's total slot accounting.

## 1.0.57 — test builds, 2026-10-04–2026-10-05

- Test builds corrected mIRC bridge compatibility/version checks, VISIONARY missing-marker detection and FXP speed matching, units and preallocation handling. These changes were included in 1.0.58; no standalone stable 1.0.57 release was published.

## 1.0.56 — 2026-10-02

### Added

- Rush import defaults to AUTH TLS, Auto listings and 4 total / 2 upload / 2 download slots. Site Options shows original Rush skip/prio entries and VISIONARY configuration from `RULES/*.ini`, `options.ini` and `rushopt.ini`; site affils are matched by filename. Priority patterns are applied to queued files; legacy skip flags and VISIONARY rules remain source configuration, not converted filters.
- Site Manager columns support ascending/descending sorting.
- Rush import offers an optional UTF-8 `Site name=password` text file for missing passwords, with per-site match status and validation. Existing-site password updates follow the Replace existing choice.
- Site Rules can discover Visionary's linked `User_Files/RULES` folder or select it manually, and edit its `.txt` documents with backups and protection against overwriting external changes.

### Fixed

- Removed the experimental WinSCP engine from racefix.9 and restored the previous FTP/SSH.NET connection and transfer paths. Saved engine selections are ignored; slot defaults remain 4 total / 2 upload / 2 download.
- Password-file imports can explicitly update passwords on selected existing sites even with Skip existing. The password prompt now uses FluxFTP theme resources.
- Local disk transfers are serialized and progress is throttled before dispatch; local copy I/O runs off the UI thread. FXP remains independently scheduled.
- RACE errors now include FTP server reply text alongside status codes.
- Legacy Rush XML import reads the site's SSL mode instead of inferring it only from the port. Explicit SSL/TLS sites use FluxFTP's AUTH TLS-first connection.
- Import Sections preserves per-site paths when importing a full RushSite XML file, instead of assigning every bookmark to `ioFTPD`. Ambiguous names and paths require review.
- Export mIRC bridge bundles updated DLLs that avoid reading beyond the legacy LOADINFO structure in mIRC 7.52. The same DLLs handle the newer mIRC 7.64+ structure, so no export version selector is needed.

## 1.0.55 — 2026-10-01

### Added

- Shared growing-release supervision for RaceTrade spread jobs and VISIONARY, including download-only targets, destination verification and overlapping-race protection.
- VISIONARY configuration/import tools, sample/covers profiles and embedded mIRC bridge export.

### Fixed

- Server CRC/zero-byte rejection messages fail transfers even when returned with FTP 226.
- Preserve DUPE, NUKE and SAMPLE rejections; avoid relay fallback for recognized server policy stops.
- Keep multiline server error boxes readable and distinguish expected missing-directory probes from transfer failures.
- White bottom status text for improved readability.
- Removed queue jobs cannot cause a spread job to report DONE; final source state triggers rechecks of growing files.

### Security

- Upgrade SSH.NET from 2025.1.0 to 2026.0.0, including upstream SCP security fixes. FluxFTP uses SftpClient, not ScpClient.

### Validation

- Race verification checks and the existing desktop test suite passed.
- NuGet vulnerability audit including transitive dependencies reported no vulnerable packages.
- SFTP and an initial live race passed user testing in the racefix.5 candidate.

### Downloads

- Windows x64 single-file EXEs: framework-dependent (.NET 8 Desktop and ASP.NET Core runtimes required) and self-contained (runtimes included).
- Separate bridge and rules/guides ZIPs, plus SHA-256 checksums.

## 1.0.53 — 2026-09-29

### Added

- Built-in IRC/ZNC connection settings, FTP-admin verification and private setup commands.
- Multiple announcement networks/channels, FiSH ECB/CBC support and configurable catch rules.
- `!news` shows the latest published FluxFTP release; `!announces` shows caught site announcements.
- Authenticated `POST /irc/connect` saves IRC/ZNC settings and starts the connection without opening Global Settings.
- RACE-Log toolbar window records announcements and transfer events, rotating after 24 hours into the `logs` subfolder.
- JSON site rules in `Rules/_site`, IRC setup examples and a site-rule viewer.
- Bookmark CAPTION import, section import and transfer completion diagnostics included in the desktop build.

### Fixed

- Saved site fields remain readable in INI files. Manually entered FTP/proxy passwords are protected on load.
- Global Settings writes changes before closing; validation and save errors remain visible.
- RACE-Log follows the application theme.
- IRC diagnostics distinguish registration failures, nickname conflicts and channel join failures.

### Notes

- IRC has an explicit option to accept invalid/self-signed certificates, enabled by default; previously saved choices are retained.
- Automatic racing from IRC announcements is not implemented. Catch rules record news and log events.
- Full slftp command compatibility is not implemented; see `docs/slftp-compatibility.md`.

## 1.0.52 — 2026-09-17

### Added

- Manual and API-driven `SITE PRE` commands now classify server replies as success, dupe or failure.
- The `/raw` API exposes duplicate PRE results separately while retaining them in `successes` for compatibility with existing RaceTrade and IRC integrations.

### Fixed

- Starting very large transfer queues now schedules work in a single batch instead of rewriting the complete queue file for every item.
- RaceTrade/API FXP and download jobs now use the same batch scheduler, preventing large races from making the interface appear frozen before transfers begin.
- Queue status synchronization now uses indexed lookups and batch resume operations, avoiding quadratic processing with thousands of files.

## 1.0.51 — 2026-09-11

### Added

- Added a Commander-inspired LOCAL toolbar to both panes.
- The selected local drive is shown as a single highlighted button with volume name, free space and total size in its tooltip.
- Added quick LOCAL actions for creating folders, renaming, deleting and refreshing.

### Changed

- Local drive switching remains in the existing drive list, avoiding duplicate buttons for every available drive.
- The LOCAL toolbar is hidden automatically whenever a pane is switched to Remote mode.

## 1.0.50 — 2026-09-04

### Fixed

- FTP servers advertising RFC 2640 UTF-8 now receive pathname commands using UTF-8 after `OPTS UTF8 ON` negotiation.
- Legacy servers such as ioFTPD that do not advertise UTF-8 now use Windows-1252 control-channel compatibility instead of replacing typographic characters with `?`.
- Downloads, uploads and FXP transfers now preserve filenames containing typographic apostrophes and other non-ASCII characters supported by the server encoding.

## 1.0.49 — 2026-08-29

### Fixed

- Priority List ranks are now carried into the transfer engine, so prioritized file patterns are selected before scoreboard size scoring.
- Files within the same priority rank now retain queue order and use natural numeric name ordering, preventing file 8 from starting before file 2.
- Normal FTP, FXP, downloads, uploads and local transfers now share the same deterministic priority scheduling.

## 1.0.48 — 2026-08-28

### Added

- Added an `ioFTPD / IRC` command preset for sending `SITE IRC`.

### Changed

- Moved `Raw command` to the first position and made it the default selection in Server Commands.
- Commands discovered through `SITE HELP` are appended after the built-in presets.

## 1.0.47 — 2026-08-11

### Added

- Added batch PRE for multiple selected release folders, executed sequentially with one section selection.
- Added sortable Size columns in both panes and sorting for every Transfer Queue column.
- Added protection against accidentally PREing the section directory itself.

### Fixed

- Selected Name, Size or Modified sorting and its direction now persist while navigating between local or remote directories.
- Size and modification sorting now use numeric byte counts and actual timestamps instead of formatted display text.
- ioFTPD delayed CWD event replies such as `This looks like a PRE` no longer desynchronize subsequent FXP commands.
- SITE PRE commands now allow up to ten minutes for large releases while ordinary commands retain their shorter timeout.

## 1.0.46 — 2026-08-09

### Removed

- Removed the legacy cbftp UDP command listener. Automation now uses the authenticated HTTPS/JSON API exclusively.

## 1.0.45 — 2026-08-09

### Added

- Added a Bookmarks window for each Local or Remote pane with site-specific filtering, editing, importing and direct navigation.
- FTPRush imports now include bookmarks from both current JSON configurations and legacy `RushSite.xml` files using `CAPTION` and `REMOTE` attributes.
- Added live DrFTPD FXP speed monitoring through the server's standard `SITE WHO` output.

### Fixed

- Standard EPSV replies now use the selected site or bouncer address instead of treating a complete multi-address field as one invalid FXP address.
- EPSV data connections through SOCKS5 no longer mistake the proxy endpoint for the FTP server's passive-data address.
- Secure glFTPD-to-glFTPD FXP can now complete through SSCN/EPSV when the passive reply only contains a port.

## 1.0.44 — 2026-08-09

### Added

- Added a Themes tab to Global Settings with configurable interface colors, UI and monospace fonts, and font size.
- Added live theme preview, default reset, and JSON theme import/export using `.flux-theme.json` files.
- Added a Changelog button and an integrated release-history window.
- Added a bundled OpenSSL compatibility backend for FTPS servers that cannot negotiate successfully with Windows Schannel.
- Added a per-site option to use the OpenSSL TLS backend directly for glFTPD compatibility.

### Fixed

- Added automatic TLS fallback from Schannel TLS 1.2/1.3 to Schannel TLS 1.2 and then OpenSSL while keeping TLS 1.0/1.1 disabled.
- Self-signed glFTPD and DrFTPD certificates can now be accepted through the existing per-site `Trust invalid TLS certificate` setting.
- DrFTPD FTPS login now authenticates with `USER/PASS` before sending `PBSZ` and `PROT`, matching servers that reject data-protection commands before login.
- Servers that reject `PROT P` can continue with an encrypted control channel and a clear `PROT C` data channel.
- Failed TLS handshakes and certificate validation no longer leave plaintext control streams available to send an invalid unencrypted `QUIT` command.

## 1.0.43 — 2026-08-08

### Fixed

- Regular FTPS, FTP and SFTP transfers now report live throughput in Metrics, Transfer Jobs and the main status bar.
- Local file copies now use the same live throughput measurement as client-mediated transfers.

## 1.0.42 — 2026-08-07

### Added

- Added optional FluxTelemetry support for live ioFTPD FXP progress, speed and transferred-byte monitoring through a shared loopback JSON API.
- Added the standalone FluxTelemetry bridge with `/info`, `/health`, `/activity`, `/metrics` and Server-Sent Events endpoints.
- Added the lightweight ioFTPD `SITE FLUXWHO` Tcl command based directly on `client who`.
- Added automatic migration from the legacy local ioGUI `sites.ini` when FluxTelemetry has no configured credentials, followed by Windows DPAPI password protection.

### Changed

- FXP monitoring now prefers FluxTelemetry without consuming one monitoring FTP slot per transfer.
- Existing `SITE ioGuiExt who` and `SIZE` monitoring remain available automatically as fallback for ioFTPD and other FTP server configurations.
- FluxTelemetry filters its own polling session, normalizes decimal ioFTPD speeds to bytes per second and suppresses routine HTTP request logging.

## 1.0.41 — 2026-08-06

### Fixed

- Passive client-relay connections now skip `EPSV` when the server does not advertise EPSV support and use `PASV` directly.
- Direct secure FXP now drains delayed `200 SSCN:SERVER METHOD` acknowledgements before evaluating the STOR/RETR response.
- FXP servers that omit the preliminary `125/150` reply and respond directly with `226/250` after transferring data are now recognized as successful.
- Completed direct FXP transfers are no longer incorrectly marked failed and retried through client relay because of a delayed SSCN response.

## 1.0.40 — 2026-08-06

### Added

- Added `Start queue` and `Stop queue` controls for manually starting or pausing the complete transfer queue.
- Added separate `Pause selected` and `Resume selected` controls for individual jobs.
- Added queued Local-to-Local file and recursive folder copies with progress reporting.

### Changed

- `Add to queue` now collects files and complete folders without starting them; `Transfer now` continues to begin immediately.
- Stopping the queue pauses waiting jobs and safely cancels active jobs into a resumable paused state.
- Worker slots are warmed only when queued transfers are actually started.

## 1.0.39 — 2026-08-06

### Added

- Added single-instance protection so starting FluxFTP again restores the existing window instead of launching another process.

### Fixed

- Minimizing FluxFTP to the system tray now hides the active window from the Windows taskbar after the minimize transition has completed.
- Restoring FluxFTP from the tray now reliably brings the existing window to the foreground and gives it focus.

## 1.0.38 — 2026-08-05

### Added

- Added RushFTP-style Back, Forward, Up and Refresh navigation controls to both file panes.
- Replaced the plain path fields with editable path-history dropdowns for fast navigation in both Local and Remote mode.
- Local path dropdowns now include available drives, Desktop, Documents, Downloads and up to 20 recently visited folders.

### Changed

- Navigation history is maintained independently for the left and right panes and safely reset when switching between Local and Remote mode.

## 1.0.37 — 2026-08-05

### Added

- Added per-site proxy settings under Site Manager with `Use global proxy`, explicit `No proxy`, SOCKS5, SOCKS4 and HTTP CONNECT modes.
- Per-site proxy settings include host, port, credentials, proxy-side DNS resolution and optional proxying of passive FTP data connections.

### Changed

- Existing sites inherit Global Settings by default, while an explicit per-site proxy or direct-connection override takes precedence for browsing, transfers, worker slots, API jobs and reconnects.
- Per-site proxy passwords are stored in `FluxFTP-sites.ini` using Windows DPAPI protection for the current user.

## 1.0.36 — 2026-08-05

### Fixed

- Dark scrollbar tracks now bind correctly to the current value, maximum and viewport size, so dragging the thumb updates the list position accurately.
- Vertical scrollbars now use the full available height instead of being incorrectly constrained to 13 pixels; horizontal scrollbars retain a compact 13-pixel height.

## 1.0.35 — 2026-08-04

### Added

- Added a dark internal file viewer for local and remote files with automatic encoding detection, selectable UTF-8, Windows-1252, IBM437/DOS, ISO-8859-1, ASCII and UTF-16 encodings, plus optional word wrapping.
- Added a separate `Open externally` action that downloads remote files to the preview cache and opens them through their Windows file association.
- NFO files that are not valid UTF-8 now automatically use IBM437 so classic scene artwork is rendered correctly.

### Changed

- Reworked context menus to remove the standard white WPF icon gutter, prevent clipped labels and match the dark FluxFTP interface.
- Added dark themed horizontal and vertical scrollbars throughout the desktop client.

## 1.0.34 — 2026-08-03

### Added

- Connection Log now reports the negotiated TLS version, cipher suite and strength, certificate subject, issuer, validity period and validation result for the control connection and first protected data connection.

### Fixed

- Explicit and implicit FTPS now automatically reconnect with TLS 1.2 only when the initial TLS 1.2/1.3 negotiation returns an invalid or corrupted TLS frame, improving compatibility with affected glFTPd, OpenSSL and bouncer configurations.

## 1.0.33 — 2026-08-03

### Added

- Explicit FTPS now falls back from `AUTH TLS` to the legacy `AUTH SSL` command for compatible older FTP daemons while retaining TLS 1.2/1.3 SChannel encryption.

## 1.0.32 — 2026-08-03

### Fixed

- Secure FXP now falls back from an unsupported CEPR/EPSV command to CPSV when available, preserving the working ioFTPD TLS route before trying PASV/SSCN.
- Clear reverse FXP now falls back from an unsupported EPSV command to regular PASV instead of abandoning the direct route immediately.
- Dragging from an existing multi-selection now preserves every selected file and folder, allowing several remote folders to be queued through one drag-and-drop operation.

## 1.0.31 — 2026-08-03

### Added

- Added local drive selectors to both panes in Local mode for direct navigation between available Windows drives.
- Added drag-and-drop copying between two Local panes, including multiple files and recursive folders.
- Added Windows Explorer drag-and-drop into Local panes for copying and into Remote panes for queued uploads.

### Fixed

- Local directory views now list the complete directory instead of stopping after 100 entries.
- Drive-only paths such as `D:` now resolve to the drive root (`D:\`) consistently.
- Local directory entries remain sorted with folders before files after changing drives.

## 1.0.30 — 2026-08-03

### Changed

- Repositioned transfer progress, elapsed time, remaining time and queue duration at the right edge of the bottom status bar with responsive sizing for smaller windows.
- Transfer Queue columns now adapt to the available width so job state, paths and progress remain visible.
- Scrolling legend mode now uses the full width of the bottom bar instead of being limited to the left status field.

## 1.0.29 — 2026-08-03

### Changed

- Reworked the bottom transfer status into a FlashFXP-inspired FluxFTP bar with direction and filename, transferred bytes and speed, percentage, elapsed time, estimated remaining time and queue duration.

## 1.0.28 — 2026-08-03

### Added

- Desktop SFTP connections with password authentication, browsing, upload, download, resume and client-relay transfers.
- SHA256 SSH host-key verification with an explicit trust prompt and persistent per-site fingerprints.
- SFTP-backed create folder/file, rename, recursive delete, chmod, preview and queue operations.

### Changed

- Remote-to-remote transfers involving SFTP automatically use client relay because the FTP FXP protocol is not available over SSH.

## 1.0.27 — 2026-08-03

### Added

- Expanded both file-list context menus with Open/View, add-to-queue wording, create-and-enter folder, create-empty-file, and copy FTP/file URL actions.
- Expanded the connection log with timestamped raw FTP commands/server replies, automatic password masking, and Copy/Clear controls.

### Fixed

- UNIX symbolic links displayed as `name -> target` are now parsed with separate name and target metadata and can be navigated like directories.

## 1.0.26 — 2026-07-30

### Fixed

- FXP monitoring now accepts the same ioGuiExt activity rows as ioGUI3 instead of incorrectly discarding active status rows.
- A successful ioGuiExt command without an exact destination filename match no longer disables all fallback progress measurement.
- FluxFTP can read RETR activity from the source site when the destination does not expose matching STOR activity, such as ProFTPD-to-ioFTPD routes.
- The aggregate status progress bar estimates transferred bytes from live FXP speed when ioFTPD does not publish a usable `TRANSFERSIZE` value.

### Changed

- The main status bar now displays live FXP throughput next to the aggregate percentage.

## 1.0.25 — 2026-07-30

### Changed

- Multi-file transfers prewarm the required source/download and destination/upload workers in parallel before queue execution begins.
- Recently returned workers are reused immediately; `NOOP` health checks are now reserved for workers that have been idle for at least 30 seconds.
- Worker prewarming respects each site's login and directional upload/download slot limits and reports its preparation time in the connection log.
- Site Options now includes a cbftp-compatible `Broken PASV` checkbox that selects the site's PORT/active role immediately, without waiting for an initial `425` timeout; the setting is also exposed as `broken_pasv` through the API.

## 1.0.24 — 2026-07-30

### Added

- Learned reverse PASV/PORT routes are persisted per source/destination site pair and restored after FluxFTP restarts.
- Compact FXP phase timings now show CWD, PRET, PROT, PASV/EPSV/CPSV, PORT, SSCN, STOR/RETR acceptance and data-transfer duration.

### Changed

- Independent control commands on the source and destination sessions are issued concurrently where protocol ordering permits.
- A persisted reverse route is removed automatically when it later fails with a timeout or FTP `425`.

## 1.0.23 — 2026-07-30

### Changed

- Successful transfer sessions are retained as reusable site workers instead of logging out after every file.
- Reused workers are health-checked with `NOOP`; disconnected, failed, cancelled or stale-profile sessions are discarded safely.
- Idle worker pools are closed when a site disconnects, its profile changes or FluxFTP exits.

## 1.0.22 — 2026-07-30

### Fixed

- Remote file transfers now change to the source and destination parent directories before issuing relative `RETR` and `STOR` commands, matching FlashFXP behavior and ioFTPD pre-command script expectations.
- The relative-path command flow is used consistently for direct FXP, reversed PASV/PORT FXP, client relay, downloads and uploads.
- FXP failure diagnostics now display the actual `CWD`, relative `RETR` and relative `STOR` command sequence.

## 1.0.21 — 2026-07-29

### Added

- Failed direct FXP attempts now log the source and destination sites, actual connected endpoints, full `RETR` and `STOR` paths, destination parent, data protection, selected route and PRET state.
- Transfer diagnostics deliberately exclude usernames, passwords and authentication commands.

## 1.0.20 — 2026-07-29

### Added

- Optional Advanced Skiplist rules with wildcard or regex matching, File/Directory/Both selection, Allow/Deny actions, scope and ordered first-match evaluation.
- A filled aggregate progress bar in the main status area.

### Changed

- The default main-window size is reduced to fit smaller laptop displays while saved window layouts continue to be restored.
- About now reflects the current FTP, FTPS, FXP, automation, import and ioFTPD feature set.
- The application icon now uses a text-free modern server-and-gear symbol that remains clear at small Windows icon sizes.

### Fixed

- The Close button in the modeless Server Commands window now closes the window correctly.

## 1.0.19 — 2026-07-29

### Fixed

- Self-contained Windows releases once again include the native WPF libraries required before the main window can open.
- `SITE PRE` now runs from the selected release's parent directory, matching ioFTPD's expected working directory.
- Delayed ioFTPD CWD-script replies are skipped so the actual PRE result is displayed and the control channel remains synchronized.
- IRC formatting and other invisible control characters are removed from commands before they are sent.
- PRE script variables now use the release named in the command and its resolved release path.

## 1.0.18 — 2026-07-29

### Added

- Per-site FXP data role selection: Auto, PASV or PORT.
- The selected FXP data role is persisted in `FluxFTP-sites.ini` and exposed through the API as `fxp_data_role`.

### Fixed

- Clear FXP can retry with the reverse PASV/PORT topology when the standard route times out.
- A successful reverse route is remembered for the remaining queued files, avoiding repeated timeout delays.
- FXP starts RETR and STOR together for compatibility with servers that wait for the peer before sending a preliminary reply.

## 1.0.17 — 2026-07-28

### Added

- Clearly visible drag handles for independently resizing Transfer Queue and Connection Log.
- Resized queue and log heights continue to be restored on the next start.

### Fixed

- Auto FXP now uses clear PASV/PORT transfers when either site explicitly uses plain FTP, while two FTPS sites remain secure by default.
- Unix `LIST` summary rows such as `total 10690` are no longer mistaken for files and queued for transfer.
- Window layout persistence now rejects invalid non-finite dimensions instead of failing while the application closes.

## 1.0.16 — 2026-07-28

### Fixed

- Opening the embedded transfer queue no longer crashes while jobs are active.
- The Transfer Jobs progress bars now use the same safe one-way progress binding.

## 1.0.15 — 2026-07-27

### Added

- Multi-selection with Ctrl/Shift in both file panes for batching files and folders.
- Recursive local-folder uploads, matching the existing recursive download and FXP folder handling.
- Multi-item support for Transfer, Queue, drag-and-drop and context-menu transfers.

## 1.0.14 — 2026-07-26

### Fixed

- Transfer slots now reload the latest Site Manager profile before every file, so switching a connected FTP/FTPS site from Auto to Clear FXP takes effect without reconnecting.
- Clarified that Clear mode supports fully plain FTP control connections and PASV/PORT FXP without data TLS.

## 1.0.13 — 2026-07-26

### Added

- RaceTrade-compatible REST spreadjob endpoints for starting and monitoring races.
- RaceTrade/cbftp aliases for site settings including PRET, CEPR, XDUPE, binary mode and named priorities.
- Spreadjob tracking tied to the underlying FluxFTP FXP queue.
- Automatic probing of eligible race sites to locate the announced release source.

### Changed

- The `/sites` and `/sections` collection endpoints now return cbftp-compatible name arrays; their detail endpoints continue to expose full configuration objects.

## 1.0.12 — 2026-07-26

### Added

- FTPRush import guide with separate **Import Sites** and **Import Bookmarks** choices.
- Mutually exclusive **Replace existing** and **Skip existing** conflict handling.
- Import of global bookmarks from `core_setting.json` and per-site bookmarks from `site.json`.
- Persistent `FluxFTP-bookmarks.json` storage.
- Bookmark selectors in both panes for local and site-specific remote navigation.
- Explicit **Kill ghost login (/username)** option in Connection details for ioFTPD accounts.

### Fixed

- Local downloads now close and flush the `.ioftp-part` stream before the final rename, preventing Windows file-lock failures.

## 1.0.11 — 2026-07-26

### Added

- Automatic nuke detection for common ioFTPD/glFTPD directory and marker-file names.
- A Status column and red highlighting for nuked entries in both file panes.
- Nuke status fields in the `/path` API response.
- A manual warning and override before transferring a nuked remote folder.
- Safe blocking of nuked releases before API/d-tool FXP or download jobs are queued.
- Per-site direct FXP protection mode: Auto keeps TLS mandatory, while Clear explicitly permits PASV/PORT FXP without data-channel TLS.
- `fxp_protection` support in the sites API for DrFTPD and other compatibility workflows.
- Fixed an application crash when DrFTPD returns `550` while a remote directory is queued recursively; inaccessible subfolders are now logged and skipped.

## 1.0.10 — 2026-07-23

### Added

- Per-section release validation (Wanker check) with Disabled, Warning and Block modes.
- Allow and deny rules using wildcards or `regex:` patterns.
- Interactive precheck testing in the Sections window.
- Validation before manual `SITE PRE`, raw API PRE and section-based API/d-tool race or FXP jobs.
- Clear precheck logging and manual override for Warning mode.

## 1.0.9 — 2026-07-23

### Added

- Reusable Spread presets that remember section, source site and target sites.
- Apply, save, update and delete controls for presets in the Spread Jobs window.
- Optional unique site descriptions in Connection details and Site Manager.
- `description` in the cbftp-compatible sites API.
- API and UDP downloads can resolve a site by either name or description.

## 1.0.8 — 2026-07-23

### Added

- Per-site **Affiliates (affils)** field in Site Options.
- cbftp-compatible `affils` synchronization through the sites API for d-tool.

### Fixed

- Preserve affiliate values when Site Options are edited and saved.

## 1.0.7 — 2026-07-23

### Added

- cbftp-compatible UDP listener for d-tool `raw`, `fxp`, `race` and `download` commands.
- Headless API and UDP transfers using saved sites and reusable transfer slots.
- cbftp-compatible `/spreadjobs` endpoint.
- Additional site API fields for sections, transfer policies, affiliates and binary mode.
- Support for both standard and compact cbftp FXP command formats.

### Fixed

- Match cbftp's `/raw` response structure and connection behavior for d-tool.
- Remove ANSI color codes from raw FTP command responses.
- Add safe API request diagnostics without logging credentials.
