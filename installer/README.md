# Calcwright installer

`Tools\make-installer.ps1` builds `dist\Calcwright-<version>.exe`, a single file that installs Calcwright
for whoever runs it.

## What users see

1. Double-click the `.exe`.
2. Windows SmartScreen may say "Windows protected your PC". Click **More info**, then **Run anyway**.
   This happens because the installer isn't signed by a commercial certificate authority.
3. **First install only:** a prompt explains that the builder's Calcwright certificate needs to be trusted,
   then Windows asks for administrator approval. That adds the certificate to the computer's
   *Trusted People* store.
4. Calcwright installs, starts, and appears in the Start menu. Uninstall it from **Settings > Apps** like any app.

Requirements: Windows 10 (19041) or newer, x64, internet access for currency rates.

## Releasing an update

1. Bump `Version` in `src\Calculator\Package.appxmanifest` (for example `1.0.0.0` to `1.0.1.0`).
   Windows only upgrades to a higher version.
2. Run `Tools\make-installer.ps1`. The new `.exe` lands in `dist\`.
3. Users run the new `.exe`. It upgrades in place with no certificate prompt.

## The signing certificate

The first run of `make-installer.ps1` on a PC creates a self-signed **CN=Calcwright** code-signing
certificate in that user's certificate store (`Cert:\CurrentUser\My`, valid for 10 years). Only its public
half goes into the installer. Each person who builds Calcwright gets their own certificate.

Every update you hand out must be signed with the same certificate. If it's lost (new PC, reinstalled
Windows), users have to uninstall Calcwright and trust a new certificate. To back it up:
`certmgr.msc` > Personal > Certificates > **Calcwright** > All Tasks > Export > *Yes, export the private key*,
with a long random password and AES256-SHA256 encryption.

- Keep the exported `.pfx` and its password in different places (for example, the password in a password
  manager and the file on encrypted storage). Never commit either to the repository.
- To restore on a new PC, double-click the `.pfx`, choose *Current User*, and put it in *Personal*.
- If the `.pfx` and its password leak together, someone could sign packages that your users' PCs would
  trust. Remove that certificate from their *Trusted People* store and switch to a new one.

If you have a certificate from a commercial certificate authority, signing with it instead skips the trust
prompt and the SmartScreen warning. Change the manifest's `Publisher` to match that certificate's subject.

## Currency rates

Rates come from the free [ExchangeRate-API](https://www.exchangerate-api.com) open endpoint (no key,
updates daily). Its terms require the "Rates By Exchange Rate API" credit shown under the converter and
don't allow redistributing the rate data itself. Each installed copy fetches its own rates, at most about
once a day.
