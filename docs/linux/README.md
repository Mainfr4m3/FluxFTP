# FluxFTP Linux preview 0.1.0-preview.4

A separate Avalonia desktop client for Linux x64, sharing IoFtp.Core and IoFtp.Engine with Windows. The WPF client is unchanged. This is an early working preview, not feature parity with FluxFTP 2.1.50.

## Install

Target: Debian 12/13 or Ubuntu 24.04 x64 with a graphical desktop. Debian 13/WSL is the verified test platform; Ubuntu compatibility still needs a real-machine test. The .NET runtime is included. Avalonia 11 uses X11/XWayland.

```sh
sudo apt install ./fluxftp-linux-preview_0.1.0.preview4_amd64.deb
fluxftp-linux-preview
```

Alternatively extract the tar.gz and run `./fluxftp-linux-preview/FluxFTP.Linux`. Native dependencies are listed in package.sh. Installing the .deb resolves these automatically. This preview installs under /opt/fluxftp-linux-preview and is separate from the Windows app.

## Available

- Two panes, each independently local or remote; folder navigation and multiple file selection.
- FTP and explicit/implicit FTPS, SFTP via the shared transport; verified host-key fingerprint required for SFTP. Certificate checks remain enabled for normal connections.
- File download, upload, local copy and direct FXP when both panes are FTP/FTPS sites.
- Shared scheduler with imported per-site login/upload/download limits, direction permissions and priority, pause/resume, remove and clear completed.
- Local downloads/copies use a per-job .part file, resume it when the same job is retried and rename only after completion. Existing final files are preserved. Remote upload/FXP refuses existing destinations in this preview.
- Progress and speed for local transfers and protocol log. Saved sites and passwords are encrypted in private Linux configuration files.
- Familiar toolbar: Sites, Tools, Metrics, Manager, Queue, Log, Global Settings and About. Panel visibility is saved. Controls wrap without stretching the Sites button.
- Site editor for connection credentials, slots, priority, listing/TLS options and FXP; section editor for per-site remote paths. Basic session Metrics and a bounded FTP log with copy/clear controls.
- Optional HTTPS API compatible with RaceTrade/CBFTP clients. It can manage sites and sections, browse remote paths, send raw FTP commands, queue transfers and run monitored spread jobs. API jobs use the same FTP login, site slot and transfer scheduler limits as the UI.

## Menus and settings

**Sites** creates, edits, deletes and connects saved sites to either pane. Usernames are sent exactly as entered, including an initial `!`. Editing preserves imported options not shown in the editor. Renaming a site updates its section mappings. Existing jobs keep their original connection snapshot.

**Tools** contains Sections, API settings and a Windows backup import shortcut. Sections maps each section name to a remote folder per site. Leave a site's path blank to exclude that site from the section. Quick Connect fields are available in each pane's collapsible **Quick Connect...** area; transfer buttons are in the panes.

**Metrics**, **Manager**, **Queue** and **Log** show or hide their panels and remember the selection. Metrics shows completed file sizes/counts, current reported speed, queued/active files and session uptime. FXP progress remains based on completed files. **About** includes the GitHub link.

**Global Settings** contains:

- **Local:** default local folder, upload/download slots (0 disables that direction), and serialized local transfers.
- **Jobs & log:** connection/slot waiting timeout, maximum runtime (0 = unlimited), log buffer history and automatic removal of completed manual queue entries. API completion states remain available to API clients.
- **Proxy:** global proxy type, host, port, credentials, DNS and data-connection settings. Per-site proxy overrides are retained.
- **Backup/Import:** password-protected Windows backup import.
- **API:** opens HTTPS API configuration and scheduling limits.

Saving applies slot limits immediately and connection/runtime settings to new connections and transfers. API settings are preserved when other global settings are saved. Changing the default local folder navigates local panes to that folder.

## RaceTrade API

Open **Tools > API settings** or **Global Settings > API**. The server is off by default and listens only on `127.0.0.1:55477` unless changed. Enable it, set an API password (8–128 printable ASCII characters), then save. Saving restarts the API and cancels its current API jobs; ordinary UI transfers keep running. The settings window shows the HTTPS endpoint and the current certificate's SHA-256 fingerprint.

For RaceTrade on the same computer, use `https://127.0.0.1:55477`. For a different computer on your trusted LAN, set the listen address to `0.0.0.0`, allow the selected TCP port through the Linux firewall, and use `https://<Linux-IP>:55477` in RaceTrade. RaceTrade authentication uses an empty username and the API password. The generated HTTPS certificate is self-signed; the fingerprint is shown in FluxFTP's API settings. Keep the API on a trusted network and do not expose its port to the public internet.

