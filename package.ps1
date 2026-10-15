<#
.SYNOPSIS
    Builds the distributable archive for Nexus Mods / GitHub releases.

.DESCRIPTION
    Packs only what the game needs. Deliberately excluded:
      src, tests, libs             - source and third-party/proprietary files
      Config/prism_profiles.json   - user data that may contain API keys

    Produces dist\AIInfluencePrism-<version>.zip plus a -src.zip that
    carries the repository sources for Nexus' "source" upload slot.
#>
[CmdletBinding()]
param([switch] $IncludeSource)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'

$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'SubModule.xml') -Raw)).Module.Version.value.TrimStart('v')
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# --- release: game payload only ---
$stage = Join-Path $env:TEMP ('prism-pkg-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'AIInfluencePrism'
New-Item -ItemType Directory -Force -Path $payload | Out-Null

Copy-Item -LiteralPath (Join-Path $root 'SubModule.xml') $payload
if (Test-Path (Join-Path $root 'CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $root 'CHANGELOG.md') $payload }
if (Test-Path (Join-Path $root 'README.md')) { Copy-Item -LiteralPath (Join-Path $root 'README.md') $payload }
Copy-Item -LiteralPath (Join-Path $root 'new-prompts-detection') $payload -Recurse
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'bin\Win64_Shipping_Client') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'bin\Win64_Shipping_Client\AIInfluencePrism.dll') (Join-Path $payload 'bin\Win64_Shipping_Client')
if (Test-Path (Join-Path $root 'bin\Win64_Shipping_Client\AIInfluencePrism.pdb')) {
    Copy-Item -LiteralPath (Join-Path $root 'bin\Win64_Shipping_Client\AIInfluencePrism.pdb') (Join-Path $payload 'bin\Win64_Shipping_Client')
}
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'Config') | Out-Null
if (Test-Path (Join-Path $root 'Config\prism_profiles.example.json')) {
    Copy-Item -LiteralPath (Join-Path $root 'Config\prism_profiles.example.json') (Join-Path $payload 'Config')
}
if (Test-Path (Join-Path $root 'GUI')) {
    Copy-Item -LiteralPath (Join-Path $root 'GUI') $payload -Recurse
}

$zip = Join-Path $dist "AIInfluencePrism-$version.zip"
Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip)
Write-Host "Release archive: $zip"

# --- optional: source archive ---
if ($IncludeSource) {
    $srcStage = Join-Path $env:TEMP ('prism-src-' + [guid]::NewGuid().ToString('N'))
    $srcPayload = Join-Path $srcStage 'AIInfluencePrism'
    New-Item -ItemType Directory -Force -Path $srcPayload | Out-Null
    foreach ($item in @('src','tests','build.bat','package.ps1','fetch-refs.ps1','.gitignore','SubModule.xml','Config\prism_profiles.example.json','README.md','LICENSE','CHANGELOG.md')) {
        $p = Join-Path $root $item
        if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $srcPayload -Recurse }
    }
    foreach ($junk in @('obj','bin')) {
        Get-ChildItem -LiteralPath $srcPayload -Recurse -Directory -Filter $junk -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    $srcZip = Join-Path $dist "AIInfluencePrism-$version-src.zip"
    Remove-Item -LiteralPath $srcZip -Force -ErrorAction SilentlyContinue
    [System.IO.Compression.ZipFile]::CreateFromDirectory($srcPayload, $srcZip)
    Write-Host "Source archive:  $srcZip"
}

Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Done. Archive contains: SubModule.xml, new-prompts-detection, bin\Win64_Shipping_Client\AIInfluencePrism.dll"
