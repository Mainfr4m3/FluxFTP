; Visionary -> FluxFTP named-pipe bridge v1.00 (no UDP/network transport)
; Load after Visionary.mrc. Keep both DLL files beside this script.

alias -l fluxvisi.dll return $scriptdir $+ $iif($bits == 64,FluxFTPVisionary64.dll,FluxFTPVisionary32.dll)

alias fluxvisi {
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

on *:SIGNAL:FU_FXP_CHAINS:{
  var %section = $1, %release = $2, %chains = $3-
  var %dll = $fluxvisi.dll
  window -kze @FluxFTP
  if (!%section || !%release || !%chains) { echo @FluxFTP $timestamp ERROR invalid FU_FXP_CHAINS signal: $1- | return }
  if (!$isfile(%dll)) { echo @FluxFTP $timestamp ERROR missing $qt(%dll) | return }
  var %i = 1
  while ($gettok(%chains,%i,32) != $null) {
    var %pair = $gettok(%chains,%i,32)
    var %source = $gettok(%pair,1,59), %target = $gettok(%pair,2,59)
    if (%source && %target) {
      echo @FluxFTP $timestamp QUEUE %section %release %source -> %target
      var %reply = $dll(%dll,FluxFTPTransfer,%section %release %source %target)
      echo @FluxFTP $timestamp %reply
    }
    else echo @FluxFTP $timestamp ERROR invalid chain pair: %pair
    inc %i
  }
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
