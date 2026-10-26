param([string]$WslDistribution = 'Debian')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-cli'
$output = Join-Path $PSScriptRoot 'dist/linux-preview-0.1.0-preview.2'
if (Test-Path -LiteralPath (Join-Path $output 'fluxftp-linux-preview_0.1.0.preview2_amd64.deb')) { throw 'Linux preview package already exists; preserve the old build before rebuilding.' }
dotnet publish (Join-Path $PSScriptRoot 'src/IoFtp.Linux/IoFtp.Linux.csproj') -c Release -r linux-x64 --self-contained true -o (Join-Path $output 'publish')
if ($LASTEXITCODE -ne 0) { throw 'Linux publish failed' }
$windowsRoot = $PSScriptRoot.Replace('\', '/')
$convertedRoot = wsl -d $WslDistribution -- wslpath -u $windowsRoot
if ($LASTEXITCODE -ne 0 -or !$convertedRoot) { throw 'Could not map workspace to WSL' }
$linuxRoot = $convertedRoot.Trim()
wsl -d $WslDistribution -- sh "$linuxRoot/docs/linux/package.sh"
if ($LASTEXITCODE -ne 0) { throw 'Debian packaging failed' }
Write-Output "Linux preview artifacts: $output"
