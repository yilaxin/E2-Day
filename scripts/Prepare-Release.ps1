param([string]$Version = '', [string]$ReleaseDirectory = '')
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
[System.IO.File]::WriteAllText((Join-Path $releaseDirectory 'SHA256SUMS.txt'), ($checksumLines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "校验文件：$(Join-Path $releaseDirectory 'SHA256SUMS.txt')"
