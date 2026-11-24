# The 2Twisted fork's Windows release: OpenTabletDriver-<version>-<suffix>_win-x64.zip in dist/, PORTABLE (an empty
# userdata folder beside the programs, so it never touches an installed OpenTabletDriver's settings).
#   powershell -ExecutionPolicy Bypass -File eng\windows\package-2twisted.ps1 [-Suffix 2twisted.1]
# Builds with upstream's package.ps1 (daemon, console, WPF window), then zips them with LICENSE, FORK.md and
# README-2TWISTED.txt. (Upstream's own zip step needs a convert_to_portable.bat that 0.6.8.1 no longer has.)
param([string]$Suffix = '2twisted.1')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $root

$env:VERSION_SUFFIX = $Suffix
& "$PSScriptRoot/package.ps1" -output dist -config Release -isPackage $false
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

$version = (git describe --tags --abbrev=0).TrimStart('v')
$name = "OpenTabletDriver-$version-${Suffix}_win-x64"
$stage = Join-Path $root "dist/$name"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'userdata') | Out-Null
New-Item -ItemType File (Join-Path $stage 'userdata/.keep') | Out-Null
Copy-Item "$root/dist/*.exe" $stage
Copy-Item "$root/LICENSE", "$root/FORK.md", "$PSScriptRoot/README-2TWISTED.txt" $stage

foreach ($exe in Get-ChildItem "$stage/*.exe") {
    if ($exe.VersionInfo.ProductVersion -ne "$version-$Suffix") { throw "$($exe.Name) says $($exe.VersionInfo.ProductVersion), expected $version-$Suffix" }
}

$zip = Join-Path $root "dist/$name.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "Release: $zip"
