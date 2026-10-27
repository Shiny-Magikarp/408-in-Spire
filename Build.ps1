param([switch]$Install, [switch]$SkipAssets, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $dependencies = Get-Content -Raw -LiteralPath 'tools/dependencies.json' | ConvertFrom-Json
    New-Item -ItemType Directory -Force -Path '.tools/downloads','dist/Spire408' | Out-Null
    foreach ($dependency in $dependencies.PSObject.Properties.Value) {
        $archive = Join-Path '.tools/downloads' $dependency.file
        if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest $dependency.url -OutFile $archive }
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $dependency.sha256) { throw "Dependency checksum failed: $archive" }
        $directory = Join-Path '.tools' $dependency.directory
        if (!(Test-Path -LiteralPath $directory)) { Expand-Archive -LiteralPath $archive -DestinationPath $directory }
    }
    if (!$SkipAssets) {
        python -X utf8 tools/import_questions.py
        if ($LASTEXITCODE -ne 0) { throw 'Question import failed' }
        if (!(Test-Path -LiteralPath 'tools/node_modules')) {
            Push-Location tools
            try { npm ci --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' } } finally { Pop-Location }
        }
        node tools/render_questions.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Question rendering failed; inspect reports/render-report.json' }
    }
    node tools/build_art.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Relic artwork validation failed' }
    python tools/ai/build_corpus.py
    if ($LASTEXITCODE -ne 0) { throw 'Essay bank generation failed' }
    python tools/import_essays.py
    if ($LASTEXITCODE -ne 0) { throw 'Pinned essay import failed' }
    python tools/build_essays.py
    if ($LASTEXITCODE -ne 0) { throw 'Essay rubric validation failed' }
    node tools/render_essays.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Essay presentation generation failed' }
    python -X utf8 tools/audit_question_quality.py
    if ($LASTEXITCODE -ne 0) { throw 'Choice question quality policy or source correspondence failed' }
    python -X utf8 tools/audit_essay_grading.py
    if ($LASTEXITCODE -ne 0) { throw 'Essay grading scope or errata audit failed' }
    python tools/verify_assets.py
    if ($LASTEXITCODE -ne 0) { throw 'Packaged asset validation failed' }
    if (!(Test-Path -LiteralPath 'models/wakuu/wakuu.onnx')) { throw 'Offline Wakuu model missing. Run PrepareWakuu.ps1 first.' }
    $modelManifest = Get-Content -Raw -LiteralPath 'models/wakuu/model.json' | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath 'models/wakuu/wakuu.onnx' -Algorithm SHA256).Hash -ne $modelManifest.sha256 -or
        (Get-Item -LiteralPath 'models/wakuu/wakuu.onnx').Length -ne $modelManifest.bytes) {
        throw 'Wakuu model size or checksum differs from its manifest; package was not updated.'
    }
    dotnet build game/Spire408.csproj -c Release --nologo -p:EnableSmoke=$($Smoke.IsPresent.ToString().ToLowerInvariant())
    if ($LASTEXITCODE -ne 0) { throw 'Mod compilation failed' }
    $godot = (Get-ChildItem -LiteralPath '.tools/godot' -Recurse -Filter '*console.exe' | Select-Object -First 1).FullName
    $packScript = Join-Path $PSScriptRoot 'tools/pack.gd'
    $packOutput = Join-Path $PSScriptRoot 'dist/Spire408/Spire408.pck'
    & $godot --headless --path game --script $packScript -- --output=$packOutput
    if ($LASTEXITCODE -ne 0) { throw 'PCK packaging failed' }
    Copy-Item -LiteralPath 'game/.godot/mono/temp/bin/Release/Spire408.dll','game/mod_manifest.json' -Destination 'dist/Spire408'
    Copy-Item -LiteralPath 'game/.godot/mono/temp/bin/Release/Microsoft.ML.OnnxRuntime.dll','game/.godot/mono/temp/bin/Release/System.Numerics.Tensors.dll' -Destination 'dist/Spire408'
    $onnxNative = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.ml.onnxruntime/1.27.1/runtimes/win-x64/native'
    Copy-Item -LiteralPath (Join-Path $onnxNative 'onnxruntime.dll'),(Join-Path $onnxNative 'onnxruntime_providers_shared.dll') -Destination 'dist/Spire408'
    New-Item -ItemType Directory -Force -Path 'dist/Spire408/Models/wakuu' | Out-Null
    Copy-Item -LiteralPath 'models/wakuu/wakuu.onnx','models/wakuu/model.json','models/wakuu/vocab.txt','models/wakuu/LICENSE.txt','models/wakuu/NOTICE.txt' -Destination 'dist/Spire408/Models/wakuu'
    Copy-Item -LiteralPath 'tools/ai/ONNX-RUNTIME-LICENSE.txt' -Destination 'dist/Spire408'
    Copy-Item -LiteralPath 'tools/ai/ONNX-THIRD-PARTY-NOTICES.txt' -Destination 'dist/Spire408'
    Copy-Item -LiteralPath 'data/UPSTREAM-LICENSE.txt' -Destination 'dist/Spire408'
    Copy-Item -LiteralPath 'data/CS408-ESSAYS-LICENSE.txt','data/CS408-ESSAYS-README.md' -Destination 'dist/Spire408'
    Copy-Item -LiteralPath 'docs/PLAYTEST.md','docs/WAKUU.md','docs/WAKUU-FULL.md','docs/ESSAY-IMPORT.md','docs/QUESTION-QUALITY.md','docs/ACADEMIC-AUDIT.md','LICENSE','THIRD_PARTY_NOTICES.md' -Destination 'dist/Spire408'
    $packageBytes = (Get-ChildItem -LiteralPath 'dist/Spire408' -File -Recurse | Measure-Object -Property Length -Sum).Sum
    if ($packageBytes -gt 500000000) { throw "Installed payload exceeds 500 MB: $packageBytes bytes" }
    $releaseVersion = (Get-Content -Raw 'game/mod_manifest.json' | ConvertFrom-Json).version
    if (!$Smoke) { Compress-Archive -LiteralPath 'dist/Spire408' -DestinationPath "dist/Spire408-$releaseVersion.zip" -Force }
    if ($Install) {
        $modRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'mods'
        $ritsuTarget = Join-Path $modRoot 'STS2-RitsuLib'
        if (Test-Path -LiteralPath $ritsuTarget) {
            $manifest = Get-Content -LiteralPath (Join-Path $ritsuTarget 'mod_manifest.json') -Raw | ConvertFrom-Json
            if ($manifest.version -ne '0.6.9') { throw "Existing RitsuLib version $($manifest.version) differs; existing dependency was preserved." }
        } else {
            New-Item -ItemType Directory -Force -Path $ritsuTarget | Out-Null
            Get-ChildItem -LiteralPath '.tools/ritsulib' | Copy-Item -Destination $ritsuTarget -Recurse
        }
        $target = Join-Path $modRoot 'Spire408'
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        $sourceRoot = (Resolve-Path 'dist/Spire408').Path
        $backupRoot = Join-Path $PSScriptRoot ('.tools/install-backups/' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
        foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse) {
            $relative = [System.IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
            $destination = Join-Path $target $relative
            if ((Test-Path -LiteralPath $destination) -and
                (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { continue }
            New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
            $staged = $destination + '.spire408-pending'
            Copy-Item -LiteralPath $file.FullName -Destination $staged -Force
            if (Test-Path -LiteralPath $destination) {
                $backup = Join-Path $backupRoot $relative
                New-Item -ItemType Directory -Force -Path (Split-Path $backup -Parent) | Out-Null
                # Keep loaded DLL/PCK file handles on the old file instead of truncating it.
                [System.IO.File]::Replace($staged, $destination, $backup)
            } else { [System.IO.File]::Move($staged, $destination) }
        }
        Write-Output "Installed to $target"
    }
    Write-Output "Package ready: $PSScriptRoot\dist\Spire408-$releaseVersion.zip"
} finally { Pop-Location }
