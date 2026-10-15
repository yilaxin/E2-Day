param([string]$Version = '', [string]$ReleaseDirectory = '', [string]$HarmonyPackageDirectory = '')
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$appProject = [xml](Get-Content -LiteralPath (Join-Path $projectDirectory 'src/DailyRecord.App/DailyRecord.App.csproj') -Raw -Encoding UTF8)
$appVersion = [string]$appProject.Project.PropertyGroup.Version
if (-not $Version) { $Version = $appVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -ne $appVersion) { throw '发布版本与 App 项目版本不一致。' }
$releaseDirectory = if ($ReleaseDirectory) { [IO.Path]::GetFullPath((Join-Path $projectDirectory $ReleaseDirectory)) } else { Join-Path $projectDirectory "artifacts/releases/v$Version" }
if (!$releaseDirectory.StartsWith($projectDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '发布目录必须位于项目内。' }
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$checksumLines = @()
foreach ($runtimeId in @('win-arm64','win-x64')) {
    $programDirectory = Join-Path $releaseDirectory "programs/E2-Day-$runtimeId"
    $executablePath = Join-Path $programDirectory 'DailyRecord.exe'
    if (-not (Test-Path -LiteralPath $executablePath)) { throw "尚未构建 $runtimeId 发布程序。" }
    if ((Get-Item -LiteralPath $executablePath).VersionInfo.FileVersion -ne "$Version.0") { throw "$runtimeId 程序版本与发布版本不一致。" }
    foreach ($documentName in @('使用说明.md','THIRD_PARTY_NOTICES.md','LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $projectDirectory $documentName) -Destination $programDirectory -Force
    }
    $nativeGuide = Join-Path $projectDirectory 'docs/HARMONY-NATIVE.md'
    if (Test-Path -LiteralPath $nativeGuide) {
        New-Item -ItemType Directory -Path (Join-Path $programDirectory 'docs') -Force | Out-Null
        Copy-Item -LiteralPath $nativeGuide -Destination (Join-Path $programDirectory 'docs/HARMONY-NATIVE.md') -Force
    }
    $packageName = "E2-Day-v$Version-$runtimeId.zip"
    $packagePath = Join-Path $releaseDirectory $packageName
    $pendingPath = Join-Path $releaseDirectory ("E2-Day-$runtimeId-" + [Guid]::NewGuid().ToString('N') + '.zip')
    [System.IO.Compression.ZipFile]::CreateFromDirectory($programDirectory, $pendingPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($pendingPath)
    try {
        $entryNames = @($archive.Entries | ForEach-Object FullName)
        foreach ($required in @('DailyRecord.exe','DailyRecord.dll','DailyRecord.Core.dll','DailyRecord.deps.json','DailyRecord.runtimeconfig.json','e_sqlite3.dll','hostfxr.dll','coreclr.dll','PresentationFramework.dll','Mobile/index.html','Mobile/app.js','Mobile/app.css','Mobile/sw.js','Mobile/manifest.webmanifest','Mobile/icon-192.png','Mobile/icon-512.png','Mobile/lunar-dates.json','使用说明.md','LICENSE','THIRD_PARTY_NOTICES.md','Data/Holidays/2026.json','Data/Holidays/LICENSE')) {
            if ($required -notin $entryNames) { throw "$runtimeId 程序包缺少 $required。" }
        }
        if ($entryNames -contains 'DailyRecord.Sync.dll' -or $entryNames -contains 'Mobile/install.html' -or $entryNames -contains 'Microsoft.AspNetCore.Server.Kestrel.Core.dll') { throw "$runtimeId 仍包含已停用的局域网同步程序。" }
        if ($entryNames | Where-Object { $_ -match '\.(db|log|pfx|p12|key|pdb|protected)$|backup|accounts\.json|session\.lock|(^|/)(\.build|previews|Accounts|LocalSync)(/|$)' }) { throw "$runtimeId 程序包包含非发布文件。" }
        foreach ($binaryName in @('DailyRecord.exe','e_sqlite3.dll')) {
            $binaryStream = $archive.GetEntry($binaryName).Open()
            $binaryMemory = [System.IO.MemoryStream]::new()
            try { $binaryStream.CopyTo($binaryMemory); $binaryBytes = $binaryMemory.ToArray() }
            finally { $binaryStream.Dispose(); $binaryMemory.Dispose() }
            $peOffset = [BitConverter]::ToInt32($binaryBytes,0x3c)
            $machine = [BitConverter]::ToUInt16($binaryBytes,$peOffset+4)
            $expectedMachine = if ($runtimeId -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
            if ($machine -ne $expectedMachine) { throw "$runtimeId 的 $binaryName 架构错误。" }
        }
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $pendingPath -Destination $packagePath -Force
    $fileHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLines += "$fileHash  $packageName"
    Write-Output "准备完成：$packageName；版本、架构和包内文件检查通过。"
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
