$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    python -X utf8 tools/import_questions.py @args
    if ($LASTEXITCODE -ne 0) { throw "题库导入失败，退出码：$LASTEXITCODE" }
} finally {
    Pop-Location
}

