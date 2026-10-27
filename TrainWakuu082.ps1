param()
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$previousModel = $env:SPIRE408_AI_TEST_MODEL
function Invoke-CheckedPython([string[]]$Arguments) {
    & python -X utf8 @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Python step failed: $($Arguments[0])" }
}
try {
    if (!(Test-Path -LiteralPath '.tools/ai/full-train/checkpoint/config.json')) {
        throw 'Requires the dev2 checkpoint. Prepare the data and run the historical ./TrainWakuu.ps1 first.'
    }
    # Preserve a fixed baseline even when production already contains dev3.
    Invoke-CheckedPython @('tools/ai/stage_curriculum_082.py', '--baseline-only')
    Invoke-CheckedPython @('tools/ai/build_curriculum_082.py')
    # Freeze exact wording and assert it is disjoint before training/inference.
    Invoke-CheckedPython @('tools/ai/holdout_082.py')
    Invoke-CheckedPython @('tools/ai/train_full_wakuu.py',
        '--checkpoint', '.tools/ai/full-train/checkpoint', '--corpus', '.tools/ai/curriculum-082',
        '--workdir', '.tools/ai/curriculum-train-082', '--report', 'reports/wakuu-curriculum-training-082.json',
        '--model-version', 'wakuu-nli-408-dev3', '--seed', '408082', '--learning-rate', '1e-5',
        '--train-last-layers', '4', '--epochs', '4', '--batch-size', '12')
    Invoke-CheckedPython @('tools/ai/stage_curriculum_082.py')
    dotnet build benchmarks/MarkingBench.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
    $models = @('.tools/ai/model-backups/dev2-before-082',
        '.tools/ai/curriculum-train-082/candidate', '.tools/ai/curriculum-train-082/candidate-fp32')
    $names = @('baseline', 'candidate', 'fp32')
    for ($index = 0; $index -lt $models.Count; $index++) {
        dotnet benchmarks/bin/Debug/net9.0/MarkingBench.dll . .tools/ai/holdout-082.json $models[$index] "reports/wakuu-holdout-082-$($names[$index]).json"
        if ($LASTEXITCODE -ne 0) { throw 'Fixed holdout evaluation failed.' }
    }
    $env:SPIRE408_AI_TEST_MODEL = Join-Path $PSScriptRoot '.tools/ai/curriculum-train-082/candidate'
    dotnet run --project tests/CoreTests.csproj *> reports/core-tests-082-candidate.log
    if ($LASTEXITCODE -ne 0) { throw 'Candidate core regression failed; production model is unchanged.' }
    Invoke-CheckedPython @('tools/ai/promote_curriculum_082.py')
    Invoke-CheckedPython @('tools/ai/summarize_082.py')
    Write-Output 'dev3 passed local promotion checks. Run ./SmokeTest.ps1 and ./Build.ps1 -SkipAssets -Install.'
} finally {
    $env:SPIRE408_AI_TEST_MODEL = $previousModel
    Pop-Location
}
