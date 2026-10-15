# FluxFTP unified mIRC bridge 2.0

Keep FluxFTP-Bridge.mrc and the four DLL files together in this folder:
- FluxFTP32.dll
- FluxFTP64.dll
- FluxFTPVisionary32.dll
- FluxFTPVisionary64.dll

Unload the old FluxFTP.mrc, FluxFTP-DLL.mrc and Visionary-FluxFTP.mrc scripts from mIRC first. Keep Visionary.mrc loaded. Then load only FluxFTP-Bridge.mrc after Visionary:

/load -rs "C:\path\FluxFTP-bridge\FluxFTP-Bridge.mrc"

The menu is named FluxFTP. /fluxbridge version and /fluxvisi test should show version 2.0.
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
