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
  .timerFluxBridgeMetrics -o 0 2 fluxbridge.metrics.tick
  fluxbridge.metrics.tick
}

alias -l fluxbridge.metrics.stop {
  .timerFluxBridgeMetrics off
  if (%fluxbridge.metrics.last != $null && $titlebar == %fluxbridge.metrics.last) titlebar %fluxbridge.metrics.saved
  unset %fluxbridge.metrics.saved %fluxbridge.metrics.last
}

alias -l fluxbridge.metrics.tick {
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
