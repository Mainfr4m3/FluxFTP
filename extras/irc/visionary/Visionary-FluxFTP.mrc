; Visionary -> FluxFTP named-pipe bridge v1.03 (batch chains and mapped IRC DONE notifications)
; Load after Visionary.mrc. Keep both DLL files beside this script.

alias -l fluxvisi.dll return $scriptdir $+ $iif($bits == 64,FluxFTPVisionary64.dll,FluxFTPVisionary32.dll)

alias fluxvisi {
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
  if (!$regex(%done.text,/^DONE:\s+-([A-Z0-9_-]+)-\s+(\S+)\s+Size\b/i)) return
  var %done.section = $regml(1), %done.release = $regml(2)
  var %done.binding = $eval($+(%,fluxvisi.done.,$lower($network),.,$lower($chan),.,$lower($nick),.,$lower(%done.section)),2)
  if ($numtok(%done.binding,32) != 2) return
  fluxvisi complete $gettok(%done.binding,2,32) %done.release $gettok(%done.binding,1,32)
}

on *:SIGNAL:FU_FXP_CHAINS:{
  var %chain.tick = $ticks
  var %section = $1, %release = $2, %chains = $3-
  var %dll = $fluxvisi.dll
  window -kze @FluxFTP
  if (!%section || !%release || !%chains) { echo @FluxFTP $timestamp ERROR invalid FU_FXP_CHAINS signal: $1- | return }
  if (!$isfile(%dll)) { echo @FluxFTP $timestamp ERROR missing $qt(%dll) | return }
  var %packed = $replace(%chains,$chr(32),$chr(44))
  echo @FluxFTP $timestamp CHAINS %section %release %chains
  var %reply = $dll(%dll,FluxFTPChains,%section %release %packed %chain.tick)
  echo @FluxFTP $timestamp %reply
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
