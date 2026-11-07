param(
    [Parameter(Mandatory = $true)][string]$SourceProject
)
$ErrorActionPreference = 'Stop'
$su57Destination = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$su57Source = (Resolve-Path -LiteralPath $SourceProject).Path.TrimEnd('\', '/')
if ($su57Source -eq $su57Destination) { throw 'Choose the original authoring project, not this repository.' }
$su57ModRelative = 'Assets\Blueprinter\Mods\SU57'
$su57SourceMod = Join-Path $su57Source $su57ModRelative
if (!(Test-Path -LiteralPath (Join-Path $su57SourceMod 'Source\SU57_DamageSections.json'))) {
    throw 'The source project does not contain the expected Su-57 authoring resources.'
}
$su57DestinationMod = Join-Path $su57Destination $su57ModRelative
New-Item -ItemType Directory -Path $su57DestinationMod -Force | Out-Null
$su57Copied = 0
foreach ($su57File in (Get-ChildItem -LiteralPath $su57SourceMod -File -Recurse)) {
    $su57Relative = $su57File.FullName.Substring($su57SourceMod.Length).TrimStart('\', '/')
    if ($su57Relative -eq 'README.md') { continue }
    $su57Target = Join-Path $su57DestinationMod $su57Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $su57Target) -Force | Out-Null
    Copy-Item -LiteralPath $su57File.FullName -Destination $su57Target -Force
    $su57Copied++
}
foreach ($su57Cache in @(
    'Reference\SU57\VectoringVideoResearch\source-nozzle-weights.json',
    'Reference\SU57\ControlSurfaces\v0.2.11\source-wing-weights.json'
)) {
    $su57CacheSource = Join-Path $su57Source $su57Cache
    if (Test-Path -LiteralPath $su57CacheSource) {
        $su57CacheTarget = Join-Path $su57Destination $su57Cache
        New-Item -ItemType Directory -Path (Split-Path -Parent $su57CacheTarget) -Force | Out-Null
        Copy-Item -LiteralPath $su57CacheSource -Destination $su57CacheTarget -Force
    }
}
Write-Output "Copied $su57Copied local aircraft files. Game imports and tests were not run."
Write-Output 'Complete Blueprinter Project Setup separately. See docs/BUILDING_ZH.md.'
