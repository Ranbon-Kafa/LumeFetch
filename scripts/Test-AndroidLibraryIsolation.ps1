param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][ValidateSet('arm64-v8a','x86_64')][string]$Abi
)
$ErrorActionPreference = 'Stop'
# Check every directory exposed on AndroidTools' LD_LIBRARY_PATH. Never ship SDK
# linker aliases under Android/system library names, even if their SONAME is private.
$forbidden = '^lib(?:crypto|ssl|sqlite3?)\.so(?:\.[0-9]+)*$'
function Assert-PrivateName([string]$name, [string]$location) {
    if ($name -match $forbidden) { throw "System-library shadowing alias: $location/$name. Package private *_python.so names only." }
}
function Test-RuntimeArchive([IO.Compression.ZipArchive]$archive, [string]$kind) {
    $topLevel = @($archive.Entries | Where-Object { $_.FullName -match '^usr/lib/[^/]+$' })
    foreach ($entry in $topLevel) {
        $name = $entry.Name
        Assert-PrivateName $name $kind
        if ($kind -eq 'python' -and $name -match '\.so(?:\.[0-9]+)*$' -and
            $name -notmatch '^(libpython3(?:\.[0-9]+)?|lib(?:crypto|ssl|sqlite3)_python)\.so$') {
            throw "Unexpected Python runtime library: $name. Review its runtime SONAME before packaging."
        }
    }
    if ($kind -eq 'python') {
        foreach ($name in @('libcrypto_python.so', 'libssl_python.so', 'libsqlite3_python.so')) {
            if ($name -notin $topLevel.Name) { throw "Missing private Python library: $name" }
        }
        if (@($topLevel | Where-Object { $_.Name -match '^libpython3\.[0-9]+\.so$' }).Count -ne 1) {
            throw 'Expected exactly one versioned libpython runtime.'
        }
    }
}
$item = Get-Item -LiteralPath $Path
if ($item.PSIsContainer) {
    $native = Join-Path $item.FullName "jni/$Abi"
    foreach ($file in Get-ChildItem -LiteralPath $native -File) { Assert-PrivateName $file.Name $native }
    foreach ($kind in @('python', 'ffmpeg')) {
        $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $item.FullName "assets/tools/$Abi/$kind.zip"))
        try { Test-RuntimeArchive $archive $kind } finally { $archive.Dispose() }
    }
} else {
    $apk = [IO.Compression.ZipFile]::OpenRead($item.FullName)
    try {
        foreach ($entry in $apk.Entries | Where-Object { $_.FullName.StartsWith("lib/$Abi/") }) {
            Assert-PrivateName $entry.Name $entry.FullName
        }
        foreach ($kind in @('python', 'ffmpeg')) {
            $entry = $apk.GetEntry("assets/tools/$Abi/$kind.zip")
            if (!$entry -or $entry.Length -gt 512MB) { throw "Missing or oversized runtime archive: $kind" }
            $buffer = [IO.MemoryStream]::new()
            $inputStream = $entry.Open()
            try { $inputStream.CopyTo($buffer) } finally { $inputStream.Dispose() }
            try {
                $buffer.Position = 0
                $archive = [IO.Compression.ZipArchive]::new($buffer, [IO.Compression.ZipArchiveMode]::Read, $true)
                try { Test-RuntimeArchive $archive $kind } finally { $archive.Dispose() }
            } finally { $buffer.Dispose() }
        }
    } finally { $apk.Dispose() }
}
Write-Host "PASS: $Abi private Python libraries present; no system-library shadowing aliases in runtime search directories."
