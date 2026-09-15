param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][ValidateSet('arm64-v8a','x86_64')][string]$Abi
)
$ErrorActionPreference = 'Stop'
$machine = if ($Abi -eq 'arm64-v8a') { 183 } else { 62 }
$script:checkedElf = 0
function Test-ElfStream([IO.Stream]$stream, [string]$name, [bool]$required = $false) {
    if ($stream.Length -lt 4) { if ($required) { throw "Missing ELF header: $name" }; return }
    $reader = [IO.BinaryReader]::new($stream, [Text.Encoding]::UTF8, $true)
    try {
        if ($reader.ReadUInt32() -ne 0x464c457f) { if ($required) { throw "Expected native ELF file: $name" }; return }
        if ($stream.Length -lt 64 -or $reader.ReadByte() -ne 2 -or $reader.ReadByte() -ne 1) { throw "Expected ELF64 little-endian: $name" }
        $stream.Position = 18
        if ($reader.ReadUInt16() -ne $machine) { throw "Wrong ELF architecture: $name" }
        $stream.Position = 32
        $programOffset = $reader.ReadUInt64()
        $stream.Position = 54
        $entrySize = $reader.ReadUInt16()
        $count = $reader.ReadUInt16()
        if ($entrySize -lt 56 -or $count -eq 0 -or $count -gt 1024 -or $programOffset -gt $stream.Length -or
            $programOffset + [long]$entrySize * $count -gt $stream.Length) { throw "Invalid ELF program headers: $name" }
        $loads = 0
        for ($i=0; $i -lt $count; $i++) {
            $stream.Position = $programOffset + [long]$entrySize * $i
            if ($reader.ReadUInt32() -ne 1) { continue } # PT_LOAD
            [void]$reader.ReadUInt32() # flags
            $offset = $reader.ReadUInt64()
            $address = $reader.ReadUInt64()
            [void]$reader.ReadUInt64() # physical address
            $fileSize = $reader.ReadUInt64()
            $memorySize = $reader.ReadUInt64()
            $alignment = $reader.ReadUInt64()
            if ($alignment -lt 16384 -or ($alignment -band ($alignment - 1)) -ne 0 -or
                ($offset % 16384) -ne ($address % 16384)) { throw "ELF is not 16-KB aligned: $name (alignment=$alignment)" }
            if ($offset -gt $stream.Length -or $fileSize -gt $stream.Length - $offset -or $fileSize -gt $memorySize) { throw "Invalid ELF segment bounds: $name" }
            $loads++
        }
        if ($loads -eq 0) { throw "ELF has no load segments: $name" }
        $script:checkedElf++
    } finally { $reader.Dispose() }
}
function Test-Zip([IO.Compression.ZipArchive]$archive, [string]$label, [int]$depth) {
    if ($depth -gt 1 -or $archive.Entries.Count -gt 30000) { throw "Unexpected archive structure: $label" }
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $isNested = $depth -eq 0 -and $entry.FullName -match '^assets/tools/[^/]+/(python|ffmpeg)\.zip$'
        if (!$isNested -and $entry.FullName -notmatch '\.so(?:\.[0-9.]+)?$') { continue }
        if ($entry.Length -gt 512MB) { throw "Unexpected ELF/archive size: $($entry.FullName)" }
        $buffer = [IO.MemoryStream]::new()
        $source = $entry.Open()
        try { $source.CopyTo($buffer) } finally { $source.Dispose() }
        try {
            $buffer.Position = 0
            if ($isNested) {
                $nested = [IO.Compression.ZipArchive]::new($buffer, [IO.Compression.ZipArchiveMode]::Read, $true)
                try { Test-Zip $nested "$label!$($entry.FullName)" ($depth+1) } finally { $nested.Dispose() }
            } else { Test-ElfStream $buffer "$label!$($entry.FullName)" $true }
        } finally { $buffer.Dispose() }
    }
}
$item = Get-Item -LiteralPath $Path
if ($item.PSIsContainer) {
    foreach ($file in Get-ChildItem -LiteralPath $item.FullName -Recurse -File) {
        if ($file.Extension -eq '.zip') {
            $zip = [IO.Compression.ZipFile]::OpenRead($file.FullName)
            try { Test-Zip $zip $file.FullName 1 } finally { $zip.Dispose() }
        } else {
            $stream = [IO.File]::OpenRead($file.FullName)
            try { Test-ElfStream $stream $file.FullName ($file.Name -match '(\.so(?:\.[0-9.]+)?$|^(ffmpeg|ffprobe|qjs|python-launcher)$)') } finally { $stream.Dispose() }
        }
    }
} else {
    $zip = [IO.Compression.ZipFile]::OpenRead($item.FullName)
    try { Test-Zip $zip $item.FullName 0 } finally { $zip.Dispose() }
}
if ($script:checkedElf -eq 0) { throw 'No ELF files checked.' }
Write-Host "PASS: $script:checkedElf ELF files, $Abi, every load segment 16-KB aligned. Device execution still required."
