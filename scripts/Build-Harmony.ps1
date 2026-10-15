param(
    [string]$StudioPath = '',
    [string]$CommandLineToolsPath = '',
    [string]$NodeExecutable = '',
    [string]$StageRoot = '',
    [ValidateSet('debug', 'release')][string]$BuildMode = 'debug'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $projectRoot 'src/DailyRecord.Harmony'
$cli = Join-Path $projectRoot '.build/harmony-cli/node_modules/@deveco/deveco-cli/cli.js'
if (!(Test-Path -LiteralPath $cli -PathType Leaf)) {
    throw '缺少本地 DevEco CLI，请按 docs/HARMONY-NATIVE.md 安装官方命令行入口。'
}
if ($StudioPath -and $CommandLineToolsPath) { throw 'StudioPath 与 CommandLineToolsPath 只填写一个。' }
$toolRoot = if ($StudioPath) { $StudioPath } elseif ($CommandLineToolsPath) { $CommandLineToolsPath } elseif ($env:DEVECO_CLI_STUDIO_PATH) { $env:DEVECO_CLI_STUDIO_PATH } elseif ($env:DEVECO_CLI_CLT_PATH) { $env:DEVECO_CLI_CLT_PATH } else { '' }
if (!$toolRoot -or !(Test-Path -LiteralPath $toolRoot -PathType Container)) {
    throw '尚未提供完整的华为官方工具链。请先安装 DevEco Studio 与 HarmonyOS SDK，再用 -StudioPath 指定目录；NPM 中的 CLI 不能代替 SDK。'
}
if (!$StageRoot) {
    $StageRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/Local/Temp/E2DayHarmonyBuild'
}
$StageRoot = [IO.Path]::GetFullPath($StageRoot)
if ($StageRoot -notmatch '^[A-Za-z]:\\[A-Za-z0-9 _./\\-]+$') {
    throw '华为工具链要求构建目录只使用英文、数字和常用路径字符。请用 -StageRoot 指定本地英文目录。'
}
# Always create a new directory. Never clear, reset, or replace an existing checkout.
$stage = Join-Path $StageRoot ('project-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -ErrorAction Stop | Out-Null
foreach ($directory in @('AppScope', 'hvigor', 'entry/src')) {
    $target = Join-Path $stage $directory
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $directory) -Destination $target -Recurse
}
foreach ($file in @('build-profile.json5', 'oh-package.json5', 'hvigorfile.ts', 'code-linter.json5', 'entry/build-profile.json5', 'entry/oh-package.json5', 'entry/hvigorfile.ts', 'entry/obfuscation-rules.txt')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination (Join-Path $stage $file)
}
if (!$NodeExecutable) {
    $bundledNode = Join-Path $toolRoot 'tools/node/node.exe'
    $NodeExecutable = if (Test-Path -LiteralPath $bundledNode -PathType Leaf) { $bundledNode } else { (Get-Command node -ErrorAction Stop).Source }
}
$names = @('DEVECO_CLI_STUDIO_PATH', 'DEVECO_CLI_CLT_PATH', 'DEVECO_CLI_DISABLE_TELEMETRY', 'DEVECO_CLI_SKIP_VERSION_CHECK', 'DEVECO_CLI_DISABLE_UPDATE', 'DEVECO_CLI_DATA_DIR', 'HVIGOR_USER_HOME', 'JAVA_HOME', 'PATH')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $stage
try {
    if ($StudioPath) { $env:DEVECO_CLI_STUDIO_PATH = [IO.Path]::GetFullPath($toolRoot); $env:DEVECO_CLI_CLT_PATH = $null }
    elseif ($CommandLineToolsPath) { $env:DEVECO_CLI_CLT_PATH = [IO.Path]::GetFullPath($toolRoot); $env:DEVECO_CLI_STUDIO_PATH = $null }
    $env:DEVECO_CLI_DISABLE_TELEMETRY = '1'
    $env:DEVECO_CLI_SKIP_VERSION_CHECK = '1'
    $env:DEVECO_CLI_DISABLE_UPDATE = '1'
    $env:DEVECO_CLI_DATA_DIR = Join-Path $StageRoot 'cli-data'
    $env:HVIGOR_USER_HOME = Join-Path $StageRoot 'hvigor-cache'
    $jbr = Join-Path $toolRoot 'jbr'
    if (Test-Path -LiteralPath (Join-Path $jbr 'bin/java.exe') -PathType Leaf) { $env:JAVA_HOME = $jbr }
    $env:PATH = (Split-Path $NodeExecutable -Parent) + [IO.Path]::PathSeparator + $env:PATH
    New-Item -ItemType Directory -Path $env:DEVECO_CLI_DATA_DIR -Force | Out-Null
    Write-Host "原生构建目录：$stage"
    & $NodeExecutable $cli build --modules entry --build-mode $BuildMode
    if ($LASTEXITCODE -ne 0) { throw "鸿蒙编译失败；诊断与构建目录保留在 $stage。未生成可交付包。" }
    $packages = @(Get-ChildItem -LiteralPath (Join-Path $stage 'entry/build') -Filter '*.hap' -File -Recurse)
    if (!$packages.Count) { throw '命令返回成功但没有找到 HAP，不能将此构建视为成功。' }
    $output = Join-Path $projectRoot ('artifacts/harmony/' + (Split-Path $stage -Leaf))
    New-Item -ItemType Directory -Path $output | Out-Null
    foreach ($package in $packages) { Copy-Item -LiteralPath $package.FullName -Destination $output }
    $hashes = Get-ChildItem -LiteralPath $output -Filter '*.hap' -File | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
    $hashes | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8
    Write-Host "HAP 与校验文件：$output"
    Write-Host '当前源码不含个人签名配置。产物仍须核验签名并完成真机安装测试，不能仅凭生成 HAP 宣称交付完成。'
} finally {
    Pop-Location
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
