<#
.SYNOPSIS
    Builds the GitMcp setup exe.

.DESCRIPTION
    1. Runs the tests (skip with -SkipTests).
    2. Publishes GitMcp.Server self-contained single-file for win-x64 (target PCs need no .NET).
    3. Compiles installer\GitMcp.iss with Inno Setup -> installer\Output\GitMcpSetup-<version>.exe

    Needs on THIS PC: .NET 10 SDK, git, Inno Setup 6.

.PARAMETER Version
    Installer version string. Default: <Version> from the server .csproj.

.EXAMPLE
    .\Build-Installer.ps1
.EXAMPLE
    .\Build-Installer.ps1 -SkipTests -Version 0.1.1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Write-Step([string]$Text) { Write-Host "`n=== $Text ===" -ForegroundColor Cyan }

function Invoke-Native {
    param([string]$File, [string[]]$Arguments, [string]$What)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
}

function Find-Iscc {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $keys = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    foreach ($k in $keys) {
        $loc = (Get-ItemProperty $k -ErrorAction SilentlyContinue).InstallLocation
        if ($loc -and (Test-Path (Join-Path $loc 'ISCC.exe'))) { return (Join-Path $loc 'ISCC.exe') }
    }
    return $null
}

# ---------------------------------------------------------------- prerequisites
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK not found (dotnet). Install the .NET 10 SDK.' }
$iscc = Find-Iscc
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup' }
if (-not $Version) {
    $csproj = Join-Path $root 'src\GitMcp.Server\GitMcp.Server.csproj'
    $Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "No <Version> in $csproj" }
}
$sw = [Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------- 1. tests
if (-not $SkipTests) {
    Write-Step '[1/3] Running tests'
    Invoke-Native -File 'dotnet' -What 'dotnet test' -Arguments @('test', (Join-Path $root 'GitMcp.slnx'), '-c', 'Release', '--nologo')
}

# ---------------------------------------------------------------- 2. publish
Write-Step '[2/3] Publishing GitMcp.Server (win-x64, self-contained, single file)'
$out = Join-Path $root 'publish\server'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
Invoke-Native -File 'dotnet' -What 'dotnet publish' -Arguments @(
    'publish', (Join-Path $root 'src\GitMcp.Server'), '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None', "-p:Version=$Version", '-o', $out, '--nologo')
foreach ($f in 'GitMcp.Server.exe', 'appsettings.json') {
    if (-not (Test-Path (Join-Path $out $f))) { throw "Publish finished but $f is missing." }
}

# ---------------------------------------------------------------- 3. installer
Write-Step '[3/3] Building installer'
Invoke-Native -File $iscc -What 'Inno Setup' -Arguments @('/Qp', "/DAppVersion=$Version", (Join-Path $root 'installer\GitMcp.iss'))

$setup = Get-ChildItem (Join-Path $root 'installer\Output') -Filter "GitMcpSetup-$Version.exe" | Select-Object -First 1
if (-not $setup) { throw 'Inno Setup reported success but no setup exe was found.' }

Write-Host ''
Write-Host ("Done in {0:mm\:ss}" -f $sw.Elapsed) -ForegroundColor Green
Write-Host ("  Setup: {0}  ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
Write-Host  '  Run it, then restart Claude Desktop.'
