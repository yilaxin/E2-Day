param([string]$Version = '', [string]$ReleaseDirectory = '', [string]$HarmonyPackageDirectory = '', [ValidateSet('Standard','Tutor')][string]$Edition = 'Standard')
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$projectPath = if ($Edition -eq 'Tutor') { 'src/DailyRecord.Tutor/DailyRecord.Tutor.csproj' } else { 'src/DailyRecord.App/DailyRecord.App.csproj' }
$productName = if ($Edition -eq 'Tutor') { 'E2-Day-Tutor' } else { 'E2-Day' }
$binaryName = if ($Edition -eq 'Tutor') { 'E2Day.Tutor' } else { 'DailyRecord' }
if ($Edition -eq 'Tutor' -and $HarmonyPackageDirectory) { throw '家教版当前仅发布 Windows。' }
$appProject = [xml](Get-Content -LiteralPath (Join-Path $projectDirectory $projectPath) -Raw -Encoding UTF8)
$appVersion = [string]$appProject.Project.PropertyGroup.Version
if (-not $Version) { $Version = $appVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -ne $appVersion) { throw '发布版本与 App 项目版本不一致。' }
$releaseTag = if ($Edition -eq 'Tutor') { "tutor-v$Version" } else { "v$Version" }
$releaseDirectory = if ($ReleaseDirectory) { [IO.Path]::GetFullPath((Join-Path $projectDirectory $ReleaseDirectory)) } else { Join-Path $projectDirectory "artifacts/releases/$releaseTag" }
if (!$releaseDirectory.StartsWith($projectDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '发布目录必须位于项目内。' }
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$checksumLines = @()
foreach ($runtimeId in @('win-arm64','win-x64')) {
    $programDirectory = Join-Path $releaseDirectory "programs/$productName-$runtimeId"
    $executablePath = Join-Path $programDirectory "$binaryName.exe"
    if (-not (Test-Path -LiteralPath $executablePath)) { throw "尚未构建 $runtimeId 发布程序。" }
    if ((Get-Item -LiteralPath $executablePath).VersionInfo.FileVersion -ne "$Version.0") { throw "$runtimeId 程序版本与发布版本不一致。" }
    foreach ($documentName in @('THIRD_PARTY_NOTICES.md','LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $projectDirectory $documentName) -Destination $programDirectory -Force
    }
    $guide = if ($Edition -eq 'Tutor') { 'docs/TUTOR-GUIDE.md' } else { '使用说明.md' }
    Copy-Item -LiteralPath (Join-Path $projectDirectory $guide) -Destination (Join-Path $programDirectory '使用说明.md') -Force
    if ($Edition -eq 'Tutor') {
        Copy-Item -LiteralPath (Join-Path $projectDirectory 'docs/E2-Day-Tutor-Guide.pdf') -Destination (Join-Path $programDirectory '安装与使用教程.pdf') -Force
        Copy-Item -LiteralPath (Join-Path $projectDirectory 'docs/TUTOR-START-HERE.txt') -Destination (Join-Path $programDirectory '先读我.txt') -Force
        $imagesDirectory = Join-Path $programDirectory 'images'; New-Item -ItemType Directory -Path $imagesDirectory -Force | Out-Null
        foreach ($imageName in @('tutor-today.png','tutor-calendar.png','tutor-student.png','tutor-income.png','tutor-editor.png','tutor-calendar-action.png','tutor-student-edit.png','tutor-student-learning.png','tutor-dashboard.png','tutor-dashboard-details.png','tutor-dashboard-year.png')) {
            Copy-Item -LiteralPath (Join-Path $projectDirectory "docs/images/$imageName") -Destination $imagesDirectory -Force
        }
    }
    $nativeGuide = Join-Path $projectDirectory 'docs/HARMONY-NATIVE.md'
    if ($Edition -eq 'Standard' -and (Test-Path -LiteralPath $nativeGuide)) {
        New-Item -ItemType Directory -Path (Join-Path $programDirectory 'docs') -Force | Out-Null
        Copy-Item -LiteralPath $nativeGuide -Destination (Join-Path $programDirectory 'docs/HARMONY-NATIVE.md') -Force
    }
    $packageName = "$productName-v$Version-$runtimeId.zip"
    $packagePath = Join-Path $releaseDirectory $packageName
    $pendingPath = Join-Path $releaseDirectory ("E2-Day-$runtimeId-" + [Guid]::NewGuid().ToString('N') + '.zip')
    [System.IO.Compression.ZipFile]::CreateFromDirectory($programDirectory, $pendingPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($pendingPath)
    try {
        $entryNames = @($archive.Entries | ForEach-Object FullName)
        $requiredFiles = @("$binaryName.exe","$binaryName.dll",'DailyRecord.Core.dll',"$binaryName.deps.json","$binaryName.runtimeconfig.json",'e_sqlite3.dll','hostfxr.dll','coreclr.dll','PresentationFramework.dll','使用说明.md','LICENSE','THIRD_PARTY_NOTICES.md','Data/Holidays/2026.json','Data/Holidays/LICENSE')
        if ($Edition -eq 'Standard') { $requiredFiles += @('Mobile/index.html','Mobile/app.js','Mobile/app.css','Mobile/sw.js','Mobile/manifest.webmanifest','Mobile/icon-192.png','Mobile/icon-512.png','Mobile/lunar-dates.json') }
        else { $requiredFiles += @('先读我.txt','安装与使用教程.pdf','e2-day-tutor.ico','images/tutor-calendar.png','images/tutor-editor.png','images/tutor-income.png','images/tutor-student.png','images/tutor-today.png','images/tutor-calendar-action.png','images/tutor-student-edit.png','images/tutor-student-learning.png','images/tutor-dashboard.png','images/tutor-dashboard-details.png','images/tutor-dashboard-year.png'); if ($entryNames -contains 'DailyRecord.exe' -or $entryNames -contains 'Mobile/index.html') { throw '家教版包中混入日常版程序或手机入口。' } }
        foreach ($required in $requiredFiles) {
            if ($required -notin $entryNames) { throw "$runtimeId 程序包缺少 $required。" }
        }
        if ($entryNames -contains 'DailyRecord.Sync.dll' -or $entryNames -contains 'Mobile/install.html' -or $entryNames -contains 'Microsoft.AspNetCore.Server.Kestrel.Core.dll') { throw "$runtimeId 仍包含已停用的局域网同步程序。" }
        if ($entryNames | Where-Object { $_ -match '\.(db|log|pfx|p12|key|pdb|protected)$|backup|accounts\.json|session\.lock|(^|/)(\.build|previews|Accounts|LocalSync)(/|$)' }) { throw "$runtimeId 程序包包含非发布文件。" }
        foreach ($checkedBinary in @("$binaryName.exe",'e_sqlite3.dll')) {
            $binaryStream = $archive.GetEntry($checkedBinary).Open()
            $binaryMemory = [System.IO.MemoryStream]::new()
            try { $binaryStream.CopyTo($binaryMemory); $binaryBytes = $binaryMemory.ToArray() }
            finally { $binaryStream.Dispose(); $binaryMemory.Dispose() }
            $peOffset = [BitConverter]::ToInt32($binaryBytes,0x3c)
            $machine = [BitConverter]::ToUInt16($binaryBytes,$peOffset+4)
            $expectedMachine = if ($runtimeId -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
            if ($machine -ne $expectedMachine) { throw "$runtimeId 的 $checkedBinary 架构错误。" }
        }
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $pendingPath -Destination $packagePath -Force
    $fileHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLines += "$fileHash  $packageName"
    Write-Output "准备完成：$packageName；版本、架构和包内文件检查通过。"
}
if ($Edition -eq 'Tutor') {
    $pdfName="E2-Day-Tutor-Guide-v$Version.pdf"
    $pdfPath=Join-Path $releaseDirectory $pdfName
    Copy-Item -LiteralPath (Join-Path $projectDirectory 'docs/E2-Day-Tutor-Guide.pdf') -Destination $pdfPath -Force
    $checksumLines += ((Get-FileHash -LiteralPath $pdfPath -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$pdfName)
}
if ($HarmonyPackageDirectory) {
    $nativeDirectory = [IO.Path]::GetFullPath((Join-Path $projectDirectory $HarmonyPackageDirectory))
    if (!$nativeDirectory.StartsWith($projectDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '鸿蒙产物目录必须位于项目内。' }
    & (Join-Path $projectDirectory 'tests/harmony-package-check.ps1') -PackageDirectory $nativeDirectory
    $nativeHap = Join-Path $nativeDirectory 'entry-default-unsigned.hap'
    if (!(Test-Path -LiteralPath $nativeHap -PathType Leaf)) { throw '只能公开明确的未签名 HAP；个人签名包留在本地。' }
    $nativeStage = Join-Path $releaseDirectory ('harmony-unsigned-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $nativeStage | Out-Null
    Copy-Item -LiteralPath $nativeHap -Destination $nativeStage
    Copy-Item -LiteralPath (Join-Path $nativeDirectory 'SHA256SUMS.txt') -Destination $nativeStage
    Copy-Item -LiteralPath (Join-Path $projectDirectory 'docs/HARMONY-NATIVE.md') -Destination $nativeStage
    Copy-Item -LiteralPath (Join-Path $projectDirectory 'LICENSE') -Destination $nativeStage
    Copy-Item -LiteralPath (Join-Path $projectDirectory 'THIRD_PARTY_NOTICES.md') -Destination $nativeStage
    Copy-Item -LiteralPath (Join-Path $projectDirectory 'src/DailyRecord.Harmony/LICENSE-TEMPLATE.txt') -Destination $nativeStage
    [IO.File]::WriteAllText((Join-Path $nativeStage 'README.txt'), "E²-Day 鸿蒙原生未签名构建`n此 HAP 不能直接点击安装。请使用自己的华为开发者账号与设备签名。`n源码与签名说明：https://github.com/yilaxin/E2-Day/tree/v$Version/src/DailyRecord.Harmony`n详见 HARMONY-NATIVE.md。个人设备签名包、证书、profile 和密钥不公开。`n", [Text.UTF8Encoding]::new($false))
    $nativeName = "E2-Day-v$Version-harmony-unsigned.zip"
    $nativeZip = Join-Path $releaseDirectory $nativeName
    if (Test-Path -LiteralPath $nativeZip) { throw '已有鸿蒙 ZIP，保留原文件。请使用新的准备目录。' }
    [IO.Compression.ZipFile]::CreateFromDirectory($nativeStage, $nativeZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $checksumLines += ((Get-FileHash -LiteralPath $nativeZip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $nativeName)
    Write-Output "准备完成：$nativeName；只含未签名 HAP、校验与公开说明。"
}
[System.IO.File]::WriteAllText((Join-Path $releaseDirectory 'SHA256SUMS.txt'), ($checksumLines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "校验文件：$(Join-Path $releaseDirectory 'SHA256SUMS.txt')"
