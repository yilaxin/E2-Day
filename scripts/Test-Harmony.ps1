param([switch]$RefreshFixtures)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Environment.ps1')
Push-Location $projectRoot
try {
    if (!(Test-Path -LiteralPath '.build/harmony-tests/node_modules/typescript/lib/typescript.js')) {
        throw '缺少 TypeScript 测试工具，请按 docs/HARMONY-NATIVE.md 安装本地测试依赖。'
    }
    if ($RefreshFixtures -or !(Test-Path -LiteralPath '.build/tests/latest-result.txt')) {
        dotnet run --project tests/DailyRecord.Tests/DailyRecord.Tests.csproj -p:UseSharedCompilation=false -- --output .build/tests
        if ($LASTEXITCODE -ne 0) { throw '电脑版规则及共享样例验证失败。' }
    }
    node tests/harmony-domain.cjs
    if ($LASTEXITCODE -ne 0) { throw '原生领域规则验证失败。' }
    node tests/harmony-storage.cjs
    if ($LASTEXITCODE -ne 0) { throw '原生持久化与账号验证失败。' }
    node tests/harmony-presentation.cjs
    if ($LASTEXITCODE -ne 0) { throw '原生任务卡片状态更新验证失败。' }
    dotnet run --project tests/DailyRecord.Tests/DailyRecord.Tests.csproj -p:UseSharedCompilation=false -- --verify-exchange .build/harmony-domain-tests/native-export.json
    if ($LASTEXITCODE -ne 0) { throw '原生导出到电脑版的实际往返验证失败。' }
    Write-Host '领域规则、SQLite 适配测试、电脑版文件往返通过；不替代鸿蒙 SDK 编译、原生 UI 和真机验证。'
} finally { Pop-Location }
