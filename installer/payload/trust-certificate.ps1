# Runs elevated (started by install.ps1). Adds Calcwright's signing certificate to the computer's
# Trusted People store, which is what Windows checks before installing a sideloaded app package.
param([Parameter(Mandatory = $true)][string]$CertificatePath)
Import-Certificate -FilePath $CertificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
