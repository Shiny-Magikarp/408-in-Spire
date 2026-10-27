param([switch]$Headless)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$testProcess = $null
$builtSmoke = $false
$isolatedRuntime = $null
try {
    $runningGame = [bool](Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue)
    ./Build.ps1 -SkipAssets -Install:(!$runningGame) -Smoke
    $builtSmoke = $true
    $runtimeRoot = (Resolve-Path '..').Path
    if ($runningGame) {
        # A separate executable directory keeps testing out of the running game's mods.
        # Native binaries are read through hard links; only the two test mods are copied.
        $isolatedRuntime = Join-Path $PSScriptRoot '.tools/smoke-game'
        New-Item -ItemType Directory -Force -Path $isolatedRuntime | Out-Null
        foreach ($native in Get-ChildItem -LiteralPath $runtimeRoot -File) {
            if ($native.Extension -notin '.exe','.dll','.pck','.json') { continue }
            $link = Join-Path $isolatedRuntime $native.Name
            if (!(Test-Path -LiteralPath $link)) { New-Item -ItemType HardLink -Path $link -Target $native.FullName | Out-Null }
        }
        foreach ($folder in @('data_sts2_windows_x86_64','408InSpire')) {
            $link = Join-Path $isolatedRuntime $folder
            if (!(Test-Path -LiteralPath $link)) {
                New-Item -ItemType Junction -Path $link -Target (Join-Path $runtimeRoot $folder) | Out-Null
            }
        }
        $isolatedMods = Join-Path $isolatedRuntime 'mods'
        New-Item -ItemType Directory -Force -Path $isolatedMods | Out-Null
        Copy-Item -LiteralPath 'dist/Spire408' -Destination $isolatedMods -Recurse -Force
        if (!(Test-Path -LiteralPath (Join-Path $isolatedMods 'STS2-RitsuLib'))) {
            Copy-Item -LiteralPath '.tools/ritsulib' -Destination (Join-Path $isolatedMods 'STS2-RitsuLib') -Recurse
        }
        $runtimeRoot = $isolatedRuntime
    }
    $testAppData = Join-Path $PSScriptRoot '.tools/test-user'
    $settingsDir = Join-Path $testAppData 'SlayTheSpire2/default/1'
    New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
    @{
        schema_version = 8; language = 'zhs'; fullscreen = $false; skip_intro_logo = $true; volume_master = 0
        mod_settings = @{ mods_enabled = $true; mod_list = @(@{ id = 'STS2AIAgent'; source = 1; is_enabled = $false }) }
    } | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 -LiteralPath (Join-Path $settingsDir 'settings.save')
    $testArguments = @('--force-steam=off','--spire408-smoke','--quit-after','7200','--log-file','408InSpire/reports/game-integration.log')
    if ($Headless) { $testArguments += '--headless' } else { $testArguments += @('--rendering-method','gl_compatibility') }
    $previousAppData = $env:APPDATA
    try {
        $env:APPDATA = $testAppData
        $testProcess = Start-Process -FilePath (Join-Path $runtimeRoot 'SlayTheSpire2.exe') -ArgumentList $testArguments -WorkingDirectory $runtimeRoot -WindowStyle Hidden -RedirectStandardOutput 'reports/game-integration-stdout.log' -RedirectStandardError 'reports/game-integration-stderr.log' -PassThru
    } finally { $env:APPDATA = $previousAppData }
    if (!$testProcess.WaitForExit(60000)) { $testProcess.Kill(); throw 'Game integration test timed out.' }
    $checks = Get-Content -LiteralPath 'reports/game-integration-stdout.log' | Where-Object { $_ -match '^\[Spire408 smoke\]' }
    $checks | Set-Content -Encoding utf8 -LiteralPath 'reports/integration-summary.txt'
    $checks | Write-Output
    if ($testProcess.ExitCode -ne 0 -or !($checks -match 'COMPLETE')) { throw 'Game integration test failed. Inspect reports/game-integration-stderr.log.' }
} finally {
    if ($testProcess -and !$testProcess.HasExited) { $testProcess.Kill(); $testProcess.WaitForExit(5000) | Out-Null }
    # Always restore the production DLL and package, excluding the automatic test entry point.
    try { if ($builtSmoke) { ./Build.ps1 -SkipAssets -Install:($null -eq $isolatedRuntime) } } finally { Pop-Location }
}
