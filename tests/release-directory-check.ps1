$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
$taskRelativeRoot = '.build/publish-lock-' + [Guid]::NewGuid().ToString('N')
$taskDirectory = Join-Path $taskProject "$taskRelativeRoot/programs/E2-Day-win-arm64"
New-Item -ItemType Directory -Path $taskDirectory -Force | Out-Null
$taskDll = Join-Path $taskDirectory 'DailyRecord.dll'
[IO.File]::WriteAllBytes($taskDll, [byte[]](1,2,3,4))
$taskLock = [IO.File]::Open($taskDll, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $taskRejected = $false
    try { & (Join-Path $taskProject 'scripts/Publish.ps1') -ReleaseDirectory $taskRelativeRoot }
    catch { $taskRejected = $_.Exception.Message -like '目标程序仍在使用*' }
    if (!$taskRejected) { throw '发布工具没有在修改文件前拒绝被占用的目标。' }
} finally { $taskLock.Dispose() }
if ([Convert]::ToHexString([IO.File]::ReadAllBytes($taskDll)) -ne '01020304') { throw '发布检查改变了被占用的文件。' }
if (Get-ChildItem -LiteralPath (Join-Path $taskProject $taskRelativeRoot) -Filter '*.zip' -File) { throw '发布检查意外产生程序包。' }
$taskOutsideRejected = $false
try { & (Join-Path $taskProject 'scripts/Publish.ps1') -ReleaseDirectory '../outside-release-check' }
catch { $taskOutsideRejected = $_.Exception.Message -eq '发布目录必须位于项目内。' }
if (!$taskOutsideRejected) { throw '发布工具没有拒绝项目外的目标。' }
Write-Output 'PASS publishing rejects locked programs before writing and rejects output outside the project'
