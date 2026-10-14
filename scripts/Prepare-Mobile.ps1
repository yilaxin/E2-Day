$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path -Parent $PSScriptRoot
$taskSource = Join-Path $taskProjectRoot 'src/DailyRecord.Mobile'
$taskDestination = Join-Path $taskProjectRoot 'docs/mobile'
New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
foreach ($taskAsset in @('index.html','app.js','app.css','manifest.webmanifest','sw.js','icon.svg','icon-192.png','icon-512.png','lunar-dates.json')) {
    $taskPath = Join-Path $taskSource $taskAsset
    if (!(Test-Path -LiteralPath $taskPath -PathType Leaf)) { throw "手机应用缺少 $taskAsset" }
    Copy-Item -LiteralPath $taskPath -Destination (Join-Path $taskDestination $taskAsset) -Force
}
[IO.File]::WriteAllText((Join-Path $taskProjectRoot 'docs/.nojekyll'), '')
Write-Output '独立手机应用已准备到 docs/mobile，仅包含静态程序和图标。'
