param(
    [switch]$Network,
    [switch]$Published,
    [ValidateSet('win-arm64', 'win-x64')]
    [string]$RuntimeIdentifier = 'win-arm64'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Environment.ps1')
Push-Location $projectRoot
try {
    $testArguments = @('--output', (Join-Path $projectRoot '.build\tests'))
    if ($Network) { $testArguments += '--network' }
    dotnet run --project tests/DailyRecord.Tests/DailyRecord.Tests.csproj -- @testArguments
    if ($LASTEXITCODE -ne 0) { throw '核心规则测试失败。' }
    if ($Published) { $executable = Join-Path $projectRoot "artifacts\E2-Day-$RuntimeIdentifier\DailyRecord.exe" }
    else {
        dotnet build src/DailyRecord.App/DailyRecord.App.csproj --nologo
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
} finally { Pop-Location }
