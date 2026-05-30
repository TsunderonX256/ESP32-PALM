param(
    [Parameter(Mandatory=$true)]
    [string]$Path,

    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"

$inputPath = (Resolve-Path -LiteralPath $Path).Path
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Split-Path -Parent $inputPath) ("prc_" + [IO.Path]::GetFileNameWithoutExtension($inputPath) + "_analysis")
}
$outRoot = [IO.Path]::GetFullPath($OutDir)
$resDir = Join-Path $outRoot "resources"
New-Item -ItemType Directory -Force -Path $resDir | Out-Null

$bytes = [IO.File]::ReadAllBytes($inputPath)

function Read-U16BE([byte[]]$b, [int]$o) {
    return (([int]$b[$o] -shl 8) -bor [int]$b[$o + 1])
}

function Read-U32BE([byte[]]$b, [int]$o) {
    return ([uint32]([uint32]$b[$o] -shl 24) -bor [uint32]([uint32]$b[$o + 1] -shl 16) -bor [uint32]([uint32]$b[$o + 2] -shl 8) -bor [uint32]$b[$o + 3])
}

function Read-Ascii([byte[]]$b, [int]$o, [int]$n) {
    return [Text.Encoding]::ASCII.GetString($b, $o, $n)
}

function Safe-Name([string]$name) {
    return ($name -replace '[\\/:*?"<>|]', '_')
}

if ($bytes.Length -lt 78) {
    throw "File is too small to be a Palm database."
}

$nameEnd = [Array]::IndexOf($bytes, [byte]0, 0, [Math]::Min(32, $bytes.Length))
if ($nameEnd -lt 0) { $nameEnd = 32 }
$dbName = [Text.Encoding]::ASCII.GetString($bytes, 0, $nameEnd)
$attributes = Read-U16BE $bytes 32
$type = Read-Ascii $bytes 60 4
$creator = Read-Ascii $bytes 64 4
$entryCount = Read-U16BE $bytes 76
$isResourceDb = ($attributes -band 1) -ne 0
if (-not $isResourceDb) {
    throw "Only Palm resource PRCs are supported by this analyzer."
}

$summary = New-Object System.Text.StringBuilder
[void]$summary.AppendLine("PRC: $inputPath")
[void]$summary.AppendLine("Name: $dbName")
[void]$summary.AppendLine(("Type/Creator: {0}/{1}" -f $type, $creator))
[void]$summary.AppendLine(("Attributes: ${0:X4}" -f $attributes))
[void]$summary.AppendLine("Resources: $entryCount")
[void]$summary.AppendLine()
[void]$summary.AppendLine("Type  ID       Offset   Size     File")
[void]$summary.AppendLine("----  -------  -------  -------  ----")

$entries = @()
for ($i = 0; $i -lt $entryCount; $i++) {
    $entryOff = 78 + $i * 10
    if ($entryOff + 10 -gt $bytes.Length) { throw "Resource table is truncated." }
    $resType = Read-Ascii $bytes $entryOff 4
    $resId = Read-U16BE $bytes ($entryOff + 4)
    $resOff = [int](Read-U32BE $bytes ($entryOff + 6))
    $entries += [pscustomobject]@{
        Type = $resType
        Id = $resId
        Offset = $resOff
        Index = $i
    }
}

for ($i = 0; $i -lt $entries.Count; $i++) {
    $entry = $entries[$i]
    $nextOff = $bytes.Length
    foreach ($candidate in $entries) {
        if ($candidate.Offset -gt $entry.Offset -and $candidate.Offset -lt $nextOff) {
            $nextOff = $candidate.Offset
        }
    }
    if ($entry.Offset -lt 78 -or $entry.Offset -gt $bytes.Length -or $nextOff -lt $entry.Offset -or $nextOff -gt $bytes.Length) {
        throw "Invalid resource offset for $($entry.Type) #$($entry.Id)."
    }
    $size = $nextOff - $entry.Offset
    $fileName = "{0}_{1:D5}_{2:D3}.bin" -f (Safe-Name $entry.Type), $entry.Id, $entry.Index
    $filePath = Join-Path $resDir $fileName
    $data = New-Object byte[] $size
    [Array]::Copy($bytes, $entry.Offset, $data, 0, $size)
    [IO.File]::WriteAllBytes($filePath, $data)
    $entry | Add-Member -NotePropertyName Size -NotePropertyValue $size
    $entry | Add-Member -NotePropertyName File -NotePropertyValue $filePath
    [void]$summary.AppendLine(("{0,-4}  {1,7}  {2,7:X6}  {3,7}  {4}" -f $entry.Type, $entry.Id, $entry.Offset, $size, $fileName))
}

$scanLines = New-Object System.Text.StringBuilder
[void]$scanLines.AppendLine("Pattern scan")
[void]$scanLines.AppendLine("============")
$patterns = @(
    @{ Name = "Keyboard ID FA FD"; Bytes = [byte[]](0xFA, 0xFD) },
    @{ Name = "Keyboard ID FD FA"; Bytes = [byte[]](0xFD, 0xFA) },
    @{ Name = "DragonBall UART base FFFFF900"; Bytes = [byte[]](0xFF, 0xFF, 0xF9, 0x00) },
    @{ Name = "UART RX reg FFFFF904"; Bytes = [byte[]](0xFF, 0xFF, 0xF9, 0x04) },
    @{ Name = "UART data reg FFFFF905"; Bytes = [byte[]](0xFF, 0xFF, 0xF9, 0x05) },
    @{ Name = "UART misc reg FFFFF908"; Bytes = [byte[]](0xFF, 0xFF, 0xF9, 0x08) },
    @{ Name = "IRQ control FFFFF302"; Bytes = [byte[]](0xFF, 0xFF, 0xF3, 0x02) },
    @{ Name = "IRQ pending FFFFF30C"; Bytes = [byte[]](0xFF, 0xFF, 0xF3, 0x0C) },
    @{ Name = "IRQ mask FFFFF304"; Bytes = [byte[]](0xFF, 0xFF, 0xF3, 0x04) }
)

foreach ($pattern in $patterns) {
    [void]$scanLines.AppendLine()
    [void]$scanLines.AppendLine($pattern.Name)
    $pat = [byte[]]$pattern.Bytes
    $hits = 0
    foreach ($entry in $entries) {
        $data = [IO.File]::ReadAllBytes($entry.File)
        for ($i = 0; $i -le $data.Length - $pat.Length; $i++) {
            $ok = $true
            for ($j = 0; $j -lt $pat.Length; $j++) {
                if ($data[$i + $j] -ne $pat[$j]) { $ok = $false; break }
            }
            if ($ok) {
                [void]$scanLines.AppendLine(("  {0} #{1} +0x{2:X4}" -f $entry.Type, $entry.Id, $i))
                $hits++
                if ($hits -ge 50) { break }
            }
        }
        if ($hits -ge 50) { break }
    }
    if ($hits -eq 0) { [void]$scanLines.AppendLine("  no hits") }
}

$summaryPath = Join-Path $outRoot "resources.txt"
$scanPath = Join-Path $outRoot "scan.txt"
[IO.File]::WriteAllText($summaryPath, $summary.ToString(), [Text.Encoding]::UTF8)
[IO.File]::WriteAllText($scanPath, $scanLines.ToString(), [Text.Encoding]::UTF8)

Write-Host "Wrote $summaryPath"
Write-Host "Wrote $scanPath"
