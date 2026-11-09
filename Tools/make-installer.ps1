# Builds a signed Release copy of Calcwright and wraps it in one self-extracting installer:
#   dist\Calcwright-<version>.exe
#
# The first run on a PC creates a self-signed "CN=Calcwright" code-signing certificate in
# Cert:\CurrentUser\My. Keep it: every update must be signed with the same certificate, or users have
# to uninstall and trust a new one. See installer\README.md for backing it up.
#
# Bump the Version in src\Calculator\Package.appxmanifest before building an update.
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\dist')
)
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$src = Join-Path $root 'src'
$payload = Join-Path $root 'installer\payload'
$buildRoot = Join-Path $env:TEMP 'Calcwright.Build'
$stage = Join-Path $buildRoot 'installer-stage'

# --- Version from the manifest ---------------------------------------------------------------------
[xml]$manifest = Get-Content (Join-Path $src 'Calculator\Package.appxmanifest')
$version = $manifest.Package.Identity.Version
$publisher = $manifest.Package.Identity.Publisher
Write-Host "Calcwright $version (publisher $publisher)"

# --- Signing certificate ---------------------------------------------------------------------------
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $publisher -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    Write-Host 'Creating the Calcwright signing certificate (one time)...'
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher `
        -FriendlyName 'Calcwright package signing' -KeyUsage DigitalSignature `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10) `
        -TextExtension @('2.5.29.19={text}')   # basic constraints: not a CA
}
Write-Host "Signing with certificate $($cert.Thumbprint) (expires $($cert.NotAfter.ToShortDateString()))"

# --- Release build, signed -------------------------------------------------------------------------
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = Join-Path (& $vswhere -latest -prerelease -property installationPath) 'MSBuild\Current\Bin\amd64\MSBuild.exe'
Write-Host 'Building Release|x64 (this takes several minutes)...'
# /t:Calculator builds just the app (and what it depends on), so the test packages aren't signed too.
& $msbuild (Join-Path $src 'Calculator.slnx') /t:Calculator /restore /m /nologo /v:minimal `
    /p:Configuration=Release /p:Platform=x64 `
    /p:AppxPackageSigningEnabled=true /p:PackageCertificateThumbprint=$($cert.Thumbprint)
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

$appPackages = Join-Path $buildRoot 'bin\x64\Release\Calculator\Calculator\AppPackages'
$packageDir = Get-ChildItem $appPackages -Directory | Where-Object Name -like "*_$($version)_Test" | Select-Object -First 1
if (-not $packageDir) { throw "No AppPackages\*_$($version)_Test folder found under $appPackages" }
$bundle = Get-ChildItem $packageDir.FullName -Filter '*.msixbundle' | Select-Object -First 1
$deps = Get-ChildItem (Join-Path $packageDir.FullName 'Dependencies\x64') -Include *.appx, *.msix -Recurse

$sig = Get-AuthenticodeSignature $bundle.FullName
if ($sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw "Bundle isn't signed with the Calcwright certificate." }

# --- Stage the installer contents (flat: the self-extractor can't hold folders) --------------------
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $bundle.FullName (Join-Path $stage 'Calcwright.msixbundle')
foreach ($d in $deps) { Copy-Item $d.FullName $stage }
Export-Certificate -Cert $cert -FilePath (Join-Path $stage 'Calcwright.cer') -Type CERT | Out-Null  # public part only
Copy-Item (Join-Path $payload '*') $stage

# --- Build the single .exe with IExpress (part of Windows) -----------------------------------------
New-Item -ItemType Directory -Force $OutDir | Out-Null
$exeName = "Calcwright-$version.exe"
$tempExe = Join-Path $buildRoot $exeName
$files = Get-ChildItem $stage -File
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$tempExe
FriendlyName=Calcwright $version
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File install.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
$(($files | ForEach-Object -Begin { $i = 0 } -Process { "FILE$i=`"$($_.Name)`""; $i++ }) -join "`r`n")
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
$(($files | ForEach-Object -Begin { $i = 0 } -Process { "%FILE$i%="; $i++ }) -join "`r`n")
"@
$sedPath = Join-Path $buildRoot 'Calcwright.sed'
[IO.File]::WriteAllText($sedPath, $sed, [Text.Encoding]::ASCII)

if (Test-Path $tempExe) { Remove-Item $tempExe -Force }
Write-Host 'Packing the installer...'
# IExpress can't open a quoted SED path; use the 8.3 short form if the path has spaces.
$sedArg = $sedPath
if ($sedArg -match ' ') { $sedArg = (New-Object -ComObject Scripting.FileSystemObject).GetFile($sedPath).ShortPath }
Start-Process "$env:WINDIR\System32\iexpress.exe" -ArgumentList "/N /Q $sedArg" -Wait
if (-not (Test-Path $tempExe)) { throw 'IExpress did not produce the installer.' }

$final = Join-Path $OutDir $exeName
Copy-Item $tempExe $final -Force
Write-Host ("Installer: {0} ({1:N1} MB)" -f (Resolve-Path $final), ((Get-Item $final).Length / 1MB))
