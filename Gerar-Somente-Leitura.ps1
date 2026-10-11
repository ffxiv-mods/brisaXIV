param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$taskPluginRoot = Join-Path $PSScriptRoot 'plugin'
$taskStatePath = Join-Path $taskPluginRoot 'progresso-preparacao.json'
if (-not (Test-Path -LiteralPath $taskStatePath)) { throw 'A preparação completa ainda não começou.' }
$taskState = Get-Content -LiteralPath $taskStatePath -Raw | ConvertFrom-Json
if (-not $taskState.Completed) { throw 'A preparação ainda não terminou. O preparador será mantido.' }
$taskScratch = Join-Path $PSScriptRoot '.build-final'
$taskAppDataOriginal = $env:APPDATA
$taskLocalAppDataOriginal = $env:LOCALAPPDATA
$taskDalamud = Join-Path $taskAppDataOriginal 'XIVLauncher\addon\Hooks\dev'
if ($DotnetPath -eq 'dotnet' -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $taskPortable = Join-Path $PSScriptRoot '..\..\work\dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $taskPortable)) { throw 'SDK .NET 10 não encontrado.' }
    $DotnetPath = (Resolve-Path -LiteralPath $taskPortable).Path
}
Start-Transcript -LiteralPath (Join-Path $taskPluginRoot 'conversao-final.log') -Force | Out-Null
try {
    $env:APPDATA = Join-Path $taskScratch 'appdata'
    $env:LOCALAPPDATA = Join-Path $taskScratch 'localappdata'
    $env:DOTNET_CLI_HOME = Join-Path $taskScratch 'dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:NUGET_PACKAGES = Join-Path $taskScratch 'nuget'
    New-Item -ItemType Directory -Path (Join-Path $env:APPDATA 'NuGet'),$env:LOCALAPPDATA -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NuGet.Config') -Destination (Join-Path $env:APPDATA 'NuGet\NuGet.Config') -Force
    & $DotnetPath run --project (Join-Path $PSScriptRoot 'tests\BrisaXIV.Tests.csproj') -c Release "-p:DalamudHome=$taskDalamud" -- $taskScratch --verify-complete $taskPluginRoot
    if ($LASTEXITCODE -ne 0) { throw 'Há entradas sem tradução no TXT. A DLL atual foi preservada.' }
    $taskStaging = Join-Path $taskScratch 'staging'
    & $DotnetPath build (Join-Path $PSScriptRoot 'src\BrisaXIV\BrisaXIV.csproj') -c Release --nologo "-p:DalamudHome=$taskDalamud" '-p:BrisaReadOnly=true' "-p:OutputPath=$taskStaging\"
    if ($LASTEXITCODE -ne 0) { throw 'A compilação final falhou. A DLL atual foi preservada.' }
    $taskBytes = [System.IO.File]::ReadAllBytes((Join-Path $taskStaging 'BrisaXIV.dll'))
    $taskStrings = [System.Text.Encoding]::Unicode.GetString($taskBytes) + [System.Text.Encoding]::Unicode.GetString($taskBytes,1,$taskBytes.Length-1)
    if ($taskStrings.Contains('api-free.deepl.com') -or $taskStrings.Contains('translate.googleapis.com') -or $taskStrings.Contains('api.openai.com') -or $taskStrings.Contains('/api/generate')) { throw 'A DLL ainda contém endpoints online. A DLL atual foi preservada.' }
    $taskRecovery = Join-Path $taskPluginRoot ('backup-preparador-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $taskRecovery -Force | Out-Null
    foreach ($taskName in @('BrisaXIV.dll','BrisaXIV.pdb','BrisaXIV.json','BrisaXIV.deps.json')) {
        Copy-Item -LiteralPath (Join-Path $taskPluginRoot $taskName) -Destination $taskRecovery
    }
    $taskManifest = Get-Content -LiteralPath (Join-Path $taskStaging 'BrisaXIV.json') -Raw | ConvertFrom-Json
    $taskManifest.Name = 'Brisa XIV — Leitura do TXT em PT-BR'
    $taskManifest.Description = 'Versão somente de leitura. Não contém os tradutores online. Usa o TXT pessoal e consulta os originais japoneses locais mesmo com o cliente em inglês. /brisa para configurar.'
    [System.IO.File]::WriteAllText((Join-Path $taskStaging 'BrisaXIV.json'),($taskManifest | ConvertTo-Json -Depth 20),[System.Text.UTF8Encoding]::new($false))
    foreach ($taskName in @('BrisaXIV.dll','BrisaXIV.pdb','BrisaXIV.json','BrisaXIV.deps.json')) {
        Copy-Item -LiteralPath (Join-Path $taskStaging $taskName) -Destination (Join-Path $taskPluginRoot $taskName) -Force
    }
    Write-Output 'SUCESSO: tradutores removidos da DLL. O TXT, índice e chave protegida foram preservados. Recarregue Brisa XIV em /xlplugins ou abra o jogo novamente.'
    Write-Output ('Preparador anterior guardado em: ' + $taskRecovery)
}
finally {
    $env:APPDATA = $taskAppDataOriginal
    $env:LOCALAPPDATA = $taskLocalAppDataOriginal
    Stop-Transcript | Out-Null
}
