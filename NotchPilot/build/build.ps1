# NotchPilot build script.
# 1) src\NotchPilot.cs -> build\out\NotchPilot.exe (the app). Libraries in the repo root lib\ folder are embedded into the exe.
# 2) src\Setup.cs + embedded NotchPilot.exe + embedded PawnIO installer (lib\PawnIO_setup.exe) -> dist\NotchPilot-Setup.exe
# Requires: the Windows .NET Framework compiler (csc.exe).

$ErrorActionPreference = "Stop"
$root  = Split-Path -Parent $PSScriptRoot
$csc   = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src   = Join-Path $root "src"
$repo  = Split-Path -Parent $root
$lib   = Join-Path $repo "lib"
$icon  = Join-Path $root "assets\NotchPilot.ico"
$pawn  = Join-Path $lib "PawnIO_setup.exe"
$out   = Join-Path $root "build\out"
$dist  = Join-Path $root "dist"
$manifest = Join-Path $src "app.manifest"

if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
if (-not (Test-Path $pawn)) { throw "PawnIO installer not found: $pawn`nDownload PawnIO_setup.exe from https://github.com/namazso/PawnIO.Setup/releases and put it in the repo root lib folder." }
New-Item -ItemType Directory -Force -Path $out, $dist | Out-Null

# Embedded libraries: each lib\*.dll is both a compile reference and an exe resource
$libDlls = @(Get-ChildItem $lib -Filter *.dll)
$libRefs = @($libDlls | ForEach-Object { "/reference:" + $_.FullName })
$libRes  = @($libDlls | ForEach-Object { "/resource:" + $_.FullName + "," + $_.Name })

$app = Join-Path $out "NotchPilot.exe"
& $csc /nologo /target:winexe /codepage:65001 "/win32icon:$icon" "/win32manifest:$manifest" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll `
    $libRefs $libRes "/out:$app" (Join-Path $src "NotchPilot.cs") (Join-Path $src "Loc.cs")
if ($LASTEXITCODE -ne 0) { throw "NotchPilot failed to compile (code $LASTEXITCODE)" }

$setup = Join-Path $dist "NotchPilot-Setup.exe"
& $csc /nologo /target:winexe /codepage:65001 "/win32icon:$icon" "/win32manifest:$manifest" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    "/resource:$app,NotchPilotPayload" "/resource:$pawn,PawnIOSetup" "/out:$setup" (Join-Path $src "Setup.cs")
if ($LASTEXITCODE -ne 0) { throw "Installer failed to compile (code $LASTEXITCODE)" }

"NotchPilot.exe       : " + (Get-Item $app).Length + " bytes"
"NotchPilot-Setup.exe : " + (Get-Item $setup).Length + " bytes"
