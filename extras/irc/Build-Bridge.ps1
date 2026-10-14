$ErrorActionPreference = 'Stop'
$combined = "; FluxFTP unified bridge. Load after Visionary.mrc; unload the three older FluxFTP scripts first.`r`n"
foreach ($part in @('FluxFTP.mrc', 'FluxFTP-DLL.mrc', 'visionary/Visionary-FluxFTP.mrc')) {
    $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot $part) -Raw
    $content = [regex]::Replace($content, '(?ms)^menu (?:menubar|channel) \{.*?^\}\s*', '')
    $combined += "`r`n; Component: $part`r`n$content`r`n"
}
$combined = [regex]::Replace($combined, '(?im)(^on \*:load:\{)', '$1 fluxbridge version |', 1)
$combined += @'

alias fluxbridge {
  if ($1 == version || $1 == $null) {
    echo -ag FluxFTP Bridge v1.03 - batch chains, UDP commands and Visionary DLL v1.05 expected
    return
  }
  echo -ag Usage: /fluxbridge version
}
on *:LOAD:{ fluxbridge version }

menu menubar {
  FluxFTP
  .Bridge version:fluxbridge version
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
  .Raw command...:fluxftp.menu.raw
  .FXP transfer...:fluxftp.menu.fxp
  .Race...:fluxftp.menu.race
  .Download...:fluxftp.menu.download
  .Configure API...:fluxftp.menu.config
  .Test Visionary bridge:fluxvisi test
}
'@
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'FluxFTP-Bridge.mrc'), $combined, [System.Text.UTF8Encoding]::new($false))
