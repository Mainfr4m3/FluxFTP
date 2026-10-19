; FluxFTP unified bridge. Load after Visionary.mrc; unload the three older FluxFTP scripts first.

; Component: FluxFTP.mrc
; FluxFTP remote control v1.03 for mIRC and AdiIRC
; Load with: /load -rs "C:\path\to\FluxFTP.mrc"

alias fluxftp {
  if (!$1) { fluxftp.help | return }

  if ($1 == config) {
    if (!$2) { echo -ag FluxFTP: /fluxftp config <password> [port] [IPv4 address] | return }
    set %fluxftp.password $2
    set %fluxftp.port $iif($3 isnum 1-65535,$3,55477)
    set %fluxftp.host $iif($4,$4,127.0.0.1)
    echo -ag FluxFTP: configured for %fluxftp.host $+ : $+ %fluxftp.port
    echo -ag FluxFTP: the API password is stored in your IRC client's variables file.
    return
  }

  if ($1 == forget) {
    unset %fluxftp.password %fluxftp.port %fluxftp.host
    echo -ag FluxFTP: saved configuration removed.
    return
  }

  if ($1 == help) { fluxftp.help | return }
  if ($1 == raw) {
    if (!$3) { echo -ag FluxFTP: /fluxftp raw <site[,site...]> <FTP command> | return }
    fluxftp.send raw $2-
    return
  }
  if ($1 == fxp) {
    if (!$5) { echo -ag FluxFTP: /fluxftp fxp <source-site> <source-path> <target-site> <target-path> | return }
    fluxftp.send fxp $2-
    return
  }
  if ($1 == race) {
    if (!$4) { echo -ag FluxFTP: /fluxftp race <section> <release> <source,target...> | return }
    fluxftp.send race $2-
    return
  }
  if ($1 == download) {
    if (!$3) { echo -ag FluxFTP: /fluxftp download <site> <remote-path> | return }
    fluxftp.send download $2-
    return
  }

  echo -ag FluxFTP: unknown command $qt($1) $+ . Use /fluxftp help
}

alias flux {
  fluxftp $1-
}

alias fluxftp.version return 1.03

alias -l fluxftp.send {
  if (!%fluxftp.password) {
    echo -ag FluxFTP: not configured. Use /fluxftp config <password> [port] [IPv4 address]
    return
  }
  if ($sock(fluxftp)) sockclose fluxftp
  .timerFluxFTPTimeout off
  var %host = $iif(%fluxftp.host,%fluxftp.host,127.0.0.1)
  var %port = $iif(%fluxftp.port,%fluxftp.port,55477)
  var %payload = %fluxftp.password $1-
  sockudp -kt fluxftp %host %port %payload
  if ($sockerr) {
    echo -ag FluxFTP: could not send command (socket error $sockerr $+ ).
    if ($sock(fluxftp)) sockclose fluxftp
    return
  }
  .timerFluxFTPTimeout 1 10 fluxftp.timeout
  echo -ag FluxFTP: sent $qt($1) to %host $+ : $+ %port $+ ...
}

alias -l fluxftp.timeout {
  if ($sock(fluxftp)) sockclose fluxftp
  echo -ag FluxFTP: no response after 10 seconds. Check that FluxFTP and its API are running.
}

alias -l fluxftp.help {
  echo -ag FluxFTP remote control v $+ $fluxftp.version (also available as /flux)
  echo -ag /fluxftp config <password> [port] [IPv4 address]
  echo -ag /fluxftp raw <site[,site...]> <FTP command>
  echo -ag /fluxftp fxp <source-site> <source-path> <target-site> <target-path>
  echo -ag /fluxftp race <section> <release> <source,target...>
  echo -ag /fluxftp download <site> <remote-path>
  echo -ag /fluxftp forget
}

