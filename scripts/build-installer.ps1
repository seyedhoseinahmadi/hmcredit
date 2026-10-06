$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $root "installer\HiMate.Agent.iss"
$out = Join-Path $root "artifacts\installer"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Find-Iscc {
    $candidates = @()

    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }

    if ($env:ProgramFiles) {
        $candidates += (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    }

    $pf86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    if ($pf86) {
        $candidates += (Join-Path $pf86 "Inno Setup 6\ISCC.exe")
    }

    $candidates += "C:\ProgramData\chocolatey\bin\ISCC.exe"

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return $null
}

$iscc = Find-Iscc

if (-not $iscc) {
    Write-Host "Inno Setup compiler not found. Installing with Chocolatey..."
    choco install innosetup --no-progress -y
    if ($LASTEXITCODE -ne 0) {
        throw "Chocolatey failed to install Inno Setup. Exit code: $LASTEXITCODE"
    }
    $iscc = Find-Iscc
}

if (-not $iscc) {
    Write-Host "Diagnostic search for ISCC.exe:"
    Get-ChildItem "C:\Program Files*" -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 20 -ExpandProperty FullName |
        ForEach-Object { Write-Host $_ }

    throw "Inno Setup compiler (ISCC.exe) was not found."
}

Write-Host "Using Inno Setup compiler: $iscc"

$version = if ($env:HIMATE_VERSION) { $env:HIMATE_VERSION.TrimStart('v') } else { "0.2.0" }

& $iscc "/DMyAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

$setup = Join-Path $out "HiMate-Credit-Setup.exe"
if (-not (Test-Path $setup)) {
    throw "Installer was not created: $setup"
}

Write-Host "Installer created: $setup" -ForegroundColor Green
