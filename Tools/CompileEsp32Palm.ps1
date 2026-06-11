param(
    [string]$ArduinoCli = "",
    [string]$Fqbn = "",
    [string]$BuildRoot = "",
    [string]$RomFileName = "",
    [ValidateSet("", "IIIX", "M100_EXPERIMENTAL", "IIIC_EXPERIMENTAL")]
    [string]$HardwareProfile = "",
    [ValidateSet("", "app0", "app1")]
    [string]$FlashAppSlot = "",
    [string]$Esptool = "",
    [string]$Port = "",
    [switch]$CodeCheckOnly,
    [switch]$LargeApp,
    [switch]$Upload
)

$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$configPath = Join-Path $repoRoot "palm_config.h"
$configText = Get-Content -LiteralPath $configPath -Raw

$profileInfo = @{
    "IIIX" = @{
        Macro = "PALM_PROFILE_IIIX"
        Rom = "Palm-IIIx-3.1.rom"
    }
    "M100_EXPERIMENTAL" = @{
        Macro = "PALM_PROFILE_M100_EXPERIMENTAL"
        Rom = "Palm-m100-3.51-en.rom"
    }
    "IIIC_EXPERIMENTAL" = @{
        Macro = "PALM_PROFILE_IIIC_EXPERIMENTAL"
        Rom = "Palm-IIIc-4.1-en.rom"
    }
}

$selectedHardwareProfile = "IIIX"
if (-not [string]::IsNullOrWhiteSpace($HardwareProfile)) {
    $selectedHardwareProfile = $HardwareProfile
    $profileMacro = $profileInfo[$selectedHardwareProfile].Macro
    $configText = [regex]::Replace(
        $configText,
        "(?m)^(\s*#define\s+PALM_HARDWARE_PROFILE\s+)PALM_PROFILE_\w+",
        ('$1' + $profileMacro),
        1
    )
    if ([string]::IsNullOrWhiteSpace($RomFileName)) {
        $RomFileName = $profileInfo[$selectedHardwareProfile].Rom
    }
} elseif ([string]::IsNullOrWhiteSpace($RomFileName)) {
    if ($configText -match "PALM_HARDWARE_PROFILE\s+PALM_PROFILE_IIIC_EXPERIMENTAL") {
        $selectedHardwareProfile = "IIIC_EXPERIMENTAL"
        $RomFileName = "Palm-IIIc-4.1-en.rom"
    } elseif ($configText -match "PALM_HARDWARE_PROFILE\s+PALM_PROFILE_M100_EXPERIMENTAL") {
        $selectedHardwareProfile = "M100_EXPERIMENTAL"
        $RomFileName = "Palm-m100-3.51-en.rom"
    } else {
        $RomFileName = "Palm-IIIx-3.1.rom"
    }
} elseif ($configText -match "PALM_HARDWARE_PROFILE\s+PALM_PROFILE_IIIC_EXPERIMENTAL") {
    $selectedHardwareProfile = "IIIC_EXPERIMENTAL"
} elseif ($configText -match "PALM_HARDWARE_PROFILE\s+PALM_PROFILE_M100_EXPERIMENTAL") {
    $selectedHardwareProfile = "M100_EXPERIMENTAL"
}
$romFileName = $RomFileName
$romPath = Join-Path $repoRoot $romFileName
$partitionCsv = Join-Path $repoRoot "Tools\esp32_palm_16mb_partitions.csv"
$romPatcher = Join-Path $repoRoot "Tools\PatchPalmRom.ps1"

$palmDayPatchEnabled = $configText -match "#define\s+PALM_PALMDAY_PATCH_ENABLED\s+1\b"
$palmDayPatchRequired = $configText -match "#define\s+PALM_PALMDAY_ROM_PATCH_REQUIRED\s+1\b"

if ([string]::IsNullOrWhiteSpace($ArduinoCli)) {
    $ArduinoCli = Join-Path $env:LOCALAPPDATA "Programs\Arduino IDE\resources\app\lib\backend\resources\arduino-cli.exe"
}

if (-not (Test-Path -LiteralPath $ArduinoCli)) {
    throw "arduino-cli was not found. Pass -ArduinoCli or install Arduino IDE."
}

