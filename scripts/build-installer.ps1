$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $root "installer\HiMate.Agent.iss"
$out = Join-Path $root "artifacts\installer"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$isccCandidates = @(
  "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  Write-Host "Inno Setup not found. Installing with Chocolatey..."
  choco install innosetup --no-progress -y
  $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $iscc) {
  throw "Inno Setup compiler (ISCC.exe) was not found after installation."
}

$version = if ($env:HIMATE_VERSION) { $env:HIMATE_VERSION.TrimStart('v') } else { "0.1.0" }

& $iscc "/DMyAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) {
  throw "Inno Setup failed with exit code $LASTEXITCODE"
}

$setup = Join-Path $out "HiMate-Agent-Setup.exe"
if (-not (Test-Path $setup)) {
  throw "Installer was not created: $setup"
}

Write-Host "Installer created: $setup" -ForegroundColor Green
