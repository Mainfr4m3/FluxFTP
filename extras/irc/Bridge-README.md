# FluxFTP unified mIRC bridge 2.0

Keep FluxFTP-Bridge.mrc and the four DLL files together in this folder:
- FluxFTP32.dll
- FluxFTP64.dll
- FluxFTPVisionary32.dll
- FluxFTPVisionary64.dll

Unload the old FluxFTP.mrc, FluxFTP-DLL.mrc and Visionary-FluxFTP.mrc scripts from mIRC first. Keep Visionary.mrc loaded. Then load only FluxFTP-Bridge.mrc after Visionary:

/load -rs "C:\path\FluxFTP-bridge\FluxFTP-Bridge.mrc"

The menu is named FluxFTP. /fluxbridge version and /fluxvisi test should show version 2.0.
The bridge automatically shows local traffic metrics in the mIRC titlebar: current throughput,
session and all-time transferred bytes, FXP rate (last 60 seconds), completed files,
queued/active jobs, idle pool and session uptime. Session/total count successful bytes,
matching Metrics; current throughput can include FXP estimates (marked ~).
The titlebar also shows a 20-character progress bar and percent complete for the
current transfer (the same one selected in FluxFTP's status bar). Estimated FXP
progress is marked ~ and capped at 99%; no active job shows Idle, and unknown-size
transfers show Progress unknown. This progress display requires FluxFTP 2.1.9 or later.
Use /fluxbridge metrics off to restore the previous title, or /fluxbridge metrics on to enable it.
To post transferred data in a channel, right-click the channel and choose FluxFTP >
Share transferred > Session to channel or Total to channel. Each click sends one message
to that channel, for example [FluxFTP] Session transferred: 12.5 GB.
Sharing works even with titlebar metrics turned off. Missing or stale data only shows
a local warning; nothing is posted to the channel.
Detailed channel sharing requires FluxFTP 2.1.10 or later: session messages include
session peak measured speed, completed files and uptime; total messages include
the saved all-time peak speed and completed files. Speed records start with 2.1.10,
use measured aggregate throughput (and direct FXP completion averages), and exclude
display-only estimates. Session records reset when FluxFTP restarts; total records persist.
This requires the updated FluxFTP application on the same Windows account. It reads
%LOCALAPPDATA%\FluxFTP\irc-metrics.txt every two seconds, without API or DLL calls.
Missing or stale data (over six seconds) shows FluxFTP offline; it recovers automatically
when FluxFTP starts. Closing or crashing FluxFTP does not block mIRC.
Enable the Visionary bridge in FluxFTP. FluxFTP and mIRC must run on the same Windows machine and user account for the local pipe.
The unified script already handles FU_FXP_CHAINS; do not add a separate cbftp UDP handler for FluxFTP.

For live slots, copy the supplied fluxftp_chains.pl into Visionary's custom folder (beside its other .pl modules).
Every FU_FXP_CHAINS invokes this read-only module to read User_Files/sites.db and send the source download and destination upload limits with each pair.
Nonzero sectslotsdn/sectslotsup override slotsdn/slotsup, matching Visionary Chains.pm.
Changes are picked up on the next incoming chain, including existing watches; this is not continuous polling.
Missing values retain Flux site settings. The Flux total connection cap and disabled upload/download directions still apply.
Without the custom module, a warning is logged and chains continue with Flux site limits.
Use the updated Flux application together with bridge 2.0. Older Flux applications cannot parse the extended slot pairs.

Pipe error 2 means the FluxFTP named pipe is missing. Check FluxFTP is running, the Visionary bridge is enabled and the current DLLs are loaded.
Configure section paths per site in FluxFTP. Missing section mappings prevent races.
For full startup timing, use the supplied script and DLLs; RACE-Log records VISIONARY TIMING.

Optional API commands: /fluxftp config <password> [port] [IPv4 address]
Optional DLL API commands: /fluxdll config <password> [port] [IPv4 address]
Completion mapping: /fluxvisi donebot NETWORK #CHANNEL BOT FLUX_SITE ANNOUNCED_SECTION FLUX_SECTION


### Confirmed race wins in Metrics

Race route counts are unique Visionary section/release/source/destination announcements received during this FluxFTP session, not wins. Counts reset when FluxFTP restarts; duplicate protection retains the latest 100,000 announcements.

Wins are counted separately per destination. In Metrics, expand Race account names per site and save SITE=nickname entries if the bot uses a name different from the saved FTP login. The default strips a leading login-control ! from the FTP username.

Map each trusted result bot explicitly in mIRC, using your actual network and channel:

    /fluxvisi winbot NETWORK #CHANNEL BOT FLUX_SITE

Existing section-specific /fluxvisi donebot mappings also work. Only mapped bots are read. The supported format is COMPLETE: -SECTION- RELEASE by ... followed within 20 seconds by STATS: [1] username / group - ... . Overlapping COMPLETE/result sequences from the same bot are discarded for 20 seconds to avoid attributing a ranking to the wrong release. Results must match a destination/release announced to FluxFTP this session; repeats are counted once. Missing, delayed, unsupported or unmapped results remain unknown. Wins cannot be attributed to an individual source route when several sources contribute.

Update and reload FluxFTP-Bridge.mrc (or the standalone Visionary-FluxFTP.mrc). No new Visionary DLL is needed. mIRC titlebar metrics still use /fluxbridge metrics on.

Server stats in Metrics now provide Update server stats, SITE USER fallback for unsupported custom SITE username commands, and SITE STATS fallback display. Manual refresh reuses idle shared sessions and never opens extra FTP logins. Busy/unavailable sessions show a visible status.


### Race slots from Visionary (2.1.36)

Install custom/fluxftp_chains.pl in Visionary and reload the updated FluxFTP-Bridge.mrc. Valid Visi download/upload limits control race capacity; imported Flux total limits no longer reduce that capacity. Flux reserves extra connection room for its shared listing and open panes. The server still enforces its own connection limits.

Flux saves the last known values per site/direction in %LOCALAPPDATA%/FluxFTP/visionary-slots.json. If the helper times out or Flux restarts, those values remain in use until Visi sends new values. A direction with no Visi value uses Flux settings. Explicit zero disables that direction. A missing helper cannot supply values that have never been received; install/fix the helper for automatic migration.

Metrics > Detailed statistics lists Race slots per site and identifies Visi or Flux fallback for each direction. Manual transfers retain Flux settings. When both kinds of transfer use a site they share occupied connection counters. Section-specific helper values remain the last received per-site values; this does not add independent per-section connection budgets.
