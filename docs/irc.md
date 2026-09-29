# Built-in IRC and ZNC

Open **Settings > Global Settings > IRC**. Enable IRC, enter the host, port,
nickname and one `#channel`. Use TLS with a valid server certificate. For ZNC,
enable **Connect through ZNC** and enter its username, network and password in
the separate fields. FluxFTP assembles `username/network:password` for IRC PASS.
The saved password is protected with Windows DPAPI for the current Windows user.

Select the saved FTP site that determines administrator status. This version
supports ioFTPD's SiteOp (`1`) and Master (`M`) flags. The site must use FTPS
with certificate validation enabled. Its saved account must be allowed to run
`SITE USER <username>`. An unambiguous, labelled `Flags: 1M` field must appear in
a successful reply. Missing flags, unsupported layouts and failed lookups deny
access; group admin or VFS admin alone does not qualify.

Give the FluxFTP IRC nickname operator rights in the configured channel using
your existing channel services or an operator. FluxFTP requests `MODE +o` only
after FTP verification. A successful verification is not a guarantee that the
IRC server accepted the mode; rejected operations appear in the main log.
Users must be in the configured channel to receive @.

## Two ways to verify users

Both methods can be enabled at the same time.

**Private login:** enable private login and send:

```text
/msg FluxFTP !login MyFtpUser MyFtpPassword
```

FluxFTP first authenticates those credentials to the selected FTP site, then
uses the site's saved account to check that user's current admin flags. The
login is bound to the IRC `nick!user@host` identity for 15 minutes. FTP passwords
are neither saved nor logged by FluxFTP. Your IRC server, clients and ZNC may
log private messages, so configure them accordingly; TLS is not end-to-end
encryption. Passwords containing spaces are supported.

**Saved account link:** add one mapping per line in Global Settings:

```text
MyRegisteredIrcAccount=MyFtpUser
```

