$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appName = 'E' + [char]0x00B2 + '-Day'
$publishDirectory = Join-Path $projectRoot 'artifacts\E2-Day-win-arm64'
$executable = Join-Path $publishDirectory 'DailyRecord.exe'
$iconPath = Join-Path $publishDirectory 'e2-day.ico'
foreach ($requiredPath in @($executable, $iconPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "请先运行 Publish.ps1，缺少文件：$requiredPath"
    }
}

$desktopDirectory = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktopDirectory ($appName + '.lnk')
$legacyPaths = @((Join-Path $desktopDirectory '每日记录.lnk'), (Join-Path $desktopDirectory 'E2-Day.lnk'))
$legacyExecutable = Join-Path $projectRoot 'artifacts\每日记录-win-arm64\DailyRecord.exe'
$superscriptExecutable = Join-Path $projectRoot ('artifacts\' + $appName + '-win-arm64\DailyRecord.exe')
if (-not ('E2Day.Desktop.ShellLink' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'ShellLink.cs')
}
function Test-ProjectTarget([string]$target) {
    foreach ($knownTarget in @($executable, $legacyExecutable, $superscriptExecutable)) {
        if ([string]::Equals($target, $knownTarget, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}
$arguments = ''
if (Test-Path -LiteralPath $shortcutPath) {
    $existing = [E2Day.Desktop.ShellLink]::Read($shortcutPath)
    if (-not (Test-ProjectTarget $existing.TargetPath)) {
        throw "桌面已有同名快捷方式，但指向其它程序，未修改：$shortcutPath"
    }
    $arguments = $existing.Arguments
}

$replaceLegacyPaths = @()
foreach ($legacyPath in $legacyPaths) {
    if (-not (Test-Path -LiteralPath $legacyPath)) { continue }
    $legacy = [E2Day.Desktop.ShellLink]::Read($legacyPath)
    if (Test-ProjectTarget $legacy.TargetPath) {
        $backupDirectory = Join-Path $projectRoot 'artifacts\desktop-shortcut-backup'
        New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
        $backupPath = Join-Path $backupDirectory (([System.IO.Path]::GetFileNameWithoutExtension($legacyPath)) + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.lnk')
        Copy-Item -LiteralPath $legacyPath -Destination $backupPath
        $replaceLegacyPaths += $legacyPath
        if (-not (Test-Path -LiteralPath $shortcutPath)) { $arguments = $legacy.Arguments }
    }
}

$description = $appName + ' · Explore Everyday Day · 探索每一天'
[E2Day.Desktop.ShellLink]::Save($shortcutPath, $executable, $arguments, $publishDirectory, $description, $iconPath)

$saved = [E2Day.Desktop.ShellLink]::Read($shortcutPath)
if (-not [string]::Equals($saved.TargetPath, $executable, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($saved.IconPath, $iconPath, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($saved.Arguments, $arguments, [StringComparison]::Ordinal) -or
    -not [string]::Equals($saved.Description, $description, [StringComparison]::Ordinal) -or
    -not [System.IO.File]::Exists($saved.TargetPath) -or -not [System.IO.File]::Exists($saved.IconPath)) {
    throw '快捷方式验证失败，旧快捷方式已保留。'
}
foreach ($legacyPath in $replaceLegacyPaths) { Remove-Item -LiteralPath $legacyPath }

[E2Day.Desktop.ShellLink]::Refresh()
Write-Host "桌面快捷方式已更新：$shortcutPath"
$saved | Format-List
