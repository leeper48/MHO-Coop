# Packs a release the way the app's updater expects it:
#   MHO-Coop-<version>.zip          MhoCoop.exe and README.txt
#   MHO-Coop-<version>.zip.sha256   the zip's SHA-256
#   MHO-Coop-<version>.zip.sig      the developer's signature of the zip (ECDSA P-256 / SHA-256, base64)
# Nothing is uploaded. Publish the three files on a GitHub release tagged v<version>.
#
# The signature is made with the same release key as MHO Extended Mod Manager, by that app's own signing
# command, on the PC that holds the key:
#
#   powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -Version 1.2.0 -KeyPem <release key .pem>
#
# Without -KeyPem the zip and its .sha256 are written and the signing command is printed, to run where the key is.
# -TestKeyFile signs with a throwaway key from tools\new-test-key.ps1 instead (tests only).
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$KeyPem,                  # the release key's private half (never in the repository)
    [string]$Signer = (Join-Path $env:LOCALAPPDATA 'Programs\MHO Extended Mod Manager\MHO_Ext_ModManager.exe'),
    [string]$TestKeyFile,             # tests only
    [string]$TestPublicKey,           # tests only: check the signature against this instead of the built-in key
    [string]$Program,                 # default: dist\MhoCoop.exe
    [string]$Readme,                  # default: dist\README.txt
    [string]$OutDir                   # default: dist
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
Add-Type -AssemblyName System.Core
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Program) { $Program = Join-Path $root 'dist\MhoCoop.exe' }
if (-not $Readme)  { $Readme  = Join-Path $root 'dist\README.txt' }
if (-not $OutDir)  { $OutDir  = Join-Path $root 'dist' }

# The program's own version must be the version being released: the updater refuses a release where they differ.
$wanted = [Version]$Version
$inside = [Version]((Get-Item $Program).VersionInfo.ProductVersion)
if ($inside.Major -ne $wanted.Major -or $inside.Minor -ne $wanted.Minor -or $inside.Build -ne $wanted.Build) {
    throw "The program is version $inside but the release is $Version. Set the version in src\Program.cs, rebuild, and try again."
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$name = "MHO-Coop-$Version.zip"
$zipPath = Join-Path $OutDir $name
Remove-Item $zipPath, "$zipPath.sha256", "$zipPath.sig" -ErrorAction SilentlyContinue

# Built entry by entry so names are plain ("MhoCoop.exe"), never with a backslash path.
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $Program, 'MhoCoop.exe')
    if (Test-Path $Readme) { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $Readme, 'README.txt') }
} finally { $zip.Dispose() }

$data = [IO.File]::ReadAllBytes($zipPath)
$hash = ([BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash($data))).Replace('-', '').ToLowerInvariant()
[IO.File]::WriteAllText("$zipPath.sha256", "$hash  $name`n", (New-Object Text.UTF8Encoding $false))
"Packed $zipPath"
"SHA-256 $hash"

# ---- sign ----
if ($TestKeyFile) {
    $blob = [System.Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($TestKeyFile), $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    $key = [System.Security.Cryptography.CngKey]::Import($blob, [System.Security.Cryptography.CngKeyBlobFormat]::EccPrivateBlob)
    $dsa = New-Object System.Security.Cryptography.ECDsaCng $key
    $dsa.HashAlgorithm = [System.Security.Cryptography.CngAlgorithm]::Sha256
    [IO.File]::WriteAllText("$zipPath.sig", ([Convert]::ToBase64String($dsa.SignData($data)) + "`n"), (New-Object Text.UTF8Encoding $false))
    $dsa.Dispose(); $key.Dispose()
    "Signed with the TEST key. Not for publishing."
}
elseif ($KeyPem) {
    if (-not (Test-Path $KeyPem)) { throw "Key file not found: $KeyPem" }
    if (-not (Test-Path $Signer)) { throw "MHO Extended Mod Manager was not found at $Signer. Pass its path with -Signer." }
    $run = Start-Process $Signer -ArgumentList '--sign-file', "`"$zipPath`"", "`"$KeyPem`"" -Wait -PassThru
    if (-not (Test-Path "$zipPath.sig")) { throw "Signing did not produce $name.sig (exit code $($run.ExitCode))." }
}
else {
    ''
    'NOT SIGNED. On the PC that holds the release key, run:'
    "  `"$Signer`" --sign-file `"$zipPath`" <release key .pem>"
    'The app only offers a release that has a valid .sig.'
    return
}

# ---- check the signature the way the app will ----
$public = $TestPublicKey
if (-not $public) {
    $source = [IO.File]::ReadAllText((Join-Path $root 'src\ReleaseSigning.cs'))
    $start = $source.IndexOf('BuiltInPublicKey =')
    $public = -join ([regex]::Matches($source.Substring($start, $source.IndexOf(';', $start) - $start), '"([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
}
$keyBytes = [Convert]::FromBase64String($public)
$pubBlob = New-Object byte[] 72
$pubBlob[0] = 0x45; $pubBlob[1] = 0x43; $pubBlob[2] = 0x53; $pubBlob[3] = 0x31; $pubBlob[4] = 32
[Buffer]::BlockCopy($keyBytes, $keyBytes.Length - 64, $pubBlob, 8, 64)
$pubKey = [System.Security.Cryptography.CngKey]::Import($pubBlob, [System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
$check = New-Object System.Security.Cryptography.ECDsaCng $pubKey
$check.HashAlgorithm = [System.Security.Cryptography.CngAlgorithm]::Sha256
$good = $check.VerifyData($data, [Convert]::FromBase64String(([IO.File]::ReadAllText("$zipPath.sig")).Trim()))
$check.Dispose(); $pubKey.Dispose()
if (-not $good) { throw "The signature does not match the key built into the app. The app would refuse this release. (Wrong key file?)" }
"Signature checked against the key the app trusts."
''
"Release ${Version}: publish these on a GitHub release tagged v$Version"
Get-ChildItem $OutDir -Filter "$name*" | ForEach-Object { "  $($_.Name)  $($_.Length) bytes" }
