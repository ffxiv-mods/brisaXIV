param(
    [string]$Repository = 'BrisasXIV/BrisasXIV',
    [string]$BinaryDirectory = (Join-Path $PSScriptRoot 'plugin'),
    [string]$TranslationsFile = (Join-Path $PSScriptRoot 'plugin\traducoes.txt'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'publicacao')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Informe proprietário/repositório.' }
$taskManifest = Get-Content -LiteralPath (Join-Path $BinaryDirectory 'BrisaXIV.json') -Raw | ConvertFrom-Json
$taskAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $BinaryDirectory 'BrisaXIV.dll')).Version.ToString()
if ($taskAssemblyVersion -ne $taskManifest.AssemblyVersion) { throw 'A versão da DLL não corresponde ao manifesto. Compile antes de publicar.' }
$taskVersion = ([Version]$taskAssemblyVersion).ToString(3); $taskTag = 'v' + $taskVersion
$taskRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ($taskOutput -eq $taskRoot -or $taskRoot.StartsWith($taskOutput.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A saída deve ser uma subpasta separada, nunca a raiz do projeto ou uma pasta ancestral.' }
$taskExport = Join-Path $taskOutput ($taskVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$taskRepo = Join-Path $taskExport 'repositorio'; $taskRelease = Join-Path $taskExport 'release'
New-Item -ItemType Directory -Path $taskRepo,$taskRelease,(Join-Path $taskRepo 'plugin'),(Join-Path $taskRepo 'data') -Force | Out-Null
# Explicit allowlist: never export a config directory, .git, credentials, usage,
# retry journals, logs, local indices, backups or compiler temporary files.
foreach ($taskName in @('README.md','LEIA-ME.md','VALIDACAO.md','PUBLICAR.md','DESENVOLVIMENTO.md','.gitignore','NuGet.Config','Compilar.ps1','Instalar-No-Dalamud.ps1','Gerar-Somente-Leitura.ps1','Restaurar-TXT-Inicial.ps1','Preparar-Publicacao.ps1')) {
    $taskSource = Join-Path $taskRoot $taskName
    if (Test-Path -LiteralPath $taskSource) { Copy-Item -LiteralPath $taskSource -Destination (Join-Path $taskRepo $taskName) }
}
foreach ($taskFolder in @('src','tests','docs')) {
    $taskSourceRoot = Join-Path $taskRoot $taskFolder
    if (-not (Test-Path -LiteralPath $taskSourceRoot)) { continue }
    foreach ($taskSource in Get-ChildItem -LiteralPath $taskSourceRoot -File -Recurse) {
        $taskRelative = $taskSource.FullName.Substring($taskRoot.Length + 1)
        if ($taskRelative -match '(^|[\\/])(obj|bin|\.vs)([\\/]|$)' -or $taskSource.Extension -notin @('.cs','.csproj','.json','.md','.png')) { continue }
        $taskTarget = Join-Path $taskRepo $taskRelative
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($taskTarget)) -Force | Out-Null
        Copy-Item -LiteralPath $taskSource.FullName -Destination $taskTarget
    }
}
foreach ($taskName in @('BrisaXIV.dll','BrisaXIV.json','BrisaXIV.deps.json')) {
    Copy-Item -LiteralPath (Join-Path $BinaryDirectory $taskName) -Destination (Join-Path $taskRepo ('plugin\' + $taskName))
}
# Copy only the initial byte range, sharing read/write with the running plugin.
# A partially appended last line is omitted from this publication snapshot.
$taskLive = [IO.File]::Open($TranslationsFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
try {
    if ($taskLive.Length -gt [int]::MaxValue) { throw 'TXT muito grande para esta exportação.' }
    $taskBytes = [byte[]]::new([int]$taskLive.Length); $taskRead = 0
    while ($taskRead -lt $taskBytes.Length) { $taskN = $taskLive.Read($taskBytes, $taskRead, $taskBytes.Length - $taskRead); if ($taskN -eq 0) { break }; $taskRead += $taskN }
    if ($taskRead -ne $taskBytes.Length) { throw 'O TXT foi substituído durante a cópia. Tente novamente.' }
}
finally { $taskLive.Dispose() }
$taskLastNewline = [Array]::LastIndexOf($taskBytes, [byte]10)
if ($taskLastNewline -lt 0) { throw 'TXT sem linhas completas.' }
$taskSnapshotPath = Join-Path $taskExport 'traducoes-snapshot.txt'
$taskSnapshotStream = [IO.File]::Create($taskSnapshotPath)
try { $taskSnapshotStream.Write($taskBytes, 0, $taskLastNewline + 1) } finally { $taskSnapshotStream.Dispose() }
$taskSeedZip = Join-Path $taskExport 'traducoes-iniciais.zip'
$taskSeedArchive = [IO.Compression.ZipFile]::Open($taskSeedZip, [IO.Compression.ZipArchiveMode]::Create)
try { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskSeedArchive, $taskSnapshotPath, 'traducoes.txt', [IO.Compression.CompressionLevel]::Optimal) | Out-Null }
finally { $taskSeedArchive.Dispose() }
$taskParts = @(); $taskMaxPart = 20MB
if ((Get-Item -LiteralPath $taskSeedZip).Length -le $taskMaxPart) {
    Copy-Item -LiteralPath $taskSeedZip -Destination (Join-Path $taskRepo 'data\traducoes-iniciais.zip')
    $taskParts += @{ File = 'traducoes-iniciais.zip'; Sha256 = (Get-FileHash -LiteralPath $taskSeedZip -Algorithm SHA256).Hash }
} else {
    $taskInput = [IO.File]::OpenRead($taskSeedZip); $taskBuffer = [byte[]]::new($taskMaxPart); $taskPartNumber = 0
    try {
        while (($taskN = $taskInput.Read($taskBuffer, 0, $taskBuffer.Length)) -gt 0) {
            $taskPartNumber++; $taskName = 'traducoes-iniciais.zip.{0:D3}' -f $taskPartNumber
            $taskPath = Join-Path $taskRepo ('data\' + $taskName); $taskPartStream = [IO.File]::Create($taskPath)
            try { $taskPartStream.Write($taskBuffer, 0, $taskN) } finally { $taskPartStream.Dispose() }
            $taskParts += @{ File = $taskName; Sha256 = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash }
        }
    } finally { $taskInput.Dispose() }
}
$taskUtf8 = [Text.UTF8Encoding]::new($false)
$taskSeedMetadata = @{ Parts = $taskParts; ArchiveSha256 = (Get-FileHash -LiteralPath $taskSeedZip -Algorithm SHA256).Hash; TextSha256 = (Get-FileHash -LiteralPath $taskSnapshotPath -Algorithm SHA256).Hash }
[IO.File]::WriteAllText((Join-Path $taskRepo 'data\traducoes-iniciais.json'), ($taskSeedMetadata | ConvertTo-Json -Depth 8), $taskUtf8)
$taskZipPath = Join-Path $taskRelease 'latest.zip'; $taskArchive = [IO.Compression.ZipFile]::Open($taskZipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskName in @('BrisaXIV.dll','BrisaXIV.json','BrisaXIV.deps.json')) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $BinaryDirectory $taskName), $taskName) | Out-Null }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, $taskSnapshotPath, 'traducoes.txt') | Out-Null
} finally { $taskArchive.Dispose() }
$taskEntry = [ordered]@{}
foreach ($taskProperty in $taskManifest.PSObject.Properties) { $taskEntry[$taskProperty.Name] = $taskProperty.Value }
$taskLink = "https://github.com/$Repository/releases/download/$taskTag/latest.zip"
$taskEntry.Author = 'BrisasXIV'; $taskEntry.RepoUrl = "https://github.com/$Repository"
$taskEntry.AssemblyVersion = '0.0.0.0'; $taskEntry.TestingAssemblyVersion = $taskAssemblyVersion
$taskEntry.TestingDalamudApiLevel = $taskManifest.DalamudApiLevel; $taskEntry.IsTestingExclusive = $true; $taskEntry.IsHide = $false
$taskEntry.DownloadLinkInstall = $taskLink; $taskEntry.DownloadLinkUpdate = $taskLink; $taskEntry.DownloadLinkTesting = $taskLink
$taskEntry.TestingChangelog = 'Interface lateral, custos e tokens da IA, alerta de saldo/cota e armazenamento persistente.'
$taskEntry.LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
[IO.File]::WriteAllText((Join-Path $taskRepo 'repo.json'), (ConvertTo-Json -InputObject @($taskEntry) -Depth 10), $taskUtf8)
# Never print matching sensitive values. Test placeholders are intentionally short.
$taskSensitivePattern = '(?i)sk-(?:proj-|admin-)?[a-z0-9_-]{36,}|[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}:fx|[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}'
foreach ($taskSource in Get-ChildItem -LiteralPath $taskRepo -File -Recurse) {
    if ($taskSource.Extension -in @('.cs','.csproj','.json','.md','.ps1','.dll')) {
        $taskData = [IO.File]::ReadAllBytes($taskSource.FullName)
        $taskScan = [Text.Encoding]::UTF8.GetString($taskData) + [Text.Encoding]::Unicode.GetString($taskData)
        if ([regex]::IsMatch($taskScan, $taskSensitivePattern)) { throw 'A exportação contém um possível segredo ou e-mail. Revise os arquivos antes de enviar; nenhum upload foi realizado.' }
    }
}
if ([regex]::IsMatch([Text.Encoding]::UTF8.GetString($taskBytes, 0, $taskLastNewline + 1), $taskSensitivePattern)) { throw 'O TXT contém um possível segredo ou e-mail. Revise antes de publicar; nenhum upload foi realizado.' }
$taskReleaseNotes = @"
Brisa XIV $taskVersion — versão experimental, Dalamud API $($taskManifest.DalamudApiLevel).
Varredura rápida dos textos já salvos, com limite de tempo por quadro e sem reenvio à API.
Controle independente do diário de missões; atalho /brisa jornal.
Descrições na área rolável do JournalDetail usam a quebra de linha nativa sem crescer a janela.
Aba O que traduzir: categorias, seleção conjunta e explicações no botão ?.
Subdivisão de títulos e descrições; nomes de NPCs opcionais, locais/viagens, fabricação e coleções.
Nova interface lateral, gráfico de custos e tokens da IA e alerta para créditos/cotas esgotados.
Margens internas, seções e tabelas com mais espaço para facilitar a leitura.
Caixas de confirmação Sim/Não e OK ajustam altura e posição dos botões para evitar sobreposição.
Custos locais são estimativas; consulta de custos da OpenAI exige credencial administrativa separada.
O TXT inicial vem no pacote. Novas traduções, progresso e histórico ficam na pasta persistente do Dalamud.
Anexo: latest.zip. Marque esta release como pré-lançamento. Tag exata: $taskTag.
"@
[IO.File]::WriteAllText((Join-Path $taskRelease 'NOTAS-DA-RELEASE.txt'), $taskReleaseNotes, $taskUtf8)
[IO.File]::WriteAllText((Join-Path $taskRelease 'SHA256.txt'), ((Get-FileHash -LiteralPath $taskZipPath -Algorithm SHA256).Hash + '  latest.zip'), $taskUtf8)
$taskSourceZip = Join-Path $taskExport 'BrisaXIV-fontes-para-GitHub.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($taskRepo, $taskSourceZip)
Write-Output ('FONTE LIMPA: ' + $taskRepo)
Write-Output ('ZIP DAS FONTES (extraia para enviar os arquivos): ' + $taskSourceZip)
Write-Output ('ANEXO DA RELEASE: ' + $taskZipPath)
Write-Output ('TAG DA RELEASE: ' + $taskTag)
Write-Output ('URL EXPERIMENTAL (funciona após publicar): https://raw.githubusercontent.com/' + $Repository + '/main/repo.json')
Write-Output 'Nenhum arquivo foi enviado à internet. Chaves, dados de cobrança, progresso e logs não foram exportados.'
