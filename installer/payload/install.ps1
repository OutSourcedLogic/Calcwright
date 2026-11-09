# Calcwright installer script. Runs from the folder the Calcwright-*.exe extracts itself into.
# 1. Makes Windows trust Calcwright's signing certificate (one-time, needs administrator approval).
# 2. Installs the Windows runtime packages Calcwright needs (skipped if already present).
# 3. Installs or upgrades Calcwright for the signed-in user and starts it.
# A progress window stays up for steps 2-3 (they take a while), and only one copy of setup runs at a time.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName PresentationFramework, System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
# If this script was started hidden, Windows also hides the first window it shows; ShowWindow fixes that.
Add-Type -Namespace CalcwrightSetup -Name Native -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);'

function Show-Message([string]$text, [string]$icon = 'Information', [string]$buttons = 'OK') {
    return [System.Windows.MessageBox]::Show($text, 'Calcwright setup', $buttons, $icon)
}

# --- Only one setup at a time ----------------------------------------------------------------------
$mutex = New-Object System.Threading.Mutex($false, 'Local\CalcwrightSetup')
if (-not $mutex.WaitOne(0)) {
    Show-Message 'Calcwright setup is already running. Its progress window may be behind other windows.' | Out-Null
    exit 0
}

try {
    $bundle = Get-ChildItem $here -Filter '*.msixbundle' | Select-Object -First 1
    $cerPath = Join-Path $here 'Calcwright.cer'
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $cerPath

    # --- 1. Trust the signing certificate --------------------------------------------------------
    $isTrusted = { [bool](Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint) }
    if (-not (& $isTrusted)) {
        $answer = Show-Message ("Calcwright is signed with its own certificate rather than one bought from a certificate authority.`n`n" +
            "Windows needs to trust that certificate once before it will install the app. " +
            "The next window asks for administrator approval to do that.`n`nContinue?") 'Question' 'OKCancel'
        if ($answer -ne 'OK') { exit 1 }

        $trustScript = Join-Path $here 'trust-certificate.ps1'
        try {
            Start-Process powershell.exe -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$trustScript`"", "`"$cerPath`"")
        } catch {
            Show-Message 'Administrator approval was cancelled, so Calcwright was not installed.' 'Warning' | Out-Null
            exit 1
        }
        if (-not (& $isTrusted)) {
            Show-Message 'Windows did not accept the Calcwright certificate, so the app was not installed.' 'Error' | Out-Null
            exit 1
        }
    }

    $installed = Get-AppxPackage Calcwright

    # --- 2 + 3. Install in the background while a progress window shows what's happening -----------
    $sync = [hashtable]::Synchronized(@{ Status = 'Preparing...'; Done = $false; Error = $null })
    $worker = [powershell]::Create().AddScript({
        param($here, $bundlePath, $sync)
        try {
            # Runtime packages: VC++ runtime, .NET Native runtime, WinUI 2. (The self-extractor can't hold
            # folders, so these sit next to this script as *.appx files.)
            foreach ($dep in Get-ChildItem $here -Filter '*.appx') {
                $sync.Status = 'Installing Windows components...'
                try { Add-AppxPackage -Path $dep.FullName -ErrorAction Stop }
                catch { } # already installed, or a newer version is: both are fine
            }
            $sync.Status = 'Installing Calcwright...'
            Add-AppxPackage -Path $bundlePath -ForceApplicationShutdown -ErrorAction Stop
        } catch {
            $sync.Error = $_.Exception.Message
        } finally {
            $sync.Done = $true
        }
    }).AddArgument($here).AddArgument($bundle.FullName).AddArgument($sync)
    $handle = $worker.BeginInvoke()

    $form = New-Object System.Windows.Forms.Form -Property @{
        Text = 'Calcwright setup'; Width = 380; Height = 150; FormBorderStyle = 'FixedDialog'
        MaximizeBox = $false; MinimizeBox = $false; ControlBox = $false; StartPosition = 'CenterScreen'
        TopMost = $true; ShowInTaskbar = $true
    }
    $label = New-Object System.Windows.Forms.Label -Property @{
        Text = $sync.Status; Left = 20; Top = 20; Width = 330; Height = 24
        Font = New-Object System.Drawing.Font('Segoe UI', 10)
    }
    $bar = New-Object System.Windows.Forms.ProgressBar -Property @{
        Style = 'Marquee'; MarqueeAnimationSpeed = 30; Left = 20; Top = 55; Width = 330; Height = 18
    }
    $form.Controls.AddRange(@($label, $bar))
    $timer = New-Object System.Windows.Forms.Timer -Property @{ Interval = 200 }
    $script:shownOnce = $false
    $timer.Add_Tick({
        if (-not $script:shownOnce) {
            $script:shownOnce = $true
            [void][CalcwrightSetup.Native]::ShowWindow($form.Handle, 1)   # SW_SHOWNORMAL
            $form.Activate()
        }
        $label.Text = $sync.Status
        if ($sync.Done) { $timer.Stop(); $form.Close() }
    })
    $form.Add_Load({ $timer.Start() })
    [void]$form.ShowDialog()
    $worker.EndInvoke($handle) | Out-Null
    $worker.Dispose()

    if ($sync.Error) { throw $sync.Error }

    $package = Get-AppxPackage Calcwright
    # Start it the same way the Start menu does.
    Start-Process explorer.exe "shell:AppsFolder\$($package.PackageFamilyName)!App"

    if ($installed -and $installed.Version -eq $package.Version) {
        Show-Message "Calcwright $($package.Version) was already installed. It's up to date." | Out-Null
    } elseif ($installed) {
        Show-Message "Calcwright was updated from $($installed.Version) to $($package.Version)." | Out-Null
    } else {
        Show-Message "Calcwright $($package.Version) is installed. You'll find it in the Start menu." | Out-Null
    }
} catch {
    Show-Message ("Calcwright could not be installed.`n`n" + "$_") 'Error' | Out-Null
    exit 1
} finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
