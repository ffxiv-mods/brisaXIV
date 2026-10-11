param([string]$DotnetPath = 'dotnet', [string]$ScratchDirectory = (Join-Path $env:TEMP 'BrisaXIV-build'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Restaurar-TXT-Inicial.ps1')
$taskAppDataOriginal = $env:APPDATA
$taskLocalAppDataOriginal = $env:LOCALAPPDATA
$taskDalamud = Join-Path $taskAppDataOriginal 'XIVLauncher\addon\Hooks\dev'
if (-not (Test-Path -LiteralPath (Join-Path $taskDalamud 'Dalamud.dll'))) { throw 'Referências do Dalamud não encontradas.' }
if ($DotnetPath -eq 'dotnet' -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $taskPortable = Join-Path $PSScriptRoot '..\..\work\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $taskPortable) { $DotnetPath = (Resolve-Path -LiteralPath $taskPortable).Path }
    else { throw 'Instale o SDK .NET 10 ou passe -DotnetPath com o caminho de um SDK portátil.' }
}
try {
    $env:APPDATA = Join-Path $ScratchDirectory 'appdata'
    $env:LOCALAPPDATA = Join-Path $ScratchDirectory 'localappdata'
    $env:DOTNET_CLI_HOME = Join-Path $ScratchDirectory 'dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:NUGET_PACKAGES = Join-Path $ScratchDirectory 'nuget-packages'
    New-Item -ItemType Directory -Path (Join-Path $env:APPDATA 'NuGet'),$env:LOCALAPPDATA -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NuGet.Config') -Destination (Join-Path $env:APPDATA 'NuGet\NuGet.Config') -Force
    & $DotnetPath build (Join-Path $PSScriptRoot 'src\BrisaXIV\BrisaXIV.csproj') -c Release --nologo "-p:DalamudHome=$taskDalamud"
    if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
    & $DotnetPath run --project (Join-Path $PSScriptRoot 'tests\BrisaXIV.Tests.csproj') -c Release "-p:DalamudHome=$taskDalamud" -- (Join-Path $ScratchDirectory 'tests')
    if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
}
finally { $env:APPDATA = $taskAppDataOriginal; $env:LOCALAPPDATA = $taskLocalAppDataOriginal }