; Main menu: Commands > FluxFTP
; Channel right-click menu: FluxFTP (alongside tools such as d-tool)
alias -l fluxftp.menu.config {
  var %password = $input(API password,e,FluxFTP configuration,%fluxftp.password)
  if (%password == $null) return
  var %port = $input(API port,e,FluxFTP configuration,$iif(%fluxftp.port,%fluxftp.port,55477))
  if (%port == $null) return
  var %host = $input(IPv4 address,e,FluxFTP configuration,$iif(%fluxftp.host,%fluxftp.host,127.0.0.1))
  if (%host == $null) return
  fluxftp config %password %port %host
}

alias -l fluxftp.menu.raw {
  var %sites = $input(Site or comma-separated sites,e,FluxFTP raw command)
  if (%sites == $null) return
  var %command = $input(FTP command,e,FluxFTP raw command)
  if (%command == $null) return
  fluxftp raw %sites %command
}

alias -l fluxftp.menu.fxp {
  var %source.site = $input(Source site,e,FluxFTP FXP)
  if (%source.site == $null) return
  var %source.path = $input(Source path,e,FluxFTP FXP)
  if (%source.path == $null) return
  var %target.site = $input(Target site,e,FluxFTP FXP)
  if (%target.site == $null) return
  var %target.path = $input(Target path,e,FluxFTP FXP)
  if (%target.path == $null) return
  fluxftp fxp %source.site %source.path %target.site %target.path
}

alias -l fluxftp.menu.race {
  var %section = $input(Section,e,FluxFTP race)
  if (%section == $null) return
  var %release = $input(Release name,e,FluxFTP race)
  if (%release == $null) return
  var %sites = $input(Comma-separated source and target sites,e,FluxFTP race)
  if (%sites == $null) return
  fluxftp race %section %release %sites
}

alias -l fluxftp.menu.download {
  var %site = $input(Site,e,FluxFTP download)
  if (%site == $null) return
  var %path = $input(Remote path,e,FluxFTP download)
  if (%path == $null) return
  fluxftp download %site %path
}

on *:UDPREAD:fluxftp:{
  .timerFluxFTPTimeout off
  if ($sockerr) {
    echo -ag FluxFTP: socket error $sockerr $+ .
    sockclose $sockname
    return
  }
  unset %fluxftp.reply
  sockread -f %fluxftp.reply
  if (!$sockbr) return
  var %i = 1, %line
  while ($gettok(%fluxftp.reply,%i,10) != $null) {
    %line = $remove($gettok(%fluxftp.reply,%i,10),$cr)
    echo -ag FluxFTP: %line
    inc %i
  }
  sockclose $sockname
}

on *:UNLOAD:{ fluxbridge.metrics.stop |
  .timerFluxFTPTimeout off
  if ($sock(fluxftp)) sockclose fluxftp
  echo -ag FluxFTP remote control unloaded.
}


; Component: FluxFTP-DLL.mrc
; FluxFTP DLL wrapper v1.03 for mIRC and AdiIRC
; Keep FluxFTP32.dll and FluxFTP64.dll in the same directory as this script.

alias fluxdll.version return 1.03

alias -l fluxdll.path {
  if ($bits == 64) return $scriptdir $+ FluxFTP64.dll
  return $scriptdir $+ FluxFTP32.dll
}

