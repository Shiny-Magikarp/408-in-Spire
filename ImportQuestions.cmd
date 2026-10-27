@echo off
chcp 65001 >nul
python "%~dp0tools\import_questions.py" %*
if errorlevel 1 (
  echo Import failed. See the output above and reports/import-report.json.
) else (
  echo Import complete. Output: data/questions.json
)
pause
