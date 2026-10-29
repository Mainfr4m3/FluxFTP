; FluxFTP remote control v1.04 for mIRC and AdiIRC
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

alias fluxftp.version return 1.04

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
  ..Help / About (v1.04):fluxftp help
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
  .Help / About (v1.04):fluxftp help
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

; RAW results are a flat JSON array. Decode only string fields, once, without
; evaluating server text as mIRC commands or identifiers. No JSON add-on needed.
alias fluxftp.reply {
  var -n %rest = $1-, %count = 0
  if ($left(%rest,1) != $chr(91)) { fluxftp.reply.lines FluxFTP: %rest | return }
  while ($regex(fluxftp.object,%rest,/(\{(?:[^"{}]|"(?:\\.|[^"\\])*")*\})/)) {
    var -n %object = $regml(fluxftp.object,1)
    var %next = $calc($regml(fluxftp.object,1).pos + $len(%object))
    %rest = $mid(%rest,%next)
    var -n %site = $fluxftp.json.field(%object,name)
    if (%site != $null) {
      var -n %result = $fluxftp.json.field(%object,result)
      var -n %error = $fluxftp.json.field(%object,error)
      var -n %prefix = $+(FluxFTP[,%site,]:)
      if (%error != $null) fluxftp.reply.lines %prefix ERROR: %error
      elseif (%result != $null) fluxftp.reply.lines %prefix %result
      else echo -ag %prefix Command returned an empty response.
      inc %count
    }
  }
  if (%count == 0) fluxftp.reply.lines FluxFTP: $1-
}

alias -l fluxftp.json.field {
  var %pattern = $+(/",$2,"\s*:\s*("(?:\\.|[^"\\])*")/)
  if (!$regex(fluxftp.field,$1,%pattern)) return
  var -n %quoted = $regml(fluxftp.field,1)
  return $fluxftp.json.decode($mid(%quoted,2,$calc($len(%quoted) - 2)))
}

alias -l fluxftp.json.decode {
  ; A single replacement pass preserves spaces and literal \\n text.
  return $regsubex(fluxftp.decode,$1,/\\(u[0-9a-fA-F]{4}|["\\\/bfnrt])/g,$fluxftp.json.escape(\1))
}

alias -l fluxftp.json.escape {
  if ($left($1,1) == u) return $chr($base($mid($1,2),16,10))
  if ($1 == n) return $lf
  if ($1 == r) return $cr
  if ($1 == t) return $chr(9)
  if ($1 == b) return $chr(8)
  if ($1 == f) return $chr(12)
  return $1
}

alias -l fluxftp.reply.lines {
  var -n %prefix = $1, %text = $remove($2-,$cr), %i = 1
  while (%i <= $numtok(%text,10)) {
    var -n %line = $gettok(%text,%i,10)
    echo -ag %prefix %line
    inc %i
  }
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
  fluxftp.reply %fluxftp.reply
  sockclose $sockname
}

on *:UNLOAD:{
  .timerFluxFTPTimeout off
  if ($sock(fluxftp)) sockclose fluxftp
  echo -ag FluxFTP remote control unloaded.
}
