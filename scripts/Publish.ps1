param(
    [ValidateSet('win-arm64', 'win-x64')]
    [string]$RuntimeIdentifier = 'win-arm64',
    [string]$ReleaseDirectory = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Environment.ps1')
$appProject = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src/DailyRecord.App/DailyRecord.App.csproj') -Raw -Encoding UTF8)
$appVersion = [string]$appProject.Project.PropertyGroup.Version
$releaseDirectory = if ($ReleaseDirectory) { [IO.Path]::GetFullPath((Join-Path $projectRoot $ReleaseDirectory)) } else { Join-Path $projectRoot "artifacts/releases/v$appVersion" }
if (!$releaseDirectory.StartsWith($projectRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '发布目录必须位于项目内。' }
$publishDirectory = Join-Path $releaseDirectory "programs/E2-Day-$RuntimeIdentifier"
foreach ($fileName in @('DailyRecord.exe','DailyRecord.dll','DailyRecord.Core.dll','DailyRecord.Sync.dll')) {
    $targetPath = Join-Path $publishDirectory $fileName
    if (Test-Path -LiteralPath $targetPath) {
        try { $targetHandle = [IO.File]::Open($targetPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $targetHandle.Dispose() }
        catch { throw '目标程序仍在使用，未修改程序文件。请关闭程序，或使用 -ReleaseDirectory 指定独立候选目录。' }
    }
}
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Push-Location $projectRoot
try {
    & (Join-Path $PSScriptRoot 'Get-Dependencies.ps1') -IncludeRuntime -RuntimeIdentifier $RuntimeIdentifier
    dotnet publish src/DailyRecord.App/DailyRecord.App.csproj -c Release -r $RuntimeIdentifier --self-contained true -p:DebugSymbols=false -p:DebugType=None -o $publishDirectory --nologo -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
    foreach ($symbolName in @('DailyRecord.pdb', 'DailyRecord.Core.pdb', 'DailyRecord.Sync.pdb')) {
        $symbolPath = Join-Path $publishDirectory $symbolName
        if (Test-Path -LiteralPath $symbolPath) { Remove-Item -LiteralPath $symbolPath -Force }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot '使用说明.md') -Destination $publishDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $publishDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishDirectory -Force
    $zipPath = Join-Path $releaseDirectory "E2-Day-v$appVersion-$RuntimeIdentifier.zip"
    $pendingZip = Join-Path $releaseDirectory ("E2-Day-$RuntimeIdentifier-" + [Guid]::NewGuid().ToString('N') + '.zip')
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, $pendingZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Move-Item -LiteralPath $pendingZip -Destination $zipPath -Force
    Write-Host "程序：$(Join-Path $publishDirectory 'DailyRecord.exe')"
    Write-Host "压缩包：$zipPath"
} finally { Pop-Location }
