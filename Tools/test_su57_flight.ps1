param(
    [ValidatePattern('^[0-9]{2}$')][string]$Run = '02',
    [int]$TimeoutSeconds = 1000,
    [string]$Unity = $env:SU57_UNITY_EDITOR,
    [string]$Game = $env:SU57_GAME_DIR,
    [switch]$Canopy
)
$ErrorActionPreference = 'Stop'
$su57Project = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Game) -or [string]::IsNullOrWhiteSpace($Unity)) {
    throw 'Pass -Game and -Unity, or set SU57_GAME_DIR and SU57_UNITY_EDITOR.'
}
$su57Game = (Resolve-Path -LiteralPath $Game).Path
$su57Compiler = Join-Path (Split-Path -Parent $Unity) 'Data\DotNetSdkRoslyn\csc.dll'
if (!(Test-Path -LiteralPath $su57Compiler)) { throw "Unity compiler not found: $su57Compiler" }
$su57Output = Join-Path $su57Project ('Builds\SU57\FlightPhase2\run-' + $Run)
$su57Probe = Join-Path $su57Project 'Builds\SU57\Su57RuntimeProbe.dll'
$su57InstalledProbe = Join-Path $su57Game 'BepInEx\plugins\Su57RuntimeProbe.dll'
if (Test-Path -LiteralPath $su57InstalledProbe) { throw 'An existing diagnostic probe is installed; remove it manually before this run.' }
if (Get-Process -Name NuclearOption -ErrorAction SilentlyContinue) { throw 'Nuclear Option is already running; close it before launching an isolated flight test.' }
if (Test-Path -LiteralPath (Join-Path $su57Output 'flight-validation.txt')) { throw "Run $Run already has results; choose an unused run number." }
New-Item -ItemType Directory -Path $su57Output -Force | Out-Null
$su57Response = Join-Path $su57Output 'runtimeprobe.rsp'
$su57References = @(Get-ChildItem -LiteralPath (Join-Path $su57Game 'NuclearOption_Data\Managed') -Filter *.dll -File | Sort-Object Name | ForEach-Object { $_.FullName })
$su57References += Join-Path $su57Game 'BepInEx\core\BepInEx.dll'
$su57CompileArgs = @('/nostdlib+', '/target:library', ('/out:"' + $su57Probe + '"'))
$su57CompileArgs += @($su57References | ForEach-Object { '/reference:"' + $_ + '"' })
$su57CompileArgs += '"' + (Join-Path $PSScriptRoot 'RuntimeProbe.cs') + '"'
[IO.File]::WriteAllLines($su57Response, [string[]]$su57CompileArgs, [Text.UTF8Encoding]::new($false))
& dotnet $su57Compiler /noconfig ('@' + $su57Response)
if ($LASTEXITCODE -ne 0) { throw 'Flight probe compilation failed.' }
Copy-Item -LiteralPath $su57Probe -Destination $su57InstalledProbe -Force
$su57ProbeHash = (Get-FileHash -LiteralPath $su57Probe -Algorithm SHA256).Hash
Copy-Item -LiteralPath $su57Probe -Destination (Join-Path $su57Output 'Su57RuntimeProbe.dll')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RuntimeProbe.cs') -Destination (Join-Path $su57Output 'RuntimeProbe.cs')
$su57Bundles = @(Get-ChildItem -LiteralPath (Join-Path $su57Game 'BepInEx\plugins') -Recurse -File | Where-Object { $_.Name -eq 'Su-57.dll' -or $_.Name -like 'Su-57_*.nobp' } | ForEach-Object {
    @{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
@{ started_utc = [DateTime]::UtcNow.ToString('O'); run = $Run; bundles = $su57Bundles; probe_sha256 = $su57ProbeHash; probe_source_sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'RuntimeProbe.cs') -Algorithm SHA256).Hash } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $su57Output 'run-metadata.json') -Encoding utf8
$su57Process = $null
$su57PreviousProject = $env:SU57_PROJECT_ROOT
$env:SU57_PROJECT_ROOT = $su57Project
try {
    $su57Arguments = @('-su57-flight-test', ('-su57-flight-run=' + $Run), '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', ('"' + (Join-Path $su57Output 'Player.log') + '"'))
    if ($Canopy) { $su57Arguments += '-su57-canopy-test' }
    $su57Process = Start-Process -FilePath (Join-Path $su57Game 'NuclearOption.exe') -WorkingDirectory $su57Game -ArgumentList $su57Arguments -WindowStyle Hidden -PassThru
    $su57Process.Id | Set-Content -LiteralPath (Join-Path $su57Output 'process-id.txt')
    $su57Deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $su57LastPhase = ''
    Write-Output "Started isolated flight test $Run, PID $($su57Process.Id)"
    while (!$su57Process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $su57Deadline) { Stop-Process -Id $su57Process.Id; throw "Flight test exceeded $TimeoutSeconds seconds; only its own test process was stopped." }
        $su57ProgressPath = Join-Path $su57Output 'progress.txt'
        if (Test-Path -LiteralPath $su57ProgressPath) {
            $su57Phase = Get-Content -LiteralPath $su57ProgressPath -Raw
            if ($su57Phase -ne $su57LastPhase) { Write-Output $su57Phase.Trim(); $su57LastPhase = $su57Phase }
        }
    }
    Copy-Item -LiteralPath (Join-Path $su57Game 'BepInEx\LogOutput.log') -Destination (Join-Path $su57Output 'BepInEx.log') -Force
    $su57Result = Join-Path $su57Output 'flight-validation.txt'
    if (Test-Path -LiteralPath $su57Result) { Get-Content -LiteralPath $su57Result }
    if ($su57Process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $su57Result) -or !(Select-String -LiteralPath $su57Result -SimpleMatch 'PASS: automated native-physics flight cycle' -Quiet)) { throw "Flight test failed: exit=$($su57Process.ExitCode); see $su57Output" }
}
finally {
    # The diagnostic DLL is archived in the project and never shipped with .nobp.
    if ($null -eq $su57Process -or $su57Process.HasExited) {
        if ((Test-Path -LiteralPath $su57InstalledProbe) -and (Get-FileHash -LiteralPath $su57InstalledProbe -Algorithm SHA256).Hash -eq $su57ProbeHash) {
            New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'RuntimeProbe') -Force | Out-Null
            Copy-Item -LiteralPath $su57InstalledProbe -Destination (Join-Path $PSScriptRoot 'RuntimeProbe\Su57RuntimeProbe.dll') -Force
            Remove-Item -LiteralPath $su57InstalledProbe
        }
    }
    $env:SU57_PROJECT_ROOT = $su57PreviousProject
}
