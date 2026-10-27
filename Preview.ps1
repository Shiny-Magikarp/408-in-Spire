param([switch]$Editor, [switch]$Quiz, [switch]$Essay)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $godot = Get-ChildItem -LiteralPath '.tools/godot' -Recurse -Filter '*console.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (!$godot -or !(Test-Path 'game/Spire408/Rendered/presentation.json')) { throw 'Run ./Build.ps1 first.' }
    $previousEditorProject = $env:SPIRE408_EDITOR_PROJECT
    try {
        $env:SPIRE408_EDITOR_PROJECT = (Resolve-Path 'game').Path
        $prepareScript = (Resolve-Path 'tools/prepare_editor.gd').Path
        $prepareProcess = Start-Process -FilePath (Resolve-Path '../SlayTheSpire2.exe').Path -ArgumentList @('--headless','--script',('"' + $prepareScript + '"'),'--log-file','408InSpire/reports/editor-assets.log') -WorkingDirectory (Resolve-Path '..') -WindowStyle Hidden -PassThru -Wait
        if ($prepareProcess.ExitCode -ne 0) { throw 'Native editor asset preparation failed.' }
    } finally { $env:SPIRE408_EDITOR_PROJECT = $previousEditorProject }
    dotnet build game/Spire408.csproj -c Debug -p:PreviewBuild=true --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Preview compilation failed' }
    if ($Editor) { & $godot.FullName --editor --path game }
    elseif ($Essay) { & $godot.FullName --path game -- --essay }
    elseif ($Quiz) { & $godot.FullName --path game -- --quiz }
    else { & $godot.FullName --path game }
} finally { Pop-Location }
