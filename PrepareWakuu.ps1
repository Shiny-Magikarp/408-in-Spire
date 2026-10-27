param()
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Force -Path 'reports','models/wakuu','.tools' | Out-Null
    python tools/ai/prepare_model.py
    if ($LASTEXITCODE -ne 0) { throw 'Pinned base model download failed.' }
    python tools/ai/build_corpus.py
    if ($LASTEXITCODE -ne 0) { throw 'Essay and training corpus generation failed.' }
    python -m pip install --target .tools/ai-python onnx==1.17.0 onnxruntime==1.23.2 --disable-pip-version-check
    if ($LASTEXITCODE -ne 0) { throw 'Development ONNX tools unavailable.' }
    python tools/ai/train_wakuu.py
    if ($LASTEXITCODE -ne 0) { throw 'Local training failed; existing release model was preserved.' }
    python tools/ai/select_precision.py
    if ($LASTEXITCODE -ne 0) { throw 'Precision validation failed.' }
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/IDEA-CCNL/Fengshenbang-LM/main/LICENSE' -OutFile models/wakuu/LICENSE.txt
    Copy-Item -LiteralPath tools/ai/MODEL-NOTICE.txt -Destination models/wakuu/NOTICE.txt
    Write-Output 'Prepared offline Wakuu model. Rebuild with ./Build.ps1 -SkipAssets -Install.'
} finally { Pop-Location }
