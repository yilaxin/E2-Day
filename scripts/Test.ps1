param(
    [switch]$Network,
    [switch]$Published,
    [ValidateSet('win-arm64', 'win-x64')]
    [string]$RuntimeIdentifier = 'win-arm64',
    [string]$ReleaseDirectory = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Environment.ps1')
$appProject = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src/DailyRecord.App/DailyRecord.App.csproj') -Raw -Encoding UTF8)
$appVersion = [string]$appProject.Project.PropertyGroup.Version
Push-Location $projectRoot
try {
    $testArguments = @('--output', (Join-Path $projectRoot '.build\tests'))
    if ($Network) { $testArguments += '--network' }
    dotnet run --project tests/DailyRecord.Tests/DailyRecord.Tests.csproj -p:BuildInParallel=false -p:UseSharedCompilation=false -- @testArguments
    if ($LASTEXITCODE -ne 0) { throw '核心规则测试失败。' }
    $coreReport = Get-Content -LiteralPath (Join-Path $projectRoot '.build\tests\latest-result.txt') -Raw
    if ($coreReport -match 'Data: ([^\r\n]+)') {
        node tests/mobile-rules.mjs (Join-Path $Matches[1] 'portable-sample.json')
        if ($LASTEXITCODE -ne 0) { throw '手机端规则及通用备份测试失败。' }
        node tests/mobile-file-import.mjs (Join-Path $Matches[1] 'file-exchange')
        if ($LASTEXITCODE -ne 0) { throw '手机与电脑文件交换测试失败。' }
    }
    node tests/mobile-offline-start.mjs
    if ($LASTEXITCODE -ne 0) { throw '手机离线启动测试失败。' }
    & (Join-Path $projectRoot 'tests/release-directory-check.ps1')
    if ($Published) {
        $programRoot = if ($ReleaseDirectory) { [IO.Path]::GetFullPath((Join-Path $projectRoot $ReleaseDirectory)) } else { Join-Path $projectRoot "artifacts/releases/v$appVersion" }
        if (!$programRoot.StartsWith($projectRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '验证目录必须位于项目内。' }
        $executable = Join-Path $programRoot "programs/E2-Day-$RuntimeIdentifier/DailyRecord.exe"
    }
    else {
        dotnet build src/DailyRecord.App/DailyRecord.App.csproj --nologo -m:1 -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw '桌面界面构建失败。' }
        $executable = Join-Path $projectRoot 'src\DailyRecord.App\bin\Debug\net10.0-windows\DailyRecord.exe'
    }
    $smokeDirectory = Join-Path $projectRoot ('.build\smoke-' + [Guid]::NewGuid().ToString('N'))
    $previewDirectory = Join-Path $projectRoot ('artifacts\previews' + $(if ($RuntimeIdentifier -eq 'win-arm64') { '' } else { "-$RuntimeIdentifier" }))
    $smokeProcess = Start-Process -FilePath $executable -ArgumentList @('--data-dir', ('"{0}"' -f $smokeDirectory), '--smoke-test', ('"{0}"' -f $previewDirectory)) -WindowStyle Hidden -Wait -PassThru
    if ($smokeProcess.ExitCode -ne 0) {
        $errorLog = Join-Path $smokeDirectory 'errors.log'
        if (Test-Path -LiteralPath $errorLog) { Get-Content -LiteralPath $errorLog -Encoding UTF8 }
        throw "桌面程序验证失败，退出代码 $($smokeProcess.ExitCode)。"
    }
    Get-Content -LiteralPath (Join-Path $previewDirectory 'result.txt') -Encoding UTF8
    & (Join-Path $projectRoot 'tests/startup-entry-check.ps1') -Executable $executable -FixtureRoot (Join-Path $previewDirectory 'account-fixture')
} finally { Pop-Location }
