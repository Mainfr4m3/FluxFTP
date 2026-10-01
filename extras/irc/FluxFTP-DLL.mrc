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

menu menubar {
  Commands
  .FluxFTP DLL
  ..Raw command...:fluxdll.menu.raw
  ..FXP transfer...:fluxdll.menu.fxp
  ..Race...:fluxdll.menu.race
  ..Download...:fluxdll.menu.download
  ..-
  ..Configure...:fluxdll.menu.config
  ..Forget configuration:fluxdll forget
  ..Version 1.03:fluxdll version
}

menu channel {
  FluxFTP DLL
  .Raw command...:fluxdll.menu.raw
  .FXP transfer...:fluxdll.menu.fxp
  .Race...:fluxdll.menu.race
  .Download...:fluxdll.menu.download
  .-
  .Configure...:fluxdll.menu.config
  .Forget configuration:fluxdll forget
  .Version 1.03:fluxdll version
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
