param(
    [string]$ReleaseDirectory = '',
    [ValidateSet('win-arm64','win-x64')][string]$RuntimeIdentifier = 'win-arm64',
    [string]$ShortcutName = 'E²-Day 2.0'
)
$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path -Parent $PSScriptRoot
$taskProject = [xml](Get-Content -LiteralPath (Join-Path $taskProjectRoot 'src/DailyRecord.App/DailyRecord.App.csproj') -Raw -Encoding UTF8)
$taskVersion = [string]$taskProject.Project.PropertyGroup.Version
$taskRelease = if ($ReleaseDirectory) { [IO.Path]::GetFullPath((Join-Path $taskProjectRoot $ReleaseDirectory)) } else { Join-Path $taskProjectRoot "artifacts/releases/v$taskVersion" }
if (!$taskRelease.StartsWith($taskProjectRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '快捷方式目标必须位于项目内。' }
if (!$ShortcutName -or $ShortcutName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw '快捷方式名称无效。' }
$taskProgram = Join-Path $taskRelease "programs/E2-Day-$RuntimeIdentifier"
$taskExecutable = Join-Path $taskProgram 'DailyRecord.exe'
$taskIcon = Join-Path $taskProgram 'e2-day.ico'
foreach ($taskFile in @($taskExecutable,$taskIcon)) { if (!(Test-Path -LiteralPath $taskFile -PathType Leaf)) { throw "缺少程序文件：$taskFile" } }
if ((Get-Item -LiteralPath $taskExecutable).VersionInfo.FileVersion -ne "$taskVersion.0") { throw '程序版本不匹配。' }
if (!('E2Day.Desktop.ShellLink' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'ShellLink.cs') }
$taskShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) ($ShortcutName + '.lnk')
if (Test-Path -LiteralPath $taskShortcut) {
    $taskOld = [E2Day.Desktop.ShellLink]::Read($taskShortcut)
    if (!$taskOld.TargetPath.StartsWith($taskProjectRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($taskOld.TargetPath) -ne 'DailyRecord.exe') { throw '同名快捷方式指向其他程序，已保留。' }
    $taskBackupDirectory = Join-Path $taskProjectRoot 'artifacts/desktop-shortcut-backup'
    New-Item -ItemType Directory -Path $taskBackupDirectory -Force | Out-Null
    Copy-Item -LiteralPath $taskShortcut -Destination (Join-Path $taskBackupDirectory ($ShortcutName + '-' + [Guid]::NewGuid().ToString('N') + '.lnk'))
}
[E2Day.Desktop.ShellLink]::Save($taskShortcut,$taskExecutable,'',$taskProgram,'E²-Day · 独立手机与文件交换',$taskIcon)
$taskSaved = [E2Day.Desktop.ShellLink]::Read($taskShortcut)
if ($taskSaved.TargetPath -ne $taskExecutable -or $taskSaved.Arguments -ne '' -or $taskSaved.WorkingDirectory -ne $taskProgram -or $taskSaved.IconPath -ne $taskIcon) { throw '快捷方式回读验证失败，旧快捷方式已备份。' }
[E2Day.Desktop.ShellLink]::Refresh()
Write-Output "桌面快捷方式已验证：$taskShortcut -> $($taskSaved.TargetPath)"