Identify with NickServ (or your network's account service), then send `!op`
privately or in the configured channel. FluxFTP uses the server-provided IRCv3
`account-tag`, never a matching nickname, to find the mapping. On networks with
`extended-join`, linked users are also checked when joining the channel. Users
already present when FluxFTP connects can use `!op`.

There is no automatic creation of saved links after password login. Only the
local settings editor creates links. The user's FTP flags are checked anew for
every `!op` and `!announces` request. Identity changes, leaving the channel, kicks,
logout, reconnects and expiry discard the relevant private login. Account
changes cancel in-flight checks. Existing IRC @ status is controlled by the IRC
server: expiry or a disconnected FluxFTP does not automatically remove it.
An explicit failed flag check requests `-o`; `!logout` also requests `-o`.

## Commands

| Command | Action |
| --- | --- |
| `!help` | Show commands privately. |
| `!login <FTP-user> <password>` | Private message only; verify login and request @. |
| `!op` | Verify a saved account link or active login and request @. |
| `!news` | Show only the latest published FluxFTP GitHub release with its publication date and release link. Reply in the requesting channel or private conversation; no FTP login needed. Results are cached for ten minutes. |
| `!logout` | Forget the private login and request removal of @. A saved link remains configured. |

Login/op/news requests are limited to one per second globally and one per identity
every ten seconds. Setup commands are queued in arrival order (maximum 16 waiting
commands) and have no ten-second cooldown. There is a 30-second FTP timeout and automatic
IRC reconnect after 15 seconds. FTP checks do not block IRC PING/PONG.

ZNC must support and provide `server-time` for commands; traffic without it is
ignored in ZNC mode. Old timestamps and all IRC batches are ignored to prevent
history from executing commands. This client does not negotiate direct SASL;
when needed, configure the network login in ZNC. Saved links require IRCv3
account information; without it, use private FTP login.

## Set up sites directly through IRC

Use a **private message to FluxFTP**, over TLS, from a verified FTP administrator.
Channel @ alone does not authorize configuration. Either log in with `!login`
or identify to the IRC account linked in Global Settings. Every setup command
rechecks current FTP admin flags using the locally configured verification site.
That verification site and the bot connection must first be configured locally.

Open a query with FluxFTP, then send one line at a time and wait for its `OK` or
`ERROR`. Example (replace the placeholder credentials and host):

```text
!addsite ExampleSite FTPUSER FTPPASSWORD ftp.example.org:6999 explicit
!slots ExampleSite 3
!maxupdn ExampleSite 3 0
!maxidle ExampleSite 15
!setaffils ExampleSite EXAMPLEGROUP
!setdir ExampleSite MP3 /releases/music/
!ruleadd ExampleSite MP3 if releasename =~ /AUDIOBOOK/i then drop
!ruleadd ExampleSite MP3 if default then allow
!site ExampleSite
!siterules ExampleSite
```

Use `/msg FluxFTP <command>` instead if your client does not have a query window.
`!help setup` lists the syntax. Values containing spaces can be quoted for site
creation/settings, for example `!addsite "My Site" user "my password" host:21`.
Passwords are protected in the saved site file with Windows DPAPI. Responses and
FluxFTP logs never echo the supplied passwords; IRC clients/servers/ZNC can still
record private messages. No FTP connection is started by `!addsite`.

| Command | Effect |
| --- | --- |
| `!addsite SITE USER PASSWORD HOST:PORT [explicit\|implicit\|off]` | Create a saved site; default AUTH TLS; never overwrite an existing site. |
| `!slots SITE N` | Set total slots (1–100); clamp existing up/down limits to that total. |
| `!maxupdn SITE UP DOWN` | Set upload/download limits, each 0–total slots. |
| `!maxidle SITE SECONDS` | Set one idle value, 0–86400 seconds. |
| `!tls SITE explicit\|implicit\|off` | Set FTP TLS mode. Changes to the verification site's TLS must be made locally. |
| `!setaffils SITE GROUP1 GROUP2 ...` | Save affiliations. |
| `!setdir SITE SECTION /path` | Create/update a mapping in `Rules/_site`, retaining that section's rules. |
| `!ruleadd SITE SECTION\|* if CONDITION then allow\|drop` | Append a rule; `*` targets global rules. Run `!setdir` before adding section rules. |
| `!site SITE`, `!sites`, `!siterules SITE` | Read back non-secret settings/counts (lists show up to ten names). |

Supported rule conditions are `default`, `[not] releasename =~ /regex/i`, and
`[not] group in GROUP1,GROUP2` or `[not] tag in TAG1,*pattern*`. Regexes are case
insensitive, and rules are applied in order. `default` appends an unconditional
rule, so place it last. Rules/mappings appear in **Sections > Site rules**; site
settings appear in the normal site selector. No restart is required.

Each accepted setup line is acknowledged independently; the whole script is not
one transaction. On `ERROR`, earlier successful lines remain saved. On `BUSY`,
stop sending and wait for pending replies before retrying unconfirmed lines.
Identity changes, logout and reconnection discard pending commands for that
identity. ZNC history filtering also applies to setup commands.

This is a supported subset of slftp-style setup, not an arbitrary command/script
runner. `!sslmethod` numeric modes, `!legacycwd`, `!autobnctest`, `!country`,
`!ircnick` currently return an
explicit unsupported-command error. Metadata rule conditions also return an
error; the fictional example omits metadata conditions.

## Channels, FiSH and catches

The primary network has a **Network name** in Global Settings (initially
`Default`). Use that name in commands; it is separate from the ZNC network name.
Changes to channels and extra networks apply within approximately five seconds.
Send these commands privately to FluxFTP over the primary TLS connection, after
admin verification, and wait for each reply:

```text
!ircchanadd Default #site-announce
!ircchanblow Default #site-announce cbc:replace-with-your-key
!catchadd ExampleSite Default #site-announce SiteBot PRE PRE,TV TV-1080P
!catchtest Default #site-announce SiteBot PRE TV Show.S01E01.1080p-GROUP
!announces ExampleSite
```

`ExampleSite` must already exist. `!ircchanblow NETWORK #CHANNEL [KEY]` enables
FiSH CBC with a `cbc:` prefix, otherwise ECB. Omitting KEY removes encryption.
Encrypted channels reject unencrypted announcements. `!ircchankey` has the same
syntax for an IRC JOIN key, which is separate from the FiSH key. Keys and network
passwords are protected with Windows DPAPI in `FluxFTP-irc-routing.json`.
`!ircchanlist NETWORK` lists channels, and `!ircchandel NETWORK #CHANNEL` removes
a channel and its catches. The primary control channel remains joined.

`!catchadd SITE NETWORK #CHANNEL BOT1,BOT2 EVENT WORD1,WORD2 [SECTION]` accepts
PRE, NEWDIR, COMPLETE, NUKE, REQUEST or ADDPRE. All words must match complete,
case-insensitive tokens after IRC formatting is removed. Network, channel and
sender must also match. Release extraction looks for a release-group name;
an optional section overrides section detection from configured site sections.
`!catchlist [SITE]` lists the first eight rules with IDs; `!catchdel ID` deletes
one. `!catchtest NETWORK #CHANNEL BOT ANNOUNCEMENT TEXT` is a dry run.

`!announces [SITE]` shows up to five recent caught events. The list is held in memory
and duplicates within two minutes are suppressed. With no caught events, an empty-list message is returned. Catches report news;
they do not start transfers, invoke PRE or apply NUKE commands. IMDB, TV and music
metadata are not required. FiSH is not an identity-verification mechanism.

Additional announcement networks can be configured with
`!ircnetadd NAME HOST:PORT tls|plain [NICK]`, followed by
`!ircnetpass NAME [PASSWORD]` when necessary. For ZNC, the password can contain
`username/network:password`. Use `!ircnetlist` and `!ircnetdel NAME` to inspect
or remove them. Additional networks cannot execute admin commands or reuse the
primary network's account mappings. Primary credentials remain in Global
Settings. ZNC playback carrying batch tags or old server timestamps is ignored;
only the primary connection's explicit ZNC mode requires server timestamps on
every message. Configure extra ZNC networks to disable playback if they do not
provide reliable history tags.

See [slftp compatibility notes](slftp-compatibility.md) for the reference revision
and the deliberately limited command compatibility.

## Validation

```powershell
dotnet run --project tests/IoFtp.IrcTests/IoFtp.IrcTests.csproj
dotnet build src/IoFtp.Desktop/IoFtp.Desktop.csproj
```

Tests use a local simulated IRC server and fake FTP sessions. Real network/ZNC
capabilities, operator rights, and the site's actual `SITE USER` output still
need verification on your installation.

Protocol references: [ZNC](https://github.com/znc/znc),
[IRCv3 account-tag](https://ircv3.net/specs/extensions/account-tag), and
[ioFTPD admin flags](https://www.flashfxp.com/forum/ioftpd/ioftpd/ioftpd-beta/ioftpd-general/13894-ioftpd-v7-0-3-released.html).
