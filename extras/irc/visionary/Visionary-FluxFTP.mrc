; Visionary -> FluxFTP named-pipe bridge v2.0 (live slots, batch chains and mapped IRC DONE notifications)
; Load after Visionary.mrc. Keep both DLL files beside this script.

alias -l fluxvisi.dll return $scriptdir $+ $iif($bits == 64,FluxFTPVisionary64.dll,FluxFTPVisionary32.dll)

alias fluxvisi {
  if ($1 == status) {
    echo -ag FluxFTP bridge: pending live-slot replies: $hget(FluxVisiPending,0).item $+ . Script: $script
    return
  }
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
    ; Arm before /custom: a missing callback or an error in Visionary must
    ; never swallow the chain permanently. Existing Flux/live limits apply.
    var %request.id = $+(%chain.tick,.,$crc($+(%section,.,%release),0))
    hadd -m FluxVisiPending %request.id %section %release $replace(%chains,$chr(32),$chr(44)) %chain.tick
    .timer $+ FluxVisiLive. $+ %request.id -mo 1 1000 fluxvisi.live.timeout %request.id
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

alias -l fluxvisi.live.timeout {
  var %request = $hget(FluxVisiPending,$1)
  if (%request == $null) return
  hdel FluxVisiPending $1
  echo @FluxFTP $timestamp WARNING live-slot helper did not reply within 1s. Forwarding chain using existing Flux site limits. Check the Visionary console and custom/fluxftp_chains.pl.
  fluxvisi.sendchains %request
}

on *:SIGNAL:FLUXFTP_LIVE_CHAINS:{
  if ($1 == ERROR) { echo @FluxFTP $timestamp ERROR reading live Visionary slots: $2- | return }
  if ($0 < 4 || $3 !isnum) { echo @FluxFTP $timestamp ERROR invalid live-slot callback: $1- | return }
  var %request.id = $+($3,.,$crc($+($1,.,$2),0))
  .timer $+ FluxVisiLive. $+ %request.id off
  if ($hget(FluxVisiPending,%request.id) != $null) hdel FluxVisiPending %request.id
  ; Late replies still update live limits after a fallback; Flux deduplicates
  ; the watch and destination files instead of starting a second race.
  fluxvisi.sendchains $1 $2 $4 $3
}

menu menubar {
  Commands
  .FluxFTP Visionary
  ..Test DLL:fluxvisi test
}

menu channel {
  FluxFTP Visionary
  .Test DLL:fluxvisi test
}