if (-not [string]::IsNullOrWhiteSpace($FlashAppSlot)) {
    if ([string]::IsNullOrWhiteSpace($Port)) {
        throw "Pass -Port when using -FlashAppSlot."
    }
    if ($Upload) {
        throw "Use either -Upload for normal Arduino upload or -FlashAppSlot for app-only OTA-slot flashing, not both."
    }
    if ([string]::IsNullOrWhiteSpace($Esptool)) {
        $Esptool = Join-Path $env:LOCALAPPDATA "Arduino15\packages\esp32\tools\esptool_py\5.2.0\esptool.exe"
    }
    if (-not (Test-Path -LiteralPath $Esptool)) {
        throw "esptool was not found. Pass -Esptool or install the ESP32 Arduino package."
    }
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
$arduinoBuildPath = Join-Path ([IO.Path]::GetFullPath($BuildRoot)) "arduino-build"
if (Test-Path -LiteralPath $arduinoBuildPath) {
    Remove-Item -LiteralPath $arduinoBuildPath -Recurse -Force
}

New-Item -ItemType Directory -Path $sketchRoot | Out-Null

$exclude = @(".git")
Get-ChildItem -LiteralPath $repoRoot -Force |
    Where-Object { $exclude -notcontains $_.Name } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $sketchRoot -Recurse -Force
    }

Set-Content -LiteralPath (Join-Path $sketchRoot "palm_config.h") -Value $configText -NoNewline

if ($useCustomPartition) {
    Copy-Item -LiteralPath $partitionCsv -Destination (Join-Path $sketchRoot "partitions.csv") -Force
}

$currentRomPath = Join-Path $sketchRoot "palm_current.rom"
if ($CodeCheckOnly) {
    [IO.File]::WriteAllBytes($currentRomPath, [byte[]](0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))
} else {
    if ($palmDayPatchEnabled) {
        $patchArgs = @(
            "-ExecutionPolicy", "Bypass",
            "-File", $romPatcher,
            "-InputRom", $romPath,
            "-OutputRom", $currentRomPath,
            "-Feature", "PalmDay",
            "-Profile", $selectedHardwareProfile,
            "-RomFileName", $romFileName
        )
        if ($palmDayPatchRequired) {
            $patchArgs += "-Strict"
        }
        & powershell @patchArgs
        if ($LASTEXITCODE -ne 0) {
            throw "PalmDay ROM patcher failed with exit code $LASTEXITCODE."
        }
    } else {
        Copy-Item -LiteralPath $romPath -Destination $currentRomPath -Force
    }
}

$romAsmPath = Join-Path $sketchRoot "palm_rom.S"
if (Test-Path -LiteralPath $romAsmPath) {
    # palm_rom.S uses .incbin, and Arduino's dependency cache does not know that
    # palm_current.rom is an input. Put the ROM SHA in a separate harmless section
    # so the assembly source content changes whenever the selected or patched ROM
    # changes, forcing the embedded ROM object to be rebuilt.
    $romSha = (Get-FileHash -LiteralPath $currentRomPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Add-Content -LiteralPath $romAsmPath -Value ""
    Add-Content -LiteralPath $romAsmPath -Value '    .section .rodata.palm_rom_build_id, "a"'
    Add-Content -LiteralPath $romAsmPath -Value '    .global palm_rom_build_id'
    Add-Content -LiteralPath $romAsmPath -Value 'palm_rom_build_id:'
    Add-Content -LiteralPath $romAsmPath -Value ('    .ascii "' + $romSha + '"')
}

Push-Location -LiteralPath $sketchRoot
try {
    $args = @("compile", "--fqbn", $Fqbn, "--build-path", $arduinoBuildPath)
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

if (-not [string]::IsNullOrWhiteSpace($FlashAppSlot)) {
    $appBin = Get-ChildItem -LiteralPath $arduinoBuildPath -Recurse -Filter "*.ino.bin" |
        Where-Object { $_.Name -notmatch "\.(bootloader|partitions)\.bin$" } |
        Select-Object -First 1
    if ($null -eq $appBin) {
        throw "Could not find compiled app .bin in $arduinoBuildPath"
    }

    $slotOffset = if ($FlashAppSlot -eq "app1") { "0x800000" } else { "0x10000" }
    $esptoolArgs = @(
        "--chip", "esp32s3",
        "--port", $Port,
        "--baud", "921600",
        "--before", "default-reset",
        "--after", "hard-reset",
        "write-flash",
        "--flash-size", "16MB",
        $slotOffset,
        $appBin.FullName
    )
    Write-Host "Flashing $selectedHardwareProfile app image to $FlashAppSlot at $slotOffset"
    & $Esptool @esptoolArgs
    if ($LASTEXITCODE -ne 0) {
        throw "esptool app-slot flash failed with exit code $LASTEXITCODE."
    }
}
