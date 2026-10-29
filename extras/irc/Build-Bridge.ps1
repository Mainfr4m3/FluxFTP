$ErrorActionPreference = 'Stop'
$combined = "; FluxFTP unified bridge. Load after Visionary.mrc; unload the three older FluxFTP scripts first.`r`n"
foreach ($part in @('FluxFTP.mrc', 'FluxFTP-DLL.mrc', 'visionary/Visionary-FluxFTP.mrc', 'FluxFTP-Metrics.mrc')) {
    $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot $part) -Raw
    $content = [regex]::Replace($content, '(?ms)^menu (?:menubar|channel) \{.*?^\}\s*', '')
    $combined += "`r`n; Component: $part`r`n$content`r`n"
}
$combined = [regex]::Replace($combined, '(?im)(^on \*:load:\{)', '$1 fluxbridge version |', 1)
$combined = [regex]::Replace($combined, '(?im)(^on \*:UNLOAD:\{)', '$1 fluxbridge.metrics.stop |', 1)
$combined += @'

alias fluxbridge {
  if ($1 == metrics) { fluxbridge.metrics $2- | return }
  if ($1 == version || $1 == $null) {
    echo -ag FluxFTP Bridge v2.1 - metrics reader 5, live Visionary slots, batch chains and UDP commands; Visionary DLL v2.0 expected
    return
  }
  echo -ag Usage: /fluxbridge version or /fluxbridge metrics on|off|status or /fluxbridge metrics path <FluxFTP folder>|auto
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
'@
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'FluxFTP-Bridge.mrc'), $combined, [System.Text.UTF8Encoding]::new($false))
