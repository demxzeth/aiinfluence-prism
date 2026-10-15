$ErrorActionPreference = 'Stop'

$src = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($src)) {
    $src = "Z:\FOR NEXUS PUSH\AIInfluencePrism"
}
$dest = "Y:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\AIInfluencePrism"
$modulesRoot = "Y:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"

$version = "0.5.0"
$subModuleXml = Join-Path $src "SubModule.xml"
if (Test-Path $subModuleXml) {
    $version = ([xml](Get-Content -LiteralPath $subModuleXml -Raw)).Module.Version.value.TrimStart('v')
}

Write-Host "Deploying AIInfluencePrism v$version to $dest..."

# Ensure target directories exist
New-Item -ItemType Directory -Force -Path (Join-Path $dest "bin\Win64_Shipping_Client") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dest "Config") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dest "new-prompts-detection") | Out-Null

# Copy binary payload
try {
    Copy-Item -LiteralPath (Join-Path $src "bin\Win64_Shipping_Client\AIInfluencePrism.dll") -Destination (Join-Path $dest "bin\Win64_Shipping_Client\AIInfluencePrism.dll") -Force
    if (Test-Path (Join-Path $src "bin\Win64_Shipping_Client\AIInfluencePrism.pdb")) {
        Copy-Item -LiteralPath (Join-Path $src "bin\Win64_Shipping_Client\AIInfluencePrism.pdb") -Destination (Join-Path $dest "bin\Win64_Shipping_Client\AIInfluencePrism.pdb") -Force
    }
} catch [System.IO.IOException] {
    Write-Warning "AIInfluencePrism.dll is currently locked by the running game process. Close Bannerlord to update the active DLL."
}

# Copy metadata & configs
Copy-Item -LiteralPath (Join-Path $src "SubModule.xml") -Destination (Join-Path $dest "SubModule.xml") -Force
Copy-Item -LiteralPath (Join-Path $src "CHANGELOG.md") -Destination (Join-Path $dest "CHANGELOG.md") -Force
Copy-Item -LiteralPath (Join-Path $src "README.md") -Destination (Join-Path $dest "README.md") -Force
Copy-Item -LiteralPath (Join-Path $src "Config\prism_profiles.example.json") -Destination (Join-Path $dest "Config\prism_profiles.example.json") -Force

# Copy prompt detection markers
Copy-Item -Path (Join-Path $src "new-prompts-detection\*") -Destination (Join-Path $dest "new-prompts-detection") -Recurse -Force

# Copy GUI assets
if (Test-Path (Join-Path $src "GUI")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $dest "GUI") | Out-Null
    Copy-Item -Path (Join-Path $src "GUI\*") -Destination (Join-Path $dest "GUI") -Recurse -Force
}

# Also sync source code files in destination if source folder exists
if (Test-Path (Join-Path $dest "src")) {
    Copy-Item -Path (Join-Path $src "src\*") -Destination (Join-Path $dest "src") -Recurse -Force
}

Write-Host "Deployment completed successfully."

# Distribute archive
$zipSource = Join-Path $src "dist\AIInfluencePrism-$version.zip"
if (Test-Path $zipSource) {
    Copy-Item -LiteralPath $zipSource -Destination (Join-Path $src "AIInfluencePrism-$version.zip") -Force
    Copy-Item -LiteralPath $zipSource -Destination (Join-Path $modulesRoot "AIInfluencePrism-$version.zip") -Force
    Write-Host "Copied $version archive to:"
    Write-Host " - $src\AIInfluencePrism-$version.zip"
    Write-Host " - $modulesRoot\AIInfluencePrism-$version.zip"
}
