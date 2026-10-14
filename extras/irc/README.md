## Recommended: one unified script

With v1.0.102, use `FluxFTP-Bridge.mrc` instead of loading the three individual FluxFTP scripts. Keep all four DLLs alongside this file. Load it after Visionary.mrc. Unload `FluxFTP.mrc`, `FluxFTP-DLL.mrc` and `Visionary-FluxFTP.mrc` first (only the scripts, not Visionary itself), then `/load -rs "C:\path\FluxFTP-Bridge.mrc"`.

The menu is named **FluxFTP**. The unified script handles `FU_FXP_CHAINS` directly: Visionary section, release and each source;target pair are forwarded over the local named pipe. Do not add the cbftp sockudp handler for Flux; it sends cbftp's separate UDP protocol. Keep only one Flux handler for this signal. The original scripts remain in the source repository for building the unified file; they are not included in release packages.

Run `/fluxvisi test`: the included Visionary DLL must report 1.04. Enable the Visionary bridge in FluxFTP on the same Windows machine and user account as mIRC. Pipe error 2 means the named pipe is missing: check Flux is running, bridge enabled and the DLL version. The unified script includes chain timestamps; `since-chain=unknown` means an older caller/script or invalid timing data.

Sections still require per-site mappings; `Section TV-2160P-DE is not configured for TIS` must be corrected in the section paths, not by adding arbitrary path prefixes.

# FluxFTP remote control v1.03 for mIRC and AdiIRC

`FluxFTP.mrc` controls a running FluxFTP instance through its local,
password-protected UDP API. The same script works in current mIRC and AdiIRC.

## Set up FluxFTP

1. Open **Settings > Global settings** in FluxFTP.
2. Enable **HTTPS/JSON API**. The UDP control listener starts on the same port.
3. Leave **Localhost only** enabled when the IRC client runs on the same PC.
4. Note the API password and port (the default port is `55477`).
5. Keep FluxFTP running, or minimize it to the system tray.

## Load and configure the script

In mIRC or AdiIRC, enter:

```text
/load -rs "C:\path\to\FluxFTP.mrc"
/fluxftp config YOUR_API_PASSWORD 55477 127.0.0.1
```

The shorter `/flux` alias works too. The password may not contain spaces because
the FluxFTP UDP protocol uses the first space as its separator. mIRC/AdiIRC saves
the configured password in its variables file; use `/fluxftp forget` to remove it.

After loading, the graphical menu is available under **Commands > FluxFTP** in
the main menu bar. A top-level **FluxFTP** entry also appears directly in the
right-click menu inside channel windows, alongside tools such as d-tool. Its
entries open input dialogs for configuration, raw commands, FXP, races and
downloads. The command-line aliases remain available as well.

## Commands

```text
/flux raw SITE1 site stat
/flux raw SITE1,SITE2 site who
/flux fxp SITE1 /incoming/Release.Name SITE2 /archive/Release.Name
/flux race MOVIES Release.Name SITE1,SITE2,SITE3
/flux download SITE1 /incoming/Release.Name
```

- `raw` executes an FTP command on one or more saved sites.
- `fxp` queues a site-to-site transfer. Paths containing spaces are not supported
  by the compact UDP command format.
- `race` uses the first listed site as source and queues the release to every
  following site. The section must exist for all participating sites.
- `download` queues a remote path to FluxFTP's configured download directory.

Use `/fluxftp help` for the command summary. A request times out after ten seconds.
UDP itself does not guarantee delivery, so a timeout means the command's final
state is unknown; check FluxFTP's Transfer Jobs before sending it again.

To unload the script:

```text
/unload -rs "C:\path\to\FluxFTP.mrc"
```

## Native DLL edition

The optional DLL edition is intended for Windows IRC clients that implement the
mIRC/AdiIRC `/dll` calling convention:

- `FluxFTP32.dll` for a 32-bit IRC client.
- `FluxFTP64.dll` for a 64-bit IRC client.
- `FluxFTP-DLL.mrc` supplies the menus and automatically selects the matching
  DLL using `$bits`.

Keep all three files in the same directory, then load the wrapper:

```text
/load -rs "C:\path\to\FluxFTP-DLL.mrc"
/fluxdll config YOUR_API_PASSWORD 55477 127.0.0.1
```

The menu appears as **Commands > FluxFTP DLL** and as **FluxFTP DLL** in the
channel right-click menu. The native export `FluxFTPCommand` accepts
`<IPv4> <port> <password> <command>` and returns FluxFTP's response. This allows
another IRC client with the same DLL ABI to create its own small wrapper even if
it does not support `.mrc` files. A client with a different plugin API needs a
client-specific adapter; Windows DLL bitness alone does not make plugin APIs
interchangeable.

The DLL call waits for FluxFTP's UDP response (up to ten seconds), so the pure
`FluxFTP.mrc` socket edition remains preferable in mIRC/AdiIRC because it does
not block the IRC user interface while waiting.
