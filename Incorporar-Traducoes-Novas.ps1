# Incorpora ao TXT publicado (plugin\traducoes.txt) as traduções novas que o plugin criou durante o jogo
# (traducoes-novas.txt: Google Tradutor e textos do idioma do cliente). Só acrescenta originais que ainda
# não existem no destino: correções já publicadas nunca são sobrescritas. Repetições na origem: a última vence.
param(
    [string[]]$Origem = @(
        (Join-Path $env:APPDATA 'XIVLauncher\pluginConfigs\BrisaXIV\traducoes-novas.txt'),
        (Join-Path $PSScriptRoot 'plugin\traducoes-novas.txt')),
    [string]$Destino = (Join-Path $PSScriptRoot 'plugin\traducoes.txt'),
    # Depois de incorporar, renomeia cada origem para traducoes-novas.incorporado-<data>.txt.
    [switch]$Limpar
)
$ErrorActionPreference = 'Stop'
$taskUtf8 = New-Object System.Text.UTF8Encoding($false)
if (-not (Test-Path -LiteralPath $Destino)) { throw "TXT de destino não encontrado: $Destino" }

$taskExisting = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($taskLine in [IO.File]::ReadLines($Destino, $taskUtf8)) {
    if ($taskLine.StartsWith('#')) { continue }
    $taskTab = $taskLine.IndexOf("`t")
    if ($taskTab -gt 0) { [void]$taskExisting.Add($taskLine.Substring(0, $taskTab)) }
}

$taskNew = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
$taskSources = @($Origem | Where-Object { Test-Path -LiteralPath $_ })
if ($taskSources.Count -eq 0) { Write-Output 'Nenhum traducoes-novas.txt encontrado. Nada a incorporar.'; return }
$taskRead = 0
foreach ($taskSource in $taskSources) {
    foreach ($taskLine in [IO.File]::ReadLines($taskSource, $taskUtf8)) {
        if ($taskLine.StartsWith('#')) { continue }
        $taskTab = $taskLine.IndexOf("`t")
        if ($taskTab -lt 1 -or $taskTab -eq $taskLine.Length - 1) { continue }
        $taskRead++
        $taskKey = $taskLine.Substring(0, $taskTab)
        if (-not $taskExisting.Contains($taskKey)) { $taskNew[$taskKey] = $taskLine.Substring($taskTab + 1) }
    }
}

if ($taskNew.Count -gt 0) {
    $taskBuilder = New-Object System.Text.StringBuilder
    # Garante que a primeira linha nova não seja colada à última linha do destino.
    $taskStream = [IO.File]::OpenRead($Destino)
    try { if ($taskStream.Length -gt 0) { [void]$taskStream.Seek(-1, 'End'); if ($taskStream.ReadByte() -ne 10) { [void]$taskBuilder.Append("`n") } } }
    finally { $taskStream.Dispose() }
    [void]$taskBuilder.Append('# Incorporadas de traducoes-novas.txt em ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "`n")
    foreach ($taskPair in $taskNew.GetEnumerator()) { [void]$taskBuilder.Append($taskPair.Key + "`t" + $taskPair.Value + "`n") }
    [IO.File]::AppendAllText($Destino, $taskBuilder.ToString(), $taskUtf8)
}
Write-Output ("{0} linhas lidas de {1} arquivo(s); {2} traduções novas incorporadas em {3}; {4} ignoradas (já publicadas ou repetidas)." -f $taskRead, $taskSources.Count, $taskNew.Count, $Destino, ($taskRead - $taskNew.Count))

if ($Limpar) {
    $taskStamp = Get-Date -Format 'yyyyMMddHHmmss'
    foreach ($taskSource in $taskSources) {
        $taskTarget = Join-Path (Split-Path -Parent $taskSource) "traducoes-novas.incorporado-$taskStamp.txt"
        Move-Item -LiteralPath $taskSource -Destination $taskTarget
        Write-Output "Origem arquivada em $taskTarget"
    }
}
