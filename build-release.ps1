param([string]$Version = '')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/IoFtp.Desktop/IoFtp.Desktop.csproj'
if (!$Version) { [xml]$projectXml = Get-Content -LiteralPath $project; $Version = $projectXml.Project.PropertyGroup.Version }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Expected version such as 1.0.54 or 1.0.54-preview.1.' }
$numericVersion = ($Version -split '-')[0] + '.0'
$output = Join-Path $PSScriptRoot "dist/release-v$Version"
$staging = Join-Path $PSScriptRoot ('dist/release-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $output, $staging | Out-Null
$bridgeFiles = @('FluxFTP.mrc', 'FluxFTP-DLL.mrc', 'FluxFTP32.dll', 'FluxFTP64.dll', 'README.md',
    'visionary/Visionary-FluxFTP.mrc', 'visionary/FluxFTPVisionary32.dll', 'visionary/FluxFTPVisionary64.dll', 'visionary/README.md')
foreach ($file in $bridgeFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot "extras/irc/$file"))) { throw "Missing bridge asset: $file" }
}
foreach ($variant in @('framework-dependent', 'self-contained')) {
    $target = Join-Path $output "FluxFTP-v$Version-$variant-win-x64.exe"
    if (Test-Path -LiteralPath $target) { throw "Output already exists: $target. Choose a new version or move the existing release." }
}
foreach ($variant in @('framework-dependent', 'self-contained')) {
    $publish = Join-Path $staging $variant
    $standalone = if ($variant -eq 'self-contained') { 'true' } else { 'false' }
    & dotnet publish $project -c Release -r win-x64 --self-contained $standalone -o $publish `
        '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' `
        "-p:Version=$Version" "-p:AssemblyVersion=$numericVersion" "-p:FileVersion=$numericVersion"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $variant" }
    Copy-Item -LiteralPath (Join-Path $publish 'FluxFTP.exe') -Destination (Join-Path $output "FluxFTP-v$Version-$variant-win-x64.exe")
}
$bridgeStage = Join-Path $staging 'FluxFTP-bridge'
foreach ($file in $bridgeFiles) {
    $destination = Join-Path $bridgeStage $file
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "extras/irc/$file") -Destination $destination
}
Compress-Archive -LiteralPath $bridgeStage -DestinationPath (Join-Path $output "FluxFTP-v$Version-bridge.zip")
$guides = Join-Path $staging 'rules-and-guides'
New-Item -ItemType Directory -Force -Path $guides | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Rules/_site/README.md') -Destination $guides
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Rules/_site') -Filter '*.example' | Copy-Item -Destination $guides
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/releasing.md') -Destination $guides
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $guides 'FluxFTP-README.md')
Compress-Archive -LiteralPath $guides -DestinationPath (Join-Path $output "FluxFTP-v$Version-rules-and-guides.zip")
Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $_.Name
} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Release artifacts: $output"
