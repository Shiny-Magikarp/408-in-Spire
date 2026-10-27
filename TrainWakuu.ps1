param()
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    # The public repo contains scripts, not copyrighted source banks or weights.
    if (!(Test-Path -LiteralPath '.tools/ai/train/checkpoint/config.json')) {
        ./PrepareWakuu.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Initial authored-domain model preparation failed.' }
    }
    python -X utf8 tools/ai/build_corpus.py
    if ($LASTEXITCODE -ne 0) { throw 'Authored corpus generation failed.' }
    python -X utf8 tools/import_essays.py
    if ($LASTEXITCODE -ne 0) { throw 'Pinned essay import failed.' }
    python -X utf8 tools/build_essays.py
    if ($LASTEXITCODE -ne 0) { throw 'Essay rubric generation failed.' }
    python -X utf8 tools/ai/build_full_corpus.py
    if ($LASTEXITCODE -ne 0) { throw 'Full training corpus generation failed.' }
    python -X utf8 tools/ai/train_full_wakuu.py
    if ($LASTEXITCODE -ne 0) { throw 'Training failed; candidate was not promoted.' }
    python -X utf8 tools/ai/evaluate_full_wakuu.py
    if ($LASTEXITCODE -ne 0) { throw 'Candidate evaluation failed.' }
    python -X utf8 tools/ai/promote_full_wakuu.py
    if ($LASTEXITCODE -ne 0) { throw 'Candidate promotion rejected; inspect reports.' }
    Write-Output 'Experimental dev2 model prepared. Run ./Build.ps1 -SkipAssets -Install to package it.'
} finally { Pop-Location }
