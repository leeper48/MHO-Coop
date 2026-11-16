# Tests only: makes a throwaway signing key so the updater can be exercised without the real release key.
# The real key is the one that signs MHO Extended Mod Manager; it is never created or stored by this project.
#
# Writes the private half to -KeyFile (encrypted for this Windows account) and prints the public half,
# which goes in the MHOCOOP_SIGN_PUBKEY environment variable for the test run.
param([Parameter(Mandatory = $true)][string]$KeyFile)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
Add-Type -AssemblyName System.Core

if (Test-Path $KeyFile) { throw "A key already exists at $KeyFile." }

$options = New-Object System.Security.Cryptography.CngKeyCreationParameters
$options.ExportPolicy = [System.Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
$key = [System.Security.Cryptography.CngKey]::Create([System.Security.Cryptography.CngAlgorithm]::ECDsaP256, [NullString]::Value, $options)   # no name: the key is never kept in the Windows key store
$blob = $key.Export([System.Security.Cryptography.CngKeyBlobFormat]::EccPrivateBlob)   # "ECS2", size, X, Y, D
$key.Dispose()

$sealed = [System.Security.Cryptography.ProtectedData]::Protect($blob, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
New-Item -ItemType Directory -Force (Split-Path -Parent $KeyFile) | Out-Null
[IO.File]::WriteAllBytes($KeyFile, $sealed)
[Convert]::ToBase64String($blob[8..71])
