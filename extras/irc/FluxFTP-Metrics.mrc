; Local metrics heartbeat. No API password or DLL required.
alias fluxbridge.metrics {
  if ($1 == status) { fluxbridge.metrics.status | return }
  if ($1 == path) {
    if ($2 == auto) {
      unset %fluxbridge.metrics.file
      echo -ag FluxFTP: metrics folder detection is automatic.
    }
    else {
      var %folder = $replace($noqt($2-),$chr(47),$chr(92))
      if (!$isdir(%folder)) { echo -ag Usage: /fluxbridge metrics path <FluxFTP folder> or auto | return }
      if ($right(%folder,1) == $chr(92)) %folder = $left(%folder,-1)
      set %fluxbridge.metrics.file $+(%folder,\irc-metrics.txt)
      echo -ag FluxFTP: metrics file is %fluxbridge.metrics.file
    }
    if (!%fluxbridge.metrics.disabled) fluxbridge.metrics.tick
    return
  }
  if ($1 == off) {
    set %fluxbridge.metrics.disabled 1
    fluxbridge.metrics.stop
    return
  }
  if ($1 == on) {
    unset %fluxbridge.metrics.disabled
    var %wasRunning = $timer(FluxBridgeMetrics).name
    timerFluxBridgeMetrics -io 0 2 fluxbridge.metrics.tick
    if (!%wasRunning) {
      if ($titlebar != $null) set -e %fluxbridge.metrics.saved $titlebar
      else unset %fluxbridge.metrics.saved
    }
    fluxbridge.metrics.tick
    echo -ag FluxFTP: metrics enabled - timer: $iif($timer(FluxBridgeMetrics).name,on,off)
    return
  }
  echo -ag Usage: /fluxbridge metrics on|off|status or /fluxbridge metrics path <FluxFTP folder>|auto
}

alias fluxbridge.metrics.start {
  if (%fluxbridge.metrics.disabled == 1) return
  fluxbridge.metrics on
}

alias -l fluxbridge.metrics.stop {
  .timerFluxBridgeMetrics off
  if (%fluxbridge.metrics.last != $null) {
    if ($titlebar == %fluxbridge.metrics.last) titlebar %fluxbridge.metrics.saved
  }
  unset %fluxbridge.metrics.saved %fluxbridge.metrics.last
}

alias fluxbridge.metrics.tick {
  var %savedMetrics = $pos(%fluxbridge.metrics.saved,FluxFTP - ,1)
  var %savedOffline = $pos(%fluxbridge.metrics.saved,FluxFTP offline,1)
  if (%savedMetrics > 0) unset %fluxbridge.metrics.saved
  if (%savedOffline > 0) unset %fluxbridge.metrics.saved
  var %text = $fluxbridge.metrics.read
  if (%text == $null) %text = FluxFTP offline
  if (%fluxbridge.metrics.saved != $null) %text = %fluxbridge.metrics.saved $chr(124) %text
  if ($titlebar != %text) titlebar %text
  set -e %fluxbridge.metrics.last %text
}

alias -l fluxbridge.metrics.read {
  if (%fluxbridge.metrics.file != $null) return $fluxbridge.metrics.fresh(%fluxbridge.metrics.file)
  var %pointer = $+($envvar(LOCALAPPDATA),\FluxFTP\irc-metrics.path)
  var %path = $fluxbridge.metrics.line(%pointer)
  var %text = $fluxbridge.metrics.fresh(%path)
  if (%text != $null) return %text
  %text = $fluxbridge.metrics.fresh($+($scriptdir,irc-metrics.txt))
  if (%text != $null) return %text
  %text = $fluxbridge.metrics.fresh($+($scriptdir,..\irc-metrics.txt))
  if (%text != $null) return %text
  ; Compatibility with older FluxFTP EXEs during upgrade.
  return $fluxbridge.metrics.fresh($+($envvar(LOCALAPPDATA),\FluxFTP\irc-metrics.txt))
}

alias -l fluxbridge.metrics.line {
  if ($1- == $null) return $null
  if (!$isfile($1-)) return $null
  var %line = $read($1-,nt,1)
  return %line
  :error
  ; A file can be replaced/locked between $isfile and $read. Do not flood IRC.
  reseterror
  return $null
}

alias -l fluxbridge.metrics.fresh {
  set -e %fluxbridge.metrics.reason no-path
  if ($1- == $null) return $null
  var %path = $noqt($1-)
  set -e %fluxbridge.metrics.reason file-missing
  if (!$isfile(%path)) return $null
  ; Keep each comparison separate: optional isnum ranges in compound conditions
  ; are ambiguous in older mIRC parsers. Read and parse in this same alias.
  var %line = $read(%path,nt,1)
  set -e %fluxbridge.metrics.reason empty-file
  if (%line == $null) return $null
  var %stamp = $gettok(%line,1,9)
  set -e %fluxbridge.metrics.reason invalid-timestamp
  if (%stamp !isnum) return $null
  var %age = $calc($ctime - %stamp)
  set -e %fluxbridge.metrics.reason future-timestamp
  if (%age < -2) return $null
  set -e %fluxbridge.metrics.reason stale-heartbeat
  if (%age > 15) return $null
  var %text = $gettok(%line,2-,9)
  set -e %fluxbridge.metrics.reason missing-title
  if (%text == $null) return $null
  set -e %fluxbridge.metrics.reason accepted
  return %text
  :error
  set -e %fluxbridge.metrics.reason $error
  reseterror
  return $null
}

alias -l fluxbridge.metrics.status {
  echo -ag FluxFTP metrics reader 5 - Client: $version - Script: $script
  echo -ag FluxFTP metrics timer: $iif($timer(FluxBridgeMetrics).name,on,off) - Automatic path: $iif(%fluxbridge.metrics.file == $null,yes,no)
  if (%fluxbridge.metrics.file != $null) { fluxbridge.metrics.inspect %fluxbridge.metrics.file | return }
  var %pointer = $+($envvar(LOCALAPPDATA),\FluxFTP\irc-metrics.path)
  var %path = $fluxbridge.metrics.line(%pointer)
  echo -ag FluxFTP metrics pointer: %pointer - Target: $iif(%path != $null,%path,not found)
  if (%path != $null) fluxbridge.metrics.inspect %path
  fluxbridge.metrics.inspect $+($scriptdir,irc-metrics.txt)
  fluxbridge.metrics.inspect $+($scriptdir,..\irc-metrics.txt)
  fluxbridge.metrics.inspect $+($envvar(LOCALAPPDATA),\FluxFTP\irc-metrics.txt)
}

alias -l fluxbridge.metrics.inspect {
  var %path = $1-
  echo -ag FluxFTP metrics file: %path - Exists: $isfile(%path)
  if (!$isfile(%path)) return
  var %line = $read(%path,nt,1)
  var %stamp = $gettok(%line,1,9)
  echo -ag FluxFTP metrics fields: $numtok(%line,9) - File stamp: %stamp - IRC clock: $ctime
  var %text = $fluxbridge.metrics.fresh(%path)
  var %accepted = no
  if (%text != $null) %accepted = yes
  echo -ag FluxFTP metrics accepted: %accepted - Reason: %fluxbridge.metrics.reason
  if (%stamp isnum) echo -ag FluxFTP metrics age: $calc($ctime - %stamp) seconds
  return
  :error
  echo -ag FluxFTP metrics read failed: $error
  reseterror
}

alias -l fluxbridge.metrics.share {
  if (($1 != session) && ($1 != total)) return
  if (($status != connected) || (!$2) || ($me !ison $2)) {
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
