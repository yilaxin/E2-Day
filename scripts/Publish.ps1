param(
    [ValidateSet('win-arm64', 'win-x64')]
    [string]$RuntimeIdentifier = 'win-arm64'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Environment.ps1')
Push-Location $projectRoot
try {
    & (Join-Path $PSScriptRoot 'Get-Dependencies.ps1') -IncludeRuntime -RuntimeIdentifier $RuntimeIdentifier
    $publishDirectory = Join-Path $projectRoot "artifacts\E2-Day-$RuntimeIdentifier"
    dotnet publish src/DailyRecord.App/DailyRecord.App.csproj -c Release -r $RuntimeIdentifier --self-contained true -p:DebugSymbols=false -p:DebugType=None -o $publishDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
    foreach ($symbolName in @('DailyRecord.pdb', 'DailyRecord.Core.pdb')) {
        $symbolPath = Join-Path $publishDirectory $symbolName
        if (Test-Path -LiteralPath $symbolPath) { Remove-Item -LiteralPath $symbolPath -Force }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot '使用说明.md') -Destination $publishDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $publishDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishDirectory -Force
    $zipPath = Join-Path $projectRoot "artifacts\E2-Day-$RuntimeIdentifier.zip"
    $pendingZip = Join-Path $projectRoot ("artifacts\E2-Day-$RuntimeIdentifier-" + [Guid]::NewGuid().ToString('N') + '.zip')
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, $pendingZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Move-Item -LiteralPath $pendingZip -Destination $zipPath -Force
    Write-Host "程序：$(Join-Path $publishDirectory 'DailyRecord.exe')"
    Write-Host "压缩包：$zipPath"
} finally { Pop-Location }
