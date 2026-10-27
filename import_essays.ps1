param([switch]$Offline)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $importArgs = @('tools/import_essays.py')
    if ($Offline) { $importArgs += '--offline' }
    python @importArgs
    if ($LASTEXITCODE -ne 0) { throw 'Pinned essay import failed.' }
    python tools/ai/build_corpus.py
    if ($LASTEXITCODE -ne 0) { throw 'Original practice generation failed.' }
    python tools/build_essays.py
    if ($LASTEXITCODE -ne 0) { throw 'Essay merge failed.' }
    node tools/render_essays.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Essay presentation failed.' }
    Write-Output 'Essay import ready. Run Build.ps1 -SkipAssets -Install to update the game.'
} finally { Pop-Location }
