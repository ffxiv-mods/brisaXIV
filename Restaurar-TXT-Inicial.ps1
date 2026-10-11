param([string]$Destination = (Join-Path $PSScriptRoot 'plugin\traducoes.txt'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $Destination) { Write-Output 'TXT existente preservado.'; return }
$taskMetadataPath = Join-Path $PSScriptRoot 'data\traducoes-iniciais.json'
if (-not (Test-Path -LiteralPath $taskMetadataPath)) { return }
$taskMetadata = Get-Content -LiteralPath $taskMetadataPath -Raw | ConvertFrom-Json
$taskDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Destination))
New-Item -ItemType Directory -Path $taskDirectory -Force | Out-Null
$taskArchivePath = Join-Path $taskDirectory ('seed-' + [Guid]::NewGuid().ToString('N') + '.tmp.zip')
$taskArchiveStream = [IO.File]::Create($taskArchivePath)
try {
    foreach ($taskPart in $taskMetadata.Parts) {
        if ($taskPart.File -notmatch '^traducoes-iniciais\.zip(?:\.\d{3})?$') { throw 'Nome de parte inválido.' }
        $taskPartPath = Join-Path $PSScriptRoot ('data\' + $taskPart.File)
        if ((Get-FileHash -LiteralPath $taskPartPath -Algorithm SHA256).Hash -ne $taskPart.Sha256) { throw 'Parte do TXT inicial corrompida.' }
        $taskInput = [IO.File]::OpenRead($taskPartPath)
        try { $taskInput.CopyTo($taskArchiveStream) } finally { $taskInput.Dispose() }
    }
}
finally { $taskArchiveStream.Dispose() }
try {
    if ((Get-FileHash -LiteralPath $taskArchivePath -Algorithm SHA256).Hash -ne $taskMetadata.ArchiveSha256) { throw 'Arquivo inicial corrompido.' }
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskArchivePath)
    try {
        $taskEntry = $taskArchive.GetEntry('traducoes.txt')
        if ($null -eq $taskEntry) { throw 'TXT não encontrado no pacote inicial.' }
        $taskInput = $taskEntry.Open(); $taskOutput = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try { $taskInput.CopyTo($taskOutput) } finally { $taskInput.Dispose(); $taskOutput.Dispose() }
    }
    finally { $taskArchive.Dispose() }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $taskMetadata.TextSha256) { throw 'Confira o TXT inicial: a soma de verificação não corresponde.' }
    Write-Output 'TXT inicial restaurado, sem traduzir novamente.'
}
finally { if (Test-Path -LiteralPath $taskArchivePath) { Remove-Item -LiteralPath $taskArchivePath } }