alias fluxdll {
  if ($1 == config) {
    if (!$2) { echo -ag FluxFTP DLL: /fluxdll config <password> [port] [IPv4 address] | return }
    set %fluxdll.password $2
    set %fluxdll.port $iif($3 isnum 1-65535,$3,55477)
    set %fluxdll.host $iif($4,$4,127.0.0.1)
    echo -ag FluxFTP DLL: configured for %fluxdll.host $+ : $+ %fluxdll.port
    return
  }
  if ($1 == forget) { unset %fluxdll.password %fluxdll.port %fluxdll.host | echo -ag FluxFTP DLL: configuration removed. | return }
  if ($1 == version) { echo -ag FluxFTP DLL v $+ $fluxdll.version | return }
  if (!$istok(raw fxp race download,$1,32)) { echo -ag FluxFTP DLL: use raw, fxp, race, download, config, forget or version. | return }
  if (!%fluxdll.password) { echo -ag FluxFTP DLL: configure it first with /fluxdll config <password> | return }
  var %dll = $fluxdll.path
  if (!$isfile(%dll)) { echo -ag FluxFTP DLL: missing $qt(%dll) | return }
  var %request = $iif(%fluxdll.host,%fluxdll.host,127.0.0.1) $iif(%fluxdll.port,%fluxdll.port,55477) %fluxdll.password $1-
  var %reply = $dll(%dll,FluxFTPCommand,%request)
  echo -ag FluxFTP: %reply
}

alias -l fluxdll.menu.config {
  var %password = $input(API password,e,FluxFTP DLL configuration,%fluxdll.password)
  if (%password == $null) return
  var %port = $input(API port,e,FluxFTP DLL configuration,$iif(%fluxdll.port,%fluxdll.port,55477))
  if (%port == $null) return
  var %host = $input(IPv4 address,e,FluxFTP DLL configuration,$iif(%fluxdll.host,%fluxdll.host,127.0.0.1))
  if (%host == $null) return
  fluxdll config %password %port %host
}

alias -l fluxdll.menu.raw {
  var %sites = $input(Site or comma-separated sites,e,FluxFTP DLL raw)
  if (%sites == $null) return
  var %command = $input(FTP command,e,FluxFTP DLL raw)
  if (%command == $null) return
  fluxdll raw %sites %command
}

alias -l fluxdll.menu.fxp {
  var %source.site = $input(Source site,e,FluxFTP DLL FXP)
  if (%source.site == $null) return
  var %source.path = $input(Source path,e,FluxFTP DLL FXP)
  if (%source.path == $null) return
  var %target.site = $input(Target site,e,FluxFTP DLL FXP)
  if (%target.site == $null) return
  var %target.path = $input(Target path,e,FluxFTP DLL FXP)
  if (%target.path == $null) return
  fluxdll fxp %source.site %source.path %target.site %target.path
}

alias -l fluxdll.menu.race {
  var %section = $input(Section,e,FluxFTP DLL race)
  if (%section == $null) return
  var %release = $input(Release name,e,FluxFTP DLL race)
  if (%release == $null) return
  var %sites = $input(Comma-separated source and target sites,e,FluxFTP DLL race)
  if (%sites == $null) return
  fluxdll race %section %release %sites
}

alias -l fluxdll.menu.download {
  var %site = $input(Site,e,FluxFTP DLL download)
  if (%site == $null) return
  var %path = $input(Remote path,e,FluxFTP DLL download)
  if (%path == $null) return
  fluxdll download %site %path
}


; Component: visionary/Visionary-FluxFTP.mrc
; Visionary -> FluxFTP named-pipe bridge v2.0 (live slots, batch chains and mapped IRC DONE notifications)
; Load after Visionary.mrc. Keep both DLL files beside this script.

alias -l fluxvisi.dll return $scriptdir $+ $iif($bits == 64,FluxFTPVisionary64.dll,FluxFTPVisionary32.dll)

