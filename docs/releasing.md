# Windows releases

Run `./build-release.ps1 -Version 1.0.54-preview.1` from PowerShell with the .NET 8
SDK. It creates two single-file win-x64 executables (framework-dependent and
self-contained), a bridge ZIP, a rules/guides ZIP and SHA-256 checksums under
`dist/release-vVERSION`. The framework-dependent build needs the .NET 8 Desktop
and ASP.NET Core runtimes; the self-contained build includes its runtimes.

Both executables embed the FluxFTP and VISIONARY mIRC scripts and 32-/64-bit DLLs.
Use **IRC Add-ons > Bridge & integrations > Export mIRC bridge** to extract them into an empty folder,
even if only the EXE was downloaded. The separate bridge ZIP contains the same
files for convenient manual installation. The VISIONARY wrapper is in its
`visionary` subfolder. Native DLLs are checked in under `extras/irc`; rebuild them
using their build.ps1 files when changing their C source, then update the bundled
DLLs before building a release.

The GitHub Windows release workflow runs tests, builds the same artifacts and
attaches all of them to a release when a `v*` tag is pushed. Tags containing a
hyphen create prereleases. Manual workflow dispatch builds downloadable workflow
artifacts without publishing a GitHub release. These workflow changes take effect
once committed and pushed; a local build does not publish anything.

Never package the entire working directory or an installed application's config
folder. The build script includes only the explicit bridge assets and guide files.

