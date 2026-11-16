# Builds dist\MhoCoop.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

$ico = Join-Path $root 'MhoCoop.ico'
if (-not (Test-Path $ico)) { & (Join-Path $root 'make-icon.ps1') }

$out = Join-Path $dist 'MhoCoop.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ /warn:4 `
    "/win32icon:$ico" "/win32manifest:$(Join-Path $root 'app.manifest')" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "/out:$out" (Join-Path $root 'src\*.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Copy-Item (Join-Path $root 'README.txt') $dist -Force -ErrorAction SilentlyContinue
"Built $out"
