# FluxFTP Linux preview 0.1.0-preview.2

A separate Avalonia desktop client for Linux x64, sharing IoFtp.Core and IoFtp.Engine with Windows. The WPF client is unchanged. This is an early working preview, not feature parity with FluxFTP 2.1.50.

## Install

Target: Debian 12/13 or Ubuntu 24.04 x64 with a graphical desktop. Debian 13/WSL is the verified test platform; Ubuntu compatibility still needs a real-machine test. The .NET runtime is included. Avalonia 11 uses X11/XWayland.

```sh
sudo apt install ./fluxftp-linux-preview_0.1.0~preview2_amd64.deb
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

## Import a Windows backup

1. On Windows, open Global Settings > Backup/Import and **export a password-protected .fluxbackup**. A copied settings directory or DPAPI .fluxrestore recovery file cannot be decrypted on Linux.
2. Copy that file to the Linux machine. Open **Import Windows backup** in the Linux toolbar, select the file and enter its backup password.
3. Choose an absolute Linux download directory to replace Windows local drive paths. Read the import summary and confirm the import.
4. Select saved sites from the dropdown in either pane and click Connect. The Sections dropdown navigates to the imported path for that site's name. Use Save site to persist edited connection fields.

Portable site passwords and per-site proxy passwords exported by Windows are imported; no Windows DPAPI decryption is attempted on Linux. Existing Linux sites are merged by ID. Matching IDs are updated, unrelated sites retained. Section definitions are merged by name.

Applied global settings: local upload/download slots, serialized local transfers, starter connection timeout, maximum transfer runtime and global proxy. All imported per-site connection options are retained. Windows UI layout/themes, queued jobs, logs/history, scripts, rules, Visi and API credentials are not activated/imported. Section navigation paths are supported; section validation rules are not yet enforced.

Saved configuration is AES-GCM encrypted in `$XDG_CONFIG_HOME/fluxftp-linux/state.enc` (default `~/.config/fluxftp-linux`). Its local key `secret.key` and state file have owner-only permissions; the directory is mode 700. The key is stored locally, not in a system keyring, so this protects against other local users, not someone controlling your account. Preserve both files together when backing up the Linux configuration. Windows backup passwords are used for the import only and are not retained.

## Not yet ported

Recursive folder transfers, queue persistence/reordering across restarts, bridge/Visi/mIRC integration, full Metrics, themes/settings editor, server command tools, automated recovery and Windows backup export from Linux. Windows backup import is supported as described below. SFTP has not yet been integration-tested on Linux. Secure server-to-server FXP needs testing against real sites; the FTP FXP path is exercised by the local fixture.

The transfer queue starts added jobs automatically. Imported/saved sites retain their IDs; already queued jobs retain the credentials from when they were queued. Upload retry is not resumable when the destination exists. FXP progress is completion-based in this preview.

## Build and verify

From Windows with .NET 8 SDK and Debian WSL:

```powershell
./build-linux.ps1
```

Or publish directly on Linux:

```sh
dotnet publish src/IoFtp.Linux/IoFtp.Linux.csproj -c Release -r linux-x64 --self-contained true -o dist/linux-preview-0.1.0-preview.2/publish
sh docs/linux/package.sh
```

Test dependencies: python3-pyftpdlib, python3-openssl, openssl, xvfb, xauth and the native runtime dependencies.

```sh
dist/linux-preview-0.1.0-preview.2/publish/FluxFTP.Linux --smoke-test
dist/linux-preview-0.1.0-preview.2/publish/FluxFTP.Linux --ui-check
python3 docs/linux/test-ftp.py "$PWD/dist/linux-preview-0.1.0-preview.2/publish/FluxFTP.Linux"
FLUXFTP_RENDER_PATH="$PWD/docs/linux/preview.png" xvfb-run -a dist/linux-preview-0.1.0-preview.2/publish/FluxFTP.Linux
```

The fixture uses an ephemeral self-signed TLS certificate, accepted only in the test profile. It compares payloads byte for byte for download, resumed download, upload and plain FTP FXP. GUI and packaging checks are separate from transfer tests.

Avalonia reference: https://docs.avaloniaui.net/docs/supported-platforms

Backup compatibility check: generate a fixture using the Windows test runner `--linux-backup-fixture <path>`, then run the Linux binary with `--backup-checks <path>`. The fixture contains only synthetic credentials.
