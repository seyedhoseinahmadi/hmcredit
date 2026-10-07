$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$driverRoot = Join-Path $root 'artifacts\drivers\cp210x'
$downloadDir = Join-Path $env:RUNNER_TEMP 'himate-cp210x-download'
$archive = Join-Path $downloadDir 'CP210x_Universal_Windows_Driver.zip'
$extracted = Join-Path $downloadDir 'extracted'
$urls = @(
    'https://www.silabs.com/documents/public/software/CP210x_Universal_Windows_Driver.zip',
    'https://dl.espressif.com/dl/idf-installer/CP210x_Universal_Windows_Driver.zip'
)

Remove-Item -Recurse -Force $driverRoot -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $downloadDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $driverRoot, $downloadDir, $extracted | Out-Null

$downloaded = $false
foreach ($url in $urls) {
    try {
        Write-Host "Downloading CP210x VCP driver from: $url"
        Invoke-WebRequest -Uri $url -OutFile $archive -MaximumRedirection 5 -TimeoutSec 120
        $downloaded = $true
        $downloadSource = $url
        Write-Host "Driver source: $url"
        break
    }
    catch {
        Write-Warning "Download blocked/unavailable from $url : $($_.Exception.Message)"
        Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
    }
}

if (-not $downloaded) {
    throw "CP210x driver download failed from Silicon Labs and official Espressif driver mirror."
}

if (-not (Test-Path $archive) -or (Get-Item $archive).Length -lt 10000) {
    throw 'CP210x driver archive download is missing or unexpectedly small.'
}

$sha = (Get-FileHash $archive -Algorithm SHA256).Hash
Write-Host "CP210x driver archive SHA-256: $sha"

# Pin the Espressif-hosted version to a previously validated archive.
if ($downloadSource -like 'https://dl.espressif.com/*') {
    $expectedSha = '414345BDA1B0149F5DAA567ABDFA71E6D1A4405B7E0302BBC0DC46319FA154AB'
    if ($sha -ne $expectedSha) {
        throw "Espressif CP210x driver archive checksum changed unexpectedly. Review the new package before publishing."
    }
}

Expand-Archive -LiteralPath $archive -DestinationPath $extracted -Force

$infFiles = @(Get-ChildItem $extracted -Filter '*.inf' -Recurse -File)
$catFiles = @(Get-ChildItem $extracted -Filter '*.cat' -Recurse -File)
$sysFiles = @(Get-ChildItem $extracted -Filter '*.sys' -Recurse -File)

$licenseFiles = @(Get-ChildItem $extracted -Filter 'SLAB_License_Agreement_VCP_Windows.txt' -Recurse -File)

if ($infFiles.Count -eq 0 -or $catFiles.Count -eq 0 -or $sysFiles.Count -eq 0 -or $licenseFiles.Count -eq 0) {
    throw 'Official driver archive is missing INF, CAT, or SYS driver files.'
}

$hasCp2102Id = $false
foreach ($inf in $infFiles) {
    if ((Get-Content -LiteralPath $inf.FullName -Raw) -match '(?i)VID_10C4&PID_EA60') {
        $hasCp2102Id = $true
        break
    }
}

if (-not $hasCp2102Id) {
    throw 'Downloaded driver package does not advertise the standard CP2102 VID_10C4 / PID_EA60.'
}

$validCatalogs = @()
foreach ($cat in $catFiles) {
    $signature = Get-AuthenticodeSignature -LiteralPath $cat.FullName
    Write-Host "Catalog: $($cat.Name), Signature: $($signature.Status), Signer: $($signature.SignerCertificate.Subject)"
    if ($signature.Status -eq 'Valid') {
        $validCatalogs += $cat
    }
}

if ($validCatalogs.Count -eq 0) {
    throw 'Downloaded CP210x driver has no valid signed catalog. Refusing to package unsigned driver.'
}

Copy-Item -Path (Join-Path $extracted '*') -Destination $driverRoot -Recurse -Force

$packagedInf = @(Get-ChildItem $driverRoot -Filter '*.inf' -Recurse -File)
Write-Host "CP210x driver packaged: $($packagedInf.Count) INF file(s)."
Write-Host "Vendor license document included unmodified with the driver."
