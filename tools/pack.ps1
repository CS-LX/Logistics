<#
.SYNOPSIS
    Logistics addon mod packaging script.
.DESCRIPTION
    Packs build output into a .scmod archive (plain ZIP, no asset encryption).
.PARAMETER BuildOutputDir
    MSBuild output directory ($(TargetDir)).
.PARAMETER Configuration
    Debug / Release.
.PARAMETER ArtifactDir
    When set, writes the .scmod here (CI artifacts). Skips pack.config.json.
#>

param(
    [Parameter(Mandatory)]
    [string]$BuildOutputDir,

    [string]$Configuration = "Release",

    [string]$ModFileName = "Logistics",

    [string]$ArtifactDir = ""
)

$ErrorActionPreference = "Stop"

$BuildOutputDir = $BuildOutputDir.TrimEnd('\', '/') + '\'
$ScriptDir = $PSScriptRoot
$ConfigPath = Join-Path $ScriptDir "pack.config.json"
$sevenZipCandidates = @(
    (Join-Path $ScriptDir "7z\7z.exe"),
    (Join-Path (Join-Path $ScriptDir "..\..\SCIENEW\tools\7z") "7z.exe"),
    (Join-Path (Join-Path $ScriptDir "..\..\..\SCIENEW\tools\7z") "7z.exe")
)
$sevenZipExe = $sevenZipCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not (Test-Path $BuildOutputDir)) {
    Write-Error "[PackMod] ERROR: Build output directory does not exist: $BuildOutputDir"
    exit 1
}

$DestDir = $null
if (-not [string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $DestDir = $ArtifactDir
}
elseif (Test-Path $ConfigPath) {
    $Config = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $DestDir = $Config.ModsFolder
    if ($Config.ModFileName) {
        $ModFileName = $Config.ModFileName
    }
}
else {
    Write-Host ""
    Write-Host "[PackMod] INFO: pack.config.json not found and -ArtifactDir not set; skipping deployment." -ForegroundColor Yellow
    Write-Host "[PackMod]   Copy tools\pack.config.example.json to tools\pack.config.json for local deploy." -ForegroundColor Yellow
    Write-Host ""
    exit 0
}

if ([string]::IsNullOrWhiteSpace($DestDir)) {
    Write-Error "[PackMod] ERROR: destination directory is empty."
    exit 1
}

if (-not (Test-Path $DestDir)) {
    New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
}

$TempZip = Join-Path $env:TEMP "$ModFileName.zip"
$ScmodFile = Join-Path $env:TEMP "$ModFileName.scmod"
$DestFile = Join-Path $DestDir "$ModFileName.scmod"

Write-Host ""
Write-Host "[PackMod] ----------------------------------------" -ForegroundColor Cyan
Write-Host "[PackMod] Mod     : Logistics (工业时代2：物流)" -ForegroundColor Cyan
Write-Host "[PackMod] Config  : $Configuration" -ForegroundColor Cyan
Write-Host "[PackMod] Source  : $BuildOutputDir" -ForegroundColor Cyan
Write-Host "[PackMod] Target  : $DestFile" -ForegroundColor Cyan
Write-Host "[PackMod] ----------------------------------------" -ForegroundColor Cyan

if (Test-Path $TempZip) { Remove-Item $TempZip -Force }
if (Test-Path $ScmodFile) { Remove-Item $ScmodFile -Force }

Write-Host "[PackMod] Compressing (plaintext, no AMPK)..." -ForegroundColor Cyan
Push-Location $BuildOutputDir
try {
    if ($sevenZipExe) {
        & $sevenZipExe a -tzip -mx=1 -r "$TempZip" "*" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "[PackMod] ERROR: 7z compression failed (exit code: $LASTEXITCODE)."
            exit 1
        }
    }
    else {
        Compress-Archive -Path * -DestinationPath $TempZip -Force
    }
}
finally {
    Pop-Location
}

Move-Item $TempZip $ScmodFile -Force
Move-Item $ScmodFile $DestFile -Force

Write-Host "[PackMod] OK - Packaged: $DestFile" -ForegroundColor Green
Write-Host ""
