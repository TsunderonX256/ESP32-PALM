param(
    [string]$ArduinoCli = "",
    [string]$Fqbn = "",
    [string]$BuildRoot = "",
    [string]$RomFileName = "",
    [string]$Port = "",
    [switch]$CodeCheckOnly,
    [switch]$LargeApp,
    [switch]$Upload
)

$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$configText = Get-Content -LiteralPath (Join-Path $repoRoot "palm_config.h") -Raw
if ([string]::IsNullOrWhiteSpace($RomFileName)) {
    if ($configText -match "PALM_HARDWARE_PROFILE\s+PALM_PROFILE_IIIC_EXPERIMENTAL") {
        $RomFileName = "Palm-IIIc-4.1-en.rom"
    } elseif ($configText -match "PALM_HARDWARE_PROFILE\s+PALM_PROFILE_M100_EXPERIMENTAL") {
        $RomFileName = "Palm-m100-3.51-en.rom"
    } else {
        $RomFileName = "Palm-IIIx-3.1.rom"
    }
}
$romFileName = $RomFileName
$romPath = Join-Path $repoRoot $romFileName
$partitionCsv = Join-Path $repoRoot "Tools\esp32_palm_16mb_partitions.csv"

if ([string]::IsNullOrWhiteSpace($ArduinoCli)) {
    $ArduinoCli = Join-Path $env:LOCALAPPDATA "Programs\Arduino IDE\resources\app\lib\backend\resources\arduino-cli.exe"
}

if (-not (Test-Path -LiteralPath $ArduinoCli)) {
    throw "arduino-cli was not found. Pass -ArduinoCli or install Arduino IDE."
}

if ([string]::IsNullOrWhiteSpace($Fqbn)) {
    $partitionScheme = "custom"
    $Fqbn = "esp32:esp32:esp32s3:FlashSize=16M,PartitionScheme=$partitionScheme,PSRAM=opi,CPUFreq=240,USBMode=hwcdc,UploadMode=default,CDCOnBoot=default"
}

$useCustomPartition = $Fqbn -match "PartitionScheme=custom"
if ($useCustomPartition -and -not (Test-Path -LiteralPath $partitionCsv)) {
    throw "Missing ESP32 partition file: $partitionCsv"
}

if (-not $CodeCheckOnly -and -not (Test-Path -LiteralPath $romPath)) {
    throw "Missing $romFileName in $repoRoot. ROM files are user-supplied and ignored by git."
}

if ([string]::IsNullOrWhiteSpace($BuildRoot)) {
    $BuildRoot = Join-Path $env:TEMP "esp32-palm-arduino-build"
}

$sketchRoot = Join-Path ([IO.Path]::GetFullPath($BuildRoot)) "ESP32-PALM"
if (Test-Path -LiteralPath $sketchRoot) {
    Remove-Item -LiteralPath $sketchRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $sketchRoot | Out-Null

$exclude = @(".git")
Get-ChildItem -LiteralPath $repoRoot -Force |
    Where-Object { $exclude -notcontains $_.Name } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $sketchRoot -Recurse -Force
    }

if ($useCustomPartition) {
    Copy-Item -LiteralPath $partitionCsv -Destination (Join-Path $sketchRoot "partitions.csv") -Force
}

$currentRomPath = Join-Path $sketchRoot "palm_current.rom"
if ($CodeCheckOnly) {
    [IO.File]::WriteAllBytes($currentRomPath, [byte[]](0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))
} else {
    Copy-Item -LiteralPath $romPath -Destination $currentRomPath -Force
}

Push-Location -LiteralPath $sketchRoot
try {
    $args = @("compile", "--fqbn", $Fqbn)
    if ($Upload) {
        if ([string]::IsNullOrWhiteSpace($Port)) {
            throw "Pass -Port when using -Upload."
        }
        $args += @("--upload", "--port", $Port)
    }
    $args += "."

    & $ArduinoCli @args
    if ($LASTEXITCODE -ne 0) {
        throw "arduino-cli compile failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
