# ============================================================
#  AIInfluence Prism - fetch reference assemblies
#
#  Копирует из установленной игры DLL, которые нужны для сборки
#  (TaleWorlds, Harmony, MCM, Newtonsoft.Json). Сборки не хранятся
#  в репозитории. Запуск:
#
#    powershell -ExecutionPolicy Bypass -File fetch-refs.ps1 `
#        -GameFolder "C:\...\Mount & Blade II Bannerlord"
# ============================================================
param(
    [Parameter(Mandatory = $true)]
    [string]$GameFolder,
    # Дополнительные папки поиска (например, Workshop-контент):
    # Y:\SteamLibrary\steamapps\workshop\content\261550
    [string[]]$ExtraFolders = @()
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $GameFolder 'Modules'))) {
    Write-Error "GameFolder is not a Bannerlord install: $GameFolder"
    exit 1
}

$libsRoot = Join-Path $PSScriptRoot 'libs'
$taleRoot = Join-Path $libsRoot 'TaleWorlds'
New-Item -ItemType Directory -Force -Path $taleRoot | Out-Null

# Имя -> результат: копируем только первое найденное попадание,
# приоритет у путей под Modules\<id>\bin\Win64_Shipping_Client.
$targets = @(
    @{ Name = 'TaleWorlds.Core.dll';         Dest = $taleRoot },
    @{ Name = 'TaleWorlds.Library.dll';      Dest = $taleRoot },
    @{ Name = 'TaleWorlds.Localization.dll'; Dest = $taleRoot },
    @{ Name = 'TaleWorlds.SaveSystem.dll';   Dest = $taleRoot },
    @{ Name = 'TaleWorlds.ScreenSystem.dll'; Dest = $taleRoot },
    @{ Name = 'TaleWorlds.CampaignSystem.dll'; Dest = $taleRoot },
    @{ Name = 'TaleWorlds.MountAndBlade.dll';  Dest = $taleRoot },
    @{ Name = 'TaleWorlds.DotNet.dll';         Dest = $taleRoot },
    @{ Name = 'TaleWorlds.Engine.dll';        Dest = $taleRoot },
    @{ Name = 'TaleWorlds.TwoDimension.dll';  Dest = $taleRoot },
    @{ Name = 'TaleWorlds.GauntletUI.dll';    Dest = $taleRoot },
    @{ Name = 'TaleWorlds.Engine.GauntletUI.dll'; Dest = $taleRoot },
    @{ Name = 'TaleWorlds.ObjectSystem.dll';  Dest = $taleRoot },
    @{ Name = '0Harmony.dll';                Dest = $libsRoot },
    @{ Name = 'MCMv5.dll';                   Dest = $libsRoot },
    @{ Name = 'Newtonsoft.Json.dll';         Dest = $libsRoot }
)

$found = 0
foreach ($target in $targets) {
    $existing = Join-Path $target.Dest $target.Name
    if (Test-Path $existing) {
        Write-Host "[Prism] ok (cached): $($target.Name)"
        $found++
        continue
    }

    $searchRoots = @($GameFolder) + $ExtraFolders
    $hits = $searchRoots | ForEach-Object {
        Get-ChildItem -Path $_ -Recurse -Filter $target.Name -File -ErrorAction SilentlyContinue
    } | Sort-Object { if ($_.FullName -like '*\bin\Win64_Shipping_Client\*') { 0 } else { 1 } }
    if (-not $hits) {
        Write-Warning "[Prism] not found in game: $($target.Name)"
        continue
    }

    Copy-Item $hits[0].FullName $existing -Force
    Write-Host "[Prism] copied: $($target.Name)"
    $found++
}

if ($found -lt $targets.Count) {
    Write-Warning "[Prism] $($found)/$($targets.Count) assemblies located; build may fail for missing ones."
} else {
    Write-Host '[Prism] all reference assemblies are ready.'
}
