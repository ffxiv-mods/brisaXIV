param([string]$BackupDirectory = (Join-Path $env:TEMP 'BrisaXIV-installer-backups'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Restaurar-TXT-Inicial.ps1')
if (Get-Process ffxiv_dx11 -ErrorAction SilentlyContinue) {
    throw 'Feche o FFXIV antes de registrar o plugin para evitar que o jogo sobrescreva a configuração.'
}
$taskDll = Join-Path $PSScriptRoot 'plugin\BrisaXIV.dll'
if (-not (Test-Path -LiteralPath $taskDll)) { throw 'BrisaXIV.dll não foi encontrada na pasta plugin.' }
$taskConfigPath = Join-Path $env:APPDATA 'XIVLauncher\dalamudConfig.json'
if (-not (Test-Path -LiteralPath $taskConfigPath)) { throw 'Configuração do Dalamud não encontrada. Abra o jogo pelo XIVLauncher ao menos uma vez.' }
$taskConfig = Get-Content -LiteralPath $taskConfigPath -Raw | ConvertFrom-Json
$taskLocations = $taskConfig.DevPluginLoadLocations
if ($null -eq $taskLocations) { throw 'Formato de configuração não reconhecido. Use /xlsettings > Experimental > Dev Plugin Locations e adicione a DLL manualmente.' }
$taskEntries = if ($taskLocations.PSObject.Properties.Name -contains '$values') { @($taskLocations.'$values') } else { @($taskLocations) }
$taskExisting = $taskEntries | Where-Object { $_.Path -eq $taskDll } | Select-Object -First 1
New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
$taskBackup = Join-Path $BackupDirectory ('dalamudConfig-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
Copy-Item -LiteralPath $taskConfigPath -Destination $taskBackup
if ($taskExisting) { $taskExisting.IsEnabled = $true }
else { $taskEntries += [pscustomobject]@{ Path = $taskDll; IsEnabled = $true; Nickname = 'Brisa pessoal PT-BR' } }
if ($taskLocations.PSObject.Properties.Name -contains '$values') { $taskLocations.'$values' = @($taskEntries) }
else { $taskConfig.DevPluginLoadLocations = @($taskEntries) }
$taskJson = $taskConfig | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($taskConfigPath, $taskJson, [System.Text.UTF8Encoding]::new($false))
$taskVerified = Get-Content -LiteralPath $taskConfigPath -Raw | ConvertFrom-Json
$taskRegistered = if ($taskVerified.DevPluginLoadLocations.PSObject.Properties.Name -contains '$values') { $taskVerified.DevPluginLoadLocations.'$values' } else { $taskVerified.DevPluginLoadLocations }
if (-not ($taskRegistered | Where-Object { $_.Path -eq $taskDll -and $_.IsEnabled })) { throw 'Não foi possível confirmar o registro.' }
Write-Output 'Plugin privado registrado no Dalamud.'
Write-Output ('DLL: ' + $taskDll)
Write-Output ('Backup da configuração: ' + $taskBackup)
Write-Output 'Abra o jogo pelo XIVLauncher, use /xlplugins e ative Brisa XIV em Dev Tools / Installed Dev Plugins. Depois use /brisa.'
