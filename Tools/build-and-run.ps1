# Developer loop: builds Calcwright (Debug x64), installs it from the build output and launches it.
# Build output and the install layout live in %TEMP%\Calcwright.Build.
# Requires Visual Studio 2026 (UWP + C++ UWP tools, Windows 11 SDK 26100) and Windows Developer Mode.
# To build an installer to share, use Tools\make-installer.ps1 instead.
param(
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
    [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$src = Join-Path $PSScriptRoot '..\src' | Resolve-Path
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -prerelease -property installationPath
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\amd64\MSBuild.exe'
$buildRoot = Join-Path $env:TEMP 'Calcwright.Build'

Write-Host "Building $Configuration|x64 ..."
& $msbuild (Join-Path $src 'Calculator.slnx') /restore /m /nologo /v:minimal `
    /p:Configuration=$Configuration /p:Platform=x64 /p:AppxPackageSigningEnabled=false
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

# The loose output folder is not a runnable layout; unpack the built x64 package instead.
$appPackages = Join-Path $buildRoot "bin\x64\$Configuration\Calculator\Calculator\AppPackages"
$bundle = Get-ChildItem $appPackages -Recurse -Filter '*.msixbundle' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$layout = Join-Path $buildRoot "layout\x64-$Configuration"

# Remove any installed copy (including one from the installer, or the pre-rename dev identity).
Get-AppxPackage Calcwright | Remove-AppxPackage
Get-AppxPackage Microsoft.WindowsCalculator.Dev | Remove-AppxPackage
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force $layout | Out-Null

$zip = [IO.Compression.ZipFile]::OpenRead($bundle.FullName)
try {
    $entry = $zip.Entries | Where-Object { $_.FullName -match '_x64(_Debug)?\.msix$' } | Select-Object -First 1
    $msix = Join-Path $buildRoot 'layout\app-x64.msix'
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $msix, $true)
} finally { $zip.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($msix, $layout)
Remove-Item (Join-Path $layout 'AppxBlockMap.xml'), (Join-Path $layout '[Content_Types].xml') -Force -ErrorAction SilentlyContinue

# Framework packages (VCLibs debug, .NET UWP runtime) that the dev build depends on.
Get-ChildItem $appPackages -Recurse -Include *.appx, *.msix | Where-Object { $_.FullName -match '\\Dependencies\\x64\\' } | ForEach-Object {
    try { Add-AppxPackage -Path $_.FullName -ErrorAction Stop } catch { } # already installed or newer is fine
}

Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
Write-Host "Installed Calcwright from $layout"

if (-not $NoLaunch) {
    $family = (Get-AppxPackage Calcwright).PackageFamilyName
    explorer.exe "shell:AppsFolder\$family!App"
}