alias fluxvisi {
  if ($1 == winbot) {
    if ($0 != 5) { echo -ag Usage: /fluxvisi winbot NETWORK #CHANNEL BOT FLUX_SITE | return }
    set $+(%,fluxvisi.win.,$lower($2),.,$lower($3),.,$lower($4)) $5
    echo -ag FluxFTP race ranking mapping saved for $2 $3 $4 -> $5
    return
  }
  if ($1 == update) {
    if ($0 != 4) { echo -ag Usage: /fluxvisi update SECTION RELEASE SITE | return }
    echo -ag $dll($fluxvisi.dll,FluxFTPTransfer,$2-4 @UPDATE)
    return
  }
  if ($1 == complete) {
    if ($0 != 4) { echo -ag Usage: /fluxvisi complete SECTION RELEASE SITE | return }
    var %dll = $fluxvisi.dll
    if (!$isfile(%dll)) { echo -ag FluxFTP Visionary: missing $qt(%dll) | return }
    echo -ag $dll(%dll,FluxFTPTransfer,$2-4 @DONE)
    return
  }
  if ($1 == donebot) {
    if ($0 != 7) { echo -ag Usage: /fluxvisi donebot NETWORK #CHANNEL BOT FLUX_SITE ANNOUNCED_SECTION FLUX_SECTION | return }
    set $+(%,fluxvisi.done.,$lower($2),.,$lower($3),.,$lower($4),.,$lower($6)) $5 $7
    echo -ag FluxFTP DONE mapping saved for $2 $3 $4 $6 -> $5 $7
    return
  }
  if ($1 == transfer) {
    if ($0 != 5) { echo -ag Usage: /fluxvisi transfer SECTION RELEASE SOURCE TARGET | return }
    var %dll = $fluxvisi.dll
    if (!$isfile(%dll)) { echo -ag FluxFTP Visionary: missing $qt(%dll) | return }
    echo -ag $dll(%dll,FluxFTPTransfer,$2-5)
    return
  }
  if ($1 == test) {
    var %dll = $fluxvisi.dll
    if (!$isfile(%dll)) { echo -ag FluxFTP Visionary: missing $qt(%dll) | return }
    echo -ag FluxFTP Visionary bridge v $+ $dll(%dll,FluxFTPVersion,_)
    return
  }
  echo -ag FluxFTP Visionary commands: /fluxvisi test or /fluxvisi transfer SECTION RELEASE SOURCE TARGET
}

; DONE announcements are forwarded only for explicitly mapped network/channel/bot/section combinations.
on *:TEXT:*:#:{
  var %done.text = $strip($1-)
  var %rank.key = $+($lower($network),.,$lower($chan),.,$lower($nick))
  if ($regex(%done.text,/^STATS:\s+\[1\]\s+(\S+)\s*\/\s*/i)) {
    var %rank.user = $regml(1), %rank.pending = $eval($+(%,fluxvisi.rank.,%rank.key),2)
    if ($numtok(%rank.pending,32) == 4 && $calc($ctime - $gettok(%rank.pending,1,32)) isnum 0-20) {
      echo @FluxFTP $timestamp $dll($fluxvisi.dll,FluxFTPTransfer,$gettok(%rank.pending,3,32) $gettok(%rank.pending,4,32) $gettok(%rank.pending,2,32) $+(@RESULT:,%rank.user))
    }
    unset $+(%,fluxvisi.rank.,%rank.key)
    return
  }
  if (!$regex(%done.text,/^(DONE|COMPLETE|RACE|FAST|HALF):\s+-([A-Z0-9_-]+)-\s+(\S+)\s+/i)) return
  var %event = $regml(1), %done.section = $regml(2), %done.release = $regml(3)
  var %done.binding = $eval($+(%,fluxvisi.done.,$lower($network),.,$lower($chan),.,$lower($nick),.,$lower(%done.section)),2)
  if (%event == DONE || %event == COMPLETE) {
    var %rank.old = $eval($+(%,fluxvisi.rank.,%rank.key),2)
    if (%rank.old != $null) set -eu20 $+(%,fluxvisi.rankblocked.,%rank.key) 1
    unset $+(%,fluxvisi.rank.,%rank.key)
    var %rank.site = $eval($+(%,fluxvisi.win.,%rank.key),2)
    if ($numtok(%done.binding,32) == 2) %rank.site = $gettok(%done.binding,1,32)
    var %rank.blocked = $eval($+(%,fluxvisi.rankblocked.,%rank.key),2)
    if (%rank.site != $null && !%rank.blocked) set -eu20 $+(%,fluxvisi.rank.,%rank.key) $ctime %rank.site %done.section %done.release
  }
  if ($numtok(%done.binding,32) != 2) return
  if (%event == DONE || %event == COMPLETE) fluxvisi complete $gettok(%done.binding,2,32) %done.release $gettok(%done.binding,1,32)
  else fluxvisi update $gettok(%done.binding,2,32) %done.release $gettok(%done.binding,1,32)
}

on *:SIGNAL:FU_FXP_CHAINS:{
  var %chain.tick = $ticks
  var %section = $1, %release = $2, %chains = $3-
  var %dll = $fluxvisi.dll
  window -kze @FluxFTP
  if (!%section || !%release || !%chains) { echo @FluxFTP $timestamp ERROR invalid FU_FXP_CHAINS signal: $1- | return }
  if (!$isfile(%dll)) { echo @FluxFTP $timestamp ERROR missing $qt(%dll) | return }
  echo @FluxFTP $timestamp CHAINS %section %release %chains
  var %root = $deltok($replace(%visi_folder,$chr(92),/),-1,47)
  if ($isfile($+(%root,/custom/fluxftp_chains.pl))) {
    custom fluxftp_chains.pl FLUXFTP_LIVE_CHAINS %section %release %chain.tick %chains
    return
  }
  echo @FluxFTP $timestamp WARNING live slots unavailable; install custom/fluxftp_chains.pl in Visionary. Using Flux site limits.
  fluxvisi.sendchains %section %release $replace(%chains,$chr(32),$chr(44)) %chain.tick
}

alias -l fluxvisi.sendchains {
  var %reply = $dll($fluxvisi.dll,FluxFTPChains,$1-)
  echo @FluxFTP $timestamp %reply
}

on *:SIGNAL:FLUXFTP_LIVE_CHAINS:{
  if ($1 == ERROR) { echo @FluxFTP $timestamp ERROR reading live Visionary slots: $2- | return }
  fluxvisi.sendchains $1 $2 $4 $3
}



; Component: FluxFTP-Metrics.mrc
; Local metrics heartbeat. No API password or DLL required.
alias fluxbridge.metrics {
  if ($1 == off) {
    set %fluxbridge.metrics.disabled 1
    fluxbridge.metrics.stop
    return
  }
  if ($1 == on) {
    unset %fluxbridge.metrics.disabled
    fluxbridge.metrics.start
    return
  }
  echo -ag Usage: /fluxbridge metrics on|off
}

alias -l fluxbridge.metrics.start {
  if (%fluxbridge.metrics.disabled) return
  if (!$timer(FluxBridgeMetrics)) set -e %fluxbridge.metrics.saved $titlebar
  if ($pos(%fluxbridge.metrics.saved,FluxFTP - ,1)) unset %fluxbridge.metrics.saved
  .timerFluxBridgeMetrics -o 0 2 fluxbridge.metrics.tick
  fluxbridge.metrics.tick
}

alias -l fluxbridge.metrics.stop {
  .timerFluxBridgeMetrics off
  if (%fluxbridge.metrics.last != $null && $titlebar == %fluxbridge.metrics.last) titlebar %fluxbridge.metrics.saved
  unset %fluxbridge.metrics.saved %fluxbridge.metrics.last
}

alias -l fluxbridge.metrics.tick {
  if ($pos(%fluxbridge.metrics.saved,FluxFTP - ,1)) unset %fluxbridge.metrics.saved
  var %text = $fluxbridge.metrics.read
  if (%text == $null) %text = FluxFTP offline
  if (%fluxbridge.metrics.saved != $null) %text = %fluxbridge.metrics.saved $chr(124) %text
  if ($titlebar != %text) titlebar %text
  set -e %fluxbridge.metrics.last %text
}

alias -l fluxbridge.metrics.read {
  var %path = $envvar(LOCALAPPDATA) $+ \FluxFTP\irc-metrics.txt
  if ($isfile(%path)) {
    var %line = $read(%path,nt,1)
    var %stamp = $gettok(%line,1,9)
    if (%stamp isnum && $calc($ctime - %stamp) isnum 0-6 && $gettok(%line,2,9) != $null) {
      return $gettok(%line,2-,9)
    }
  }
  return $null
}

alias -l fluxbridge.metrics.share {
  if ($1 != session && $1 != total) return
  if ($status != connected || !$2 || $me !ison $2) {
    echo -ag FluxFTP: join a channel before sharing metrics.
    return
  }
  var %text = $fluxbridge.metrics.read
  if (%text == $null) {
    echo -ag FluxFTP: metrics unavailable. Check that FluxFTP 2.1.8 or later is running.
    return
  }
  if (!$regex(fluxbridge.share,%text,/ - Session (.+?) - Total (.+?) - FXP/)) {
    echo -ag FluxFTP: could not read session and total metrics. Update FluxFTP and the bridge.
    return
  }
  var %amount = $regml(fluxbridge.share,$iif($1 == session,1,2))
  var %label = $iif($1 == session,Session transferred,Total transferred)
  if (!$regex(fluxbridge.peak,%text,/ - Session peak (.+?) - Total peak (.+?) - Total files ([0-9]+)/)) {
    echo -ag FluxFTP: peak speed sharing requires FluxFTP 2.1.10 or later.
    return
  }
  var %peak = $regml(fluxbridge.peak,$iif($1 == session,1,2))
  var %files = $regml(fluxbridge.peak,3)
  var %detail = Max speed: %peak
  if ($1 == session) {
    if ($regex(fluxbridge.files,%text,/ - Files ([0-9]+) - Jobs/)) %files = $regml(fluxbridge.files,1)
    if ($regex(fluxbridge.uptime,%text,/ - Uptime (.+?) - Session peak/)) %detail = %detail $chr(124) Uptime: $regml(fluxbridge.uptime,1)
  }
  msg $2 [FluxFTP] %label $+ : %amount $chr(124) %detail $chr(124) Files: %files
}

on *:START:{ fluxbridge.metrics.start }


alias fluxbridge {
  if ($1 == metrics) { fluxbridge.metrics $2 | return }
  if ($1 == version || $1 == $null) {
    echo -ag FluxFTP Bridge v2.0 - live Visionary slots, batch chains and UDP commands; Visionary DLL v2.0 expected
    return
  }
  echo -ag Usage: /fluxbridge version or /fluxbridge metrics on|off
}
on *:LOAD:{ fluxbridge version | fluxbridge.metrics.start }

menu menubar {
  FluxFTP
  .Bridge version:fluxbridge version
  .Titlebar metrics on:fluxbridge metrics on
  .Titlebar metrics off:fluxbridge metrics off
  .Raw command...:fluxftp.menu.raw
  .FXP transfer...:fluxftp.menu.fxp
  .Race...:fluxftp.menu.race
  .Download...:fluxftp.menu.download
  .Configure API...:fluxftp.menu.config
  .DLL commands
  ..Configure...:fluxdll.menu.config
  ..Raw command...:fluxdll.menu.raw
  ..Race...:fluxdll.menu.race
  .Visionary
  ..Test bridge DLL:fluxvisi test
  .Help:fluxftp help
}
menu channel {
  FluxFTP
  .Bridge version:fluxbridge version
  .Share transferred
  ..Session to channel:fluxbridge.metrics.share session $chan
  ..Total to channel:fluxbridge.metrics.share total $chan
  .Raw command...:fluxftp.menu.raw
  .FXP transfer...:fluxftp.menu.fxp
  .Race...:fluxftp.menu.race
  .Download...:fluxftp.menu.download
  .Configure API...:fluxftp.menu.config
  .Test Visionary bridge:fluxvisi test
}