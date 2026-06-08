param(
    [Parameter(Mandatory = $true)]
    [string]$InputRom,

    [Parameter(Mandatory = $true)]
    [string]$OutputRom,

    [string]$PatchManifest = "",
    [string]$Feature = "PalmDay",
    [string]$Profile = "",
    [string]$RomFileName = "",
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

function Convert-HexStringToBytes {
    param([Parameter(Mandatory = $true)][string]$Hex)

    $clean = ($Hex -replace "0x", "" -replace "[^0-9A-Fa-f]", "")
    if (($clean.Length % 2) -ne 0) {
        throw "Invalid hex byte string length for '$Hex'."
    }

    $bytes = New-Object byte[] ($clean.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($clean.Substring($i * 2, 2), 16)
    }
    return $bytes
}

function Convert-PatchOffset {
    param([Parameter(Mandatory = $true)]$Value)

    if ($Value -is [int] -or $Value -is [long]) {
        return [int64]$Value
    }

    $text = [string]$Value
    if ($text.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToInt64($text.Substring(2), 16)
    }
    return [Convert]::ToInt64($text, 10)
}

if ([string]::IsNullOrWhiteSpace($PatchManifest)) {
    $PatchManifest = Join-Path $PSScriptRoot "PalmRomPatches.json"
}

$inputPath = [IO.Path]::GetFullPath($InputRom)
$outputPath = [IO.Path]::GetFullPath($OutputRom)
$manifestPath = [IO.Path]::GetFullPath($PatchManifest)

if (-not (Test-Path -LiteralPath $inputPath)) {
    throw "Input ROM not found: $inputPath"
}
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Patch manifest not found: $manifestPath"
}

$romBytes = [IO.File]::ReadAllBytes($inputPath)
$romSha = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$sets = @($manifest.patchSets) | Where-Object {
    $_.enabled -eq $true -and
    $_.feature -eq $Feature -and
    ([string]::IsNullOrWhiteSpace($Profile) -or [string]::IsNullOrWhiteSpace($_.profile) -or $_.profile -eq $Profile) -and
    ([string]::IsNullOrWhiteSpace($RomFileName) -or [string]::IsNullOrWhiteSpace($_.romFileName) -or $_.romFileName -eq $RomFileName) -and
    ([string]$_.sha256).ToLowerInvariant() -eq $romSha
}

if ($sets.Count -eq 0) {
    $message = "No enabled '$Feature' ROM patch set matched profile='$Profile' rom='$RomFileName' sha256=$romSha."
    if ($Strict) {
        throw $message
    }

    Copy-Item -LiteralPath $inputPath -Destination $outputPath -Force
    Write-Host "ROM patcher: $message"
    Write-Host "ROM patcher: copied ROM unchanged."
    exit 0
}

if ($sets.Count -gt 1) {
    throw "Multiple '$Feature' ROM patch sets matched sha256=$romSha; refusing ambiguous patch."
}

$set = $sets[0]
$patches = @($set.patches)
if ($patches.Count -eq 0) {
    throw "Patch set '$($set.name)' matched but has no patches."
}

foreach ($patch in $patches) {
    $offset = Convert-PatchOffset $patch.offset
    $expected = Convert-HexStringToBytes ([string]$patch.expected)
    $replace = Convert-HexStringToBytes ([string]$patch.replace)

    if ($expected.Length -ne $replace.Length) {
        throw "Patch '$($patch.name)' expected/replace length mismatch."
    }
    if ($offset -lt 0 -or ($offset + $expected.Length) -gt $romBytes.Length) {
        throw "Patch '$($patch.name)' exceeds ROM bounds at offset 0x$($offset.ToString('X'))."
    }

    for ($i = 0; $i -lt $expected.Length; $i++) {
        $actual = $romBytes[$offset + $i]
        if ($actual -ne $expected[$i]) {
            throw "Patch '$($patch.name)' expected byte $($expected[$i].ToString('X2')) at 0x$(($offset + $i).ToString('X')), got $($actual.ToString('X2'))."
        }
    }

    [Array]::Copy($replace, 0, $romBytes, $offset, $replace.Length)
    Write-Host "ROM patcher: applied '$($patch.name)' at 0x$($offset.ToString('X')) ($($replace.Length) bytes)."
}

$outDir = Split-Path -Parent $outputPath
if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}
[IO.File]::WriteAllBytes($outputPath, $romBytes)
$outSha = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "ROM patcher: patched '$($set.name)' input=$romSha output=$outSha"
