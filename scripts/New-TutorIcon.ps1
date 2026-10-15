$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# Keep the original E²-Day logo byte-for-byte; do not add a tutor badge.
Copy-Item -LiteralPath (Join-Path $root 'src/DailyRecord.App/Assets/e2-day-logo.png') -Destination (Join-Path $root 'src/DailyRecord.Tutor/Assets/e2-day-logo.png') -Force
Copy-Item -LiteralPath (Join-Path $root 'src/DailyRecord.App/Assets/e2-day.ico') -Destination (Join-Path $root 'src/DailyRecord.Tutor/Assets/e2-day-tutor.ico') -Force
