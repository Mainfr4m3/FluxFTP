$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'FluxFTP.c'
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null

$compilerRoot = $env:FLUXFTP_LLVM_MINGW
if (-not $compilerRoot) {
    throw 'Set FLUXFTP_LLVM_MINGW to an extracted llvm-mingw directory.'
}

$targets = @(
    @{ Compiler = 'i686-w64-mingw32-clang.exe'; Output = 'FluxFTP32.dll' },
    @{ Compiler = 'x86_64-w64-mingw32-clang.exe'; Output = 'FluxFTP64.dll' }
)
foreach ($target in $targets) {
    $compiler = Join-Path (Join-Path $compilerRoot 'bin') $target.Compiler
    if (-not (Test-Path $compiler)) { throw "Compiler not found: $compiler" }
    & $compiler -shared -O2 -Wall -Wextra -Werror `
        '-Wl,--kill-at' -o (Join-Path $output $target.Output) $source -lws2_32
    if ($LASTEXITCODE -ne 0) { throw "Failed to build $($target.Output)" }
}
Write-Host "Built FluxFTP32.dll and FluxFTP64.dll in $output"
