# Gera os dois anexos de uma release instalável pelo Dalamud:
#   latest.zip  -> DLL, manifesto e TXT de traduções inicial
#   repo.json   -> entrada do repositório personalizado apontando para o latest.zip desta versão
# A versão é a da DLL compilada (no CI vem da tag ReleaseV<versão> via Compilar.ps1 -Version).
# Usado pelo workflow .github/workflows/release.yml; também roda localmente após Compilar.ps1.
param(
    [string]$Repository = 'ffxiv-mods/brisaXIV',
    [string]$TagPrefix = 'ReleaseV',
    [string]$BinaryDirectory = (Join-Path $PSScriptRoot 'plugin'),
    [string]$TranslationsFile = (Join-Path $PSScriptRoot 'plugin\traducoes.txt'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'release')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Informe proprietário/repositório.' }

$taskManifestPath = Join-Path $BinaryDirectory 'BrisaXIV.json'
$taskDllPath = Join-Path $BinaryDirectory 'BrisaXIV.dll'
foreach ($taskPath in @($taskManifestPath, $taskDllPath, (Join-Path $BinaryDirectory 'BrisaXIV.deps.json'))) {
    if (-not (Test-Path -LiteralPath $taskPath)) { throw "Arquivo de compilação ausente: $taskPath. Rode Compilar.ps1 antes." }
}
$taskAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($taskDllPath).Version.ToString()
$taskTag = $TagPrefix + ([Version]$taskAssemblyVersion).ToString(3)
# O manifesto empacotado sempre recebe a versão da DLL, para o Dalamud detectar atualizações corretamente.
$taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskManifest.AssemblyVersion = $taskAssemblyVersion
$taskUtf8 = [Text.UTF8Encoding]::new($false)

# Sem o TXT real o pacote instalaria o plugin sem nenhuma tradução. Um checkout sem LFS deixa só o ponteiro (~130 bytes).
if (-not (Test-Path -LiteralPath $TranslationsFile)) { throw "TXT de traduções não encontrado: $TranslationsFile" }
$taskTranslations = Get-Item -LiteralPath $TranslationsFile
$taskHead = [IO.File]::ReadLines($taskTranslations.FullName) | Select-Object -First 1
if ($taskTranslations.Length -lt 1MB -or $taskHead -like 'version https://git-lfs*') {
    throw 'traducoes.txt é um ponteiro do Git LFS ou está incompleto. Rode "git lfs pull".'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskZipPath = Join-Path $OutputDirectory 'latest.zip'
if (Test-Path -LiteralPath $taskZipPath) { Remove-Item -LiteralPath $taskZipPath }
$taskArchive = [IO.Compression.ZipFile]::Open($taskZipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskName in @('BrisaXIV.dll', 'BrisaXIV.deps.json')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $BinaryDirectory $taskName), $taskName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    $taskWriter = [IO.StreamWriter]::new($taskArchive.CreateEntry('BrisaXIV.json').Open(), $taskUtf8)
    try { $taskWriter.Write(($taskManifest | ConvertTo-Json -Depth 10)) } finally { $taskWriter.Dispose() }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, $taskTranslations.FullName, 'traducoes.txt', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
finally { $taskArchive.Dispose() }

$taskLink = "https://github.com/$Repository/releases/download/$taskTag/latest.zip"
$taskEntry = [ordered]@{}
foreach ($taskProperty in $taskManifest.PSObject.Properties) { $taskEntry[$taskProperty.Name] = $taskProperty.Value }
$taskEntry.RepoUrl = "https://github.com/$Repository"
$taskEntry.IsHide = $false
$taskEntry.IsTestingExclusive = $false
$taskEntry.DownloadLinkInstall = $taskLink
$taskEntry.DownloadLinkUpdate = $taskLink
$taskEntry.DownloadLinkTesting = $taskLink
$taskEntry.LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'repo.json'), (ConvertTo-Json -InputObject @($taskEntry) -Depth 10), $taskUtf8)

Write-Output "Tag: $taskTag"
Write-Output "Pacote: $taskZipPath"
