param(
    [ValidateSet('win-arm64', 'win-x64')]
    [string]$RuntimeIdentifier = 'win-arm64',
    [string]$ReleaseDirectory = '',
    [ValidateSet('Standard','Tutor')][string]$Edition = 'Standard'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Environment.ps1')
$projectPath = if ($Edition -eq 'Tutor') { 'src/DailyRecord.Tutor/DailyRecord.Tutor.csproj' } else { 'src/DailyRecord.App/DailyRecord.App.csproj' }
$productName = if ($Edition -eq 'Tutor') { 'E2-Day-Tutor' } else { 'E2-Day' }
$binaryName = if ($Edition -eq 'Tutor') { 'E2Day.Tutor' } else { 'DailyRecord' }
$appProject = [xml](Get-Content -LiteralPath (Join-Path $projectRoot $projectPath) -Raw -Encoding UTF8)
$appVersion = [string]$appProject.Project.PropertyGroup.Version
$releaseTag = if ($Edition -eq 'Tutor') { "tutor-v$appVersion" } else { "v$appVersion" }
$releaseDirectory = if ($ReleaseDirectory) { [IO.Path]::GetFullPath((Join-Path $projectRoot $ReleaseDirectory)) } else { Join-Path $projectRoot "artifacts/releases/$releaseTag" }
if (!$releaseDirectory.StartsWith($projectRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '发布目录必须位于项目内。' }
$publishDirectory = Join-Path $releaseDirectory "programs/$productName-$RuntimeIdentifier"
foreach ($fileName in @("$binaryName.exe","$binaryName.dll",'DailyRecord.Core.dll','DailyRecord.Sync.dll')) {
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
    dotnet publish $projectPath -c Release -r $RuntimeIdentifier --self-contained true -p:DebugSymbols=false -p:DebugType=None -o $publishDirectory --nologo -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
    foreach ($symbolName in @('DailyRecord.pdb', 'DailyRecord.Core.pdb', 'DailyRecord.Sync.pdb')) {
        $symbolPath = Join-Path $publishDirectory $symbolName
        if (Test-Path -LiteralPath $symbolPath) { Remove-Item -LiteralPath $symbolPath -Force }
    }
    $guide = if ($Edition -eq 'Tutor') { 'docs/TUTOR-GUIDE.md' } else { '使用说明.md' }
    Copy-Item -LiteralPath (Join-Path $projectRoot $guide) -Destination (Join-Path $publishDirectory '使用说明.md') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $publishDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishDirectory -Force
    if ($Edition -eq 'Tutor') {
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/E2-Day-Tutor-Guide.pdf') -Destination (Join-Path $publishDirectory '安装与使用教程.pdf') -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/TUTOR-START-HERE.txt') -Destination (Join-Path $publishDirectory '先读我.txt') -Force
        $imagesDirectory = Join-Path $publishDirectory 'images'; New-Item -ItemType Directory -Path $imagesDirectory -Force | Out-Null
        foreach ($imageName in @('tutor-today.png','tutor-calendar.png','tutor-student.png','tutor-income.png','tutor-editor.png','tutor-calendar-action.png')) {
            $imagePath = Join-Path $projectRoot "docs/images/$imageName"
            if (Test-Path -LiteralPath $imagePath) { Copy-Item -LiteralPath $imagePath -Destination $imagesDirectory -Force }
        }
    }
    $zipPath = Join-Path $releaseDirectory "$productName-v$appVersion-$RuntimeIdentifier.zip"
    $pendingZip = Join-Path $releaseDirectory ("E2-Day-$RuntimeIdentifier-" + [Guid]::NewGuid().ToString('N') + '.zip')
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, $pendingZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Move-Item -LiteralPath $pendingZip -Destination $zipPath -Force
    Write-Host "程序：$(Join-Path $publishDirectory "$binaryName.exe")"
    Write-Host "压缩包：$zipPath"
} finally { Pop-Location }
