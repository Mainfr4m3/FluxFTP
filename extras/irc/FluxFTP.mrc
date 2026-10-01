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
menu menubar {
  Commands
  .FluxFTP
  ..Raw command...:fluxftp.menu.raw
  ..FXP transfer...:fluxftp.menu.fxp
  ..Race...:fluxftp.menu.race
  ..Download...:fluxftp.menu.download
  ..-
  ..Configure...:fluxftp.menu.config
  ..Forget configuration:fluxftp forget
  ..Help / About (v1.03):fluxftp help
}

; Channel right-click menu: FluxFTP (alongside tools such as d-tool)
menu channel {
  FluxFTP
  .Raw command...:fluxftp.menu.raw
  .FXP transfer...:fluxftp.menu.fxp
  .Race...:fluxftp.menu.race
  .Download...:fluxftp.menu.download
  .-
  .Configure...:fluxftp.menu.config
  .Forget configuration:fluxftp forget
  .Help / About (v1.03):fluxftp help
}

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

on *:UNLOAD:{
  .timerFluxFTPTimeout off
  if ($sock(fluxftp)) sockclose fluxftp
  echo -ag FluxFTP remote control unloaded.
}
