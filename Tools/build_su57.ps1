param(
    [string]$Unity = $env:SU57_UNITY_EDITOR,
    [string]$Game = $env:SU57_GAME_DIR,
    [switch]$ValidateBundle
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildLog = Join-Path $projectRoot 'Logs\su57-build.log'
if ([string]::IsNullOrWhiteSpace($Unity)) {
    throw 'Pass -Unity with the full path to Unity 2022.3.62f2 Unity.exe, or set SU57_UNITY_EDITOR.'
}
if (!(Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
New-Item -ItemType Directory -Path (Split-Path -Parent $buildLog) -Force | Out-Null
$su57PreviousGame = $env:SU57_GAME_DIR
if (![string]::IsNullOrWhiteSpace($Game)) { $env:SU57_GAME_DIR = $Game }
Push-Location -LiteralPath $projectRoot
try {
    $su57BuildProcess = Start-Process -FilePath $Unity -ArgumentList @('-batchmode','-quit','-projectPath',('"' + $projectRoot + '"'),'-executeMethod','BuildSu57.Run','-logFile',('"' + $buildLog + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($su57BuildProcess.ExitCode -ne 0) { throw "Unity build failed ($($su57BuildProcess.ExitCode)); see $buildLog" }
    $su57BuilderSource = Get-Content -LiteralPath (Join-Path $projectRoot 'Assets\Editor\SU57\BuildSu57.cs') -Raw
    $su57VersionMatch = [regex]::Match($su57BuilderSource, 'const string Version = "([^"]+)";')
    if (!$su57VersionMatch.Success) { throw 'Cannot read Su-57 build version' }
    $bundle = Join-Path $projectRoot ('Builds\SU57\Internal\Su-57_' + $su57VersionMatch.Groups[1].Value + '.nobp')
    if (!(Test-Path -LiteralPath $bundle)) { throw "Build artifact not produced: $bundle" }
    $su57Install = Join-Path $projectRoot 'Builds\SU57\Install\Su-57.dll'
    if (!(Test-Path -LiteralPath $su57Install)) {
        throw 'Build did not produce the single-file Su-57.dll; see Unity build log and Logs/su57-singlefile-build.log.'
    }
    # Verification is user-owned. Only run this when explicitly requested.
    if ($ValidateBundle) {
        $report = Join-Path $projectRoot 'Builds\SU57\bundle-validation.json'
        python (Join-Path $PSScriptRoot 'validate_su57_bundle.py') $bundle --report $report
        if ($LASTEXITCODE -ne 0) { throw 'Serialized bundle validation failed' }
    }
    Get-Item -LiteralPath $su57Install
}
finally {
    Pop-Location
    $env:SU57_GAME_DIR = $su57PreviousGame
}
