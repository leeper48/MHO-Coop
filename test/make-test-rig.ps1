# Builds everything needed to try the app without the real game, the real signing key or GitHub.
# Run after build.ps1. Everything goes under test\rig (ignored by git):
#
#   rig\fakegame\MarvelHeroesOmega.exe   a stand-in game window (test\FakeGame.cs)
#   rig\app\                             a portable copy of the app, pointed at the stand-in game
#   rig\feed\                            a signed stand-in "next version" for the updater to find
#   rig\feed-bad\                        the same with one byte of the zip changed (must be refused)
#   rig\run.ps1                          starts the portable copy against rig\feed (pass -Bad for feed-bad)
#
#   powershell -ExecutionPolicy Bypass -File test\make-test-rig.ps1 -ProtoDir <ProtoInput folder>
param([string]$ProtoDir = '')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$rig  = Join-Path $here 'rig'
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$utf8 = New-Object Text.UTF8Encoding $false
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Security.dll',
        '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll'

if (-not (Test-Path (Join-Path $root 'dist\MhoCoop.exe'))) { throw 'Run build.ps1 first.' }
if (Test-Path $rig) { Remove-Item $rig -Recurse -Force }
New-Item -ItemType Directory -Force "$rig\fakegame", "$rig\app", "$rig\next-src", "$rig\next", "$rig\feed", "$rig\feed-bad" | Out-Null

# stand-in game
& $csc /nologo /target:winexe /platform:x64 "/out:$rig\fakegame\MarvelHeroesOmega.exe" (Join-Path $here 'FakeGame.cs')
if ($LASTEXITCODE -ne 0) { throw 'stand-in game did not build' }

# portable copy of the current build (an MhoCoop.ini next to the exe keeps its settings there)
Copy-Item (Join-Path $root 'dist\MhoCoop.exe'), (Join-Path $root 'dist\README.txt') "$rig\app\"
$full = [Version]((Get-Item "$rig\app\MhoCoop.exe").VersionInfo.ProductVersion)
$current = New-Object Version $full.Major, $full.Minor, $full.Build
$ini = '[General]', "GameExe=$rig\fakegame\MarvelHeroesOmega.exe", "ProtoDir=$ProtoDir", 'Server=example.test/SiteConfig.xml',
       'LoadWait=3', 'CheckUpdates=1', 'UpdateCheckAsked=1'
[IO.File]::WriteAllLines("$rig\app\MhoCoop.ini", $ini, $utf8)
Copy-Item "$rig\app\MhoCoop.ini" "$rig\ini-start.txt"

# the same source built as the next version
$next = New-Object Version $current.Major, ($current.Minor + 1), 0
Copy-Item (Join-Path $root 'src\*.cs') "$rig\next-src\"
$program = "$rig\next-src\Program.cs"
$text = [IO.File]::ReadAllText($program)
$now = '"' + $current.Major + '.' + $current.Minor + '.' + $current.Build + '.0"'
if (-not $text.Contains($now)) { throw "version $now not found in src\Program.cs" }
[IO.File]::WriteAllText($program, $text.Replace($now, '"' + $next + '.0"'), $utf8)
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$(Join-Path $root 'MhoCoop.ico')" "/win32manifest:$(Join-Path $root 'app.manifest')" `
    @refs "/out:$rig\next\MhoCoop.exe" "$rig\next-src\*.cs"
if ($LASTEXITCODE -ne 0) { throw 'next version did not build' }
[IO.File]::WriteAllText("$rig\next\README.txt", "README of the stand-in $next`n", $utf8)

# throwaway key, then pack and sign with the project's own release script
$public = [string](& (Join-Path $root 'tools\new-test-key.ps1') -KeyFile "$rig\test-key.bin" | Select-Object -Last 1)
[IO.File]::WriteAllText("$rig\test-public-key.txt", $public, $utf8)
& (Join-Path $root 'tools\make-release.ps1') -Version "$next" -TestKeyFile "$rig\test-key.bin" -TestPublicKey $public `
    -Program "$rig\next\MhoCoop.exe" -Readme "$rig\next\README.txt" -OutDir "$rig\feed" | Out-Null
$zip = "MHO-Coop-$next.zip"
if (-not (Test-Path "$rig\feed\$zip.sig")) { throw 'the stand-in release was not signed' }

# the feed, shaped like GitHub's answer: the signed next version, plus a pre-release and an unsigned release that must be ignored
function Assets($names) { ($names | ForEach-Object { '{"name":"' + $_ + '","browser_download_url":"' + $_ + '"}' }) -join ',' }
$signed = Assets @($zip, "$zip.sha256", "$zip.sig")
$json = '[' +
  '{"tag_name":"v99.0.0","draft":false,"prerelease":true,"html_url":"","body":"pre-release: must be ignored","assets":[' + $signed + ']},' +
  '{"tag_name":"v50.0.0","draft":false,"prerelease":false,"html_url":"","body":"unsigned: must be ignored","assets":[' + (Assets @($zip)) + ']},' +
  '{"tag_name":"v' + $next + '","draft":false,"prerelease":false,"html_url":"","body":"Stand-in release for testing the updater.\n\n- First line of the notes.\n- Second line of the notes.","assets":[' + $signed + ']}' +
  ']'
[IO.File]::WriteAllText("$rig\feed\releases.json", $json, $utf8)

# tampered copy: one byte of the zip changed and the checksum redone to match, signature left as it was
Copy-Item "$rig\feed\*" "$rig\feed-bad\"
$bytes = [IO.File]::ReadAllBytes("$rig\feed-bad\$zip"); $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 1
[IO.File]::WriteAllBytes("$rig\feed-bad\$zip", $bytes)
$hash = ([BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
[IO.File]::WriteAllText("$rig\feed-bad\$zip.sha256", "$hash  $zip`n", $utf8)

$run = @'
# Starts the portable test copy against the local feed. -Bad uses the tampered feed. -Reset puts the current build back first.
param([switch]$Bad, [switch]$Reset)
$rig = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($Reset) {
    Copy-Item (Join-Path (Split-Path -Parent (Split-Path -Parent $rig)) 'dist\MhoCoop.exe') "$rig\app\MhoCoop.exe" -Force
    Copy-Item "$rig\ini-start.txt" "$rig\app\MhoCoop.ini" -Force
}
$env:MHOCOOP_SIGN_PUBKEY = [IO.File]::ReadAllText("$rig\test-public-key.txt").Trim()
$env:MHOCOOP_UPDATE_FEED = if ($Bad) { "$rig\feed-bad" } else { "$rig\feed" }
Start-Process "$rig\app\MhoCoop.exe"
'@
[IO.File]::WriteAllText("$rig\run.ps1", $run, $utf8)

"Test rig ready in $rig"
"  app version $current, stand-in next version $next"
"  start it:  powershell -ExecutionPolicy Bypass -File test\rig\run.ps1   (add -Bad for the tampered feed, -Reset to go back to $current)"
if (-not $ProtoDir) { '  ProtoDir is not set: pass -ProtoDir, or set it in the app''s Settings, before pressing Start.' }