Implemented CBFTP-compatible endpoints include `/info`, `/sites`, `/sections`, `/sites/{site}/sections`, `/path`, `/file`, `/raw`, `/transferjobs` and `/spreadjobs`, with create/update/delete, status, abort and reset operations where applicable. API spread jobs monitor growing releases, can discover files added after the first listing, honor section mappings and source priority, and only report completion after source completion and target size verification. Existing targets of equal size are skipped; smaller partial files resume; larger targets are left untouched. API queue capacity, per-site login slots and transfer limits are enforced. UDP and IRC/Visi bridge integration are not included. RaceTrade's currently used HTTPS request format has been exercised against the Linux API; the Windows Visionary bridge remains separate.

API settings, including the password, are stored in the encrypted Linux configuration. Imported Windows backups do not overwrite or enable the Linux API settings.

## Import a Windows backup

1. On Windows, open Global Settings > Backup/Import and **export a password-protected .fluxbackup**. A copied settings directory or DPAPI .fluxrestore recovery file cannot be decrypted on Linux.
2. Copy that file to the Linux machine. Open **Global Settings > Backup/Import > Import Windows backup...**, select the file and enter its backup password.
3. Choose an absolute Linux download directory to replace Windows local drive paths. Read the import summary and confirm the import.
4. Select saved sites from the dropdown in either pane and click Connect. The Sections dropdown navigates to the imported path for that site's name. Use Save site to persist edited connection fields.

Portable site passwords and per-site proxy passwords exported by Windows are imported; no Windows DPAPI decryption is attempted on Linux. Existing Linux sites are merged by ID. Matching IDs are updated, unrelated sites retained. Section definitions are merged by name.

Applied global settings: local upload/download slots, serialized local transfers, starter connection timeout, maximum transfer runtime and global proxy. All imported per-site connection options are retained. Windows UI layout/themes, queued jobs, logs/history, scripts, rules, Visi and API credentials are not activated/imported. Section navigation paths are supported; section validation rules are not yet enforced.

Saved configuration is AES-GCM encrypted in `$XDG_CONFIG_HOME/fluxftp-linux/state.enc` (default `~/.config/fluxftp-linux`). Its local key `secret.key` and state file have owner-only permissions; the directory is mode 700. The key is stored locally, not in a system keyring, so this protects against other local users, not someone controlling your account. Preserve both files together when backing up the Linux configuration. Windows backup passwords are used for the import only and are not retained.

## Not yet ported

Queue persistence/reordering across restarts, bridge/Visi/mIRC integration, Windows race statistics, theme editing, automated recovery and Windows backup export from Linux. Windows backup import is supported as described below. SFTP has not yet been integration-tested on Linux. Secure server-to-server FXP needs testing against real sites; the FTP FXP path is exercised by the local fixture.

The transfer queue starts added jobs automatically. Imported/saved sites retain their IDs; already queued jobs retain the credentials from when they were queued. Upload retry is not resumable when the destination exists. FXP progress is completion-based in this preview.

## Build and verify

From Windows with .NET 8 SDK and Debian WSL:

```powershell
./build-linux.ps1
```

Or publish directly on Linux:

```sh
dotnet publish src/IoFtp.Linux/IoFtp.Linux.csproj -c Release -r linux-x64 --self-contained true -o dist/linux-preview-0.1.0-preview.4/publish
sh docs/linux/package.sh
```

Test dependencies: python3-pyftpdlib, python3-openssl, openssl, xvfb, xauth and the native runtime dependencies.

```sh
dist/linux-preview-0.1.0-preview.4/publish/FluxFTP.Linux --smoke-test
dist/linux-preview-0.1.0-preview.4/publish/FluxFTP.Linux --ui-check
dist/linux-preview-0.1.0-preview.4/publish/FluxFTP.Linux --settings-checks
python3 docs/linux/test-ftp.py "$PWD/dist/linux-preview-0.1.0-preview.4/publish/FluxFTP.Linux"
python3 docs/linux/test-api.py "$PWD/dist/linux-preview-0.1.0-preview.4/publish/FluxFTP.Linux"
FLUXFTP_RENDER_PATH="$PWD/docs/linux/preview.png" xvfb-run -a dist/linux-preview-0.1.0-preview.4/publish/FluxFTP.Linux
```

The fixture uses an ephemeral self-signed TLS certificate, accepted only in the test profile. It compares payloads byte for byte for download, resumed download, upload and plain FTP FXP. GUI and packaging checks are separate from transfer tests.

Avalonia reference: https://docs.avaloniaui.net/docs/supported-platforms

Backup compatibility check: generate a fixture using the Windows test runner `--linux-backup-fixture <path>`, then run the Linux binary with `--backup-checks <path>`. The fixture contains only synthetic credentials.
