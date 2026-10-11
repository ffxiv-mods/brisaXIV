# Gera os dois anexos de uma release instalável pelo Dalamud:
#   latest.zip  -> DLL, manifesto e TXT de traduções inicial
#   repo.json   -> entrada do repositório personalizado apontando para o latest.zip desta versão
# Usado pelo workflow .github/workflows/release.yml (somente na master); também roda localmente após Compilar.ps1.
param(
    [string]$Repository = 'ffxiv-mods/brisaXIV',
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
$taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($taskDllPath).Version.ToString()
if ($taskAssemblyVersion -ne $taskManifest.AssemblyVersion) {
    throw "A DLL ($taskAssemblyVersion) e o manifesto BrisaXIV.json ($($taskManifest.AssemblyVersion)) têm versões diferentes."
}
$taskTag = 'v' + ([Version]$taskAssemblyVersion).ToString(3)

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
    foreach ($taskName in @('BrisaXIV.dll', 'BrisaXIV.json', 'BrisaXIV.deps.json')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $BinaryDirectory $taskName), $taskName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, $taskTranslations.FullName, 'traducoes.txt', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
finally { $taskArchive.Dispose() }

$taskLink = "https://github.com/$Repository/releases/download/$taskTag/latest.zip"
$taskEntry = [ordered]@{}
foreach ($taskProperty in $taskManifest.PSObject.Properties) { $taskEntry[$taskProperty.Name] = $taskProperty.Value }
$taskEntry.AssemblyVersion = $taskAssemblyVersion
$taskEntry.RepoUrl = "https://github.com/$Repository"
$taskEntry.IsHide = $false
$taskEntry.IsTestingExclusive = $false
$taskEntry.DownloadLinkInstall = $taskLink
$taskEntry.DownloadLinkUpdate = $taskLink
$taskEntry.DownloadLinkTesting = $taskLink
$taskEntry.LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$taskUtf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'repo.json'), (ConvertTo-Json -InputObject @($taskEntry) -Depth 10), $taskUtf8)

Write-Output "Versão: $taskTag"
Write-Output "Pacote: $taskZipPath"
