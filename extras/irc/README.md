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
