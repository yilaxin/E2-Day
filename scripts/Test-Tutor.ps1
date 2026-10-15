param([switch]$Published,[ValidateSet('win-arm64','win-x64')][string]$RuntimeIdentifier='win-arm64',[string]$ReleaseDirectory='')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Environment.ps1')
Push-Location $projectRoot
try {
    dotnet run --project tests/DailyRecord.Tutor.Tests -p:UseSharedCompilation=false -- (Join-Path $projectRoot '.build/tutor-tests')
    if ($LASTEXITCODE -ne 0) { throw '家教版核心测试失败。' }
    if ($Published) {
        $releaseRoot = if ($ReleaseDirectory) { [IO.Path]::GetFullPath((Join-Path $projectRoot $ReleaseDirectory)) } else { Join-Path $projectRoot 'artifacts/releases/tutor-v1.0.0' }
        if (!$releaseRoot.StartsWith($projectRoot.TrimEnd('\') + '\',[StringComparison]::OrdinalIgnoreCase)) { throw '验证目录必须位于项目内。' }
        $executable = Join-Path $releaseRoot "programs/E2-Day-Tutor-$RuntimeIdentifier/E2Day.Tutor.exe"
    } else {
        dotnet build src/DailyRecord.Tutor --nologo -m:1 -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw '家教版构建失败。' }
        $executable = Join-Path $projectRoot 'src/DailyRecord.Tutor/bin/Debug/net10.0-windows/E2Day.Tutor.exe'
    }
    $data = Join-Path $projectRoot ('.build/tutor-smoke-' + [Guid]::NewGuid().ToString('N'))
    $output = Join-Path $projectRoot "artifacts/tutor-previews-$RuntimeIdentifier"
    $process = Start-Process -FilePath $executable -ArgumentList @('--data-dir',('"{0}"' -f $data),'--smoke-test',('"{0}"' -f $output)) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        if (Test-Path (Join-Path $data 'errors.log')) { Get-Content (Join-Path $data 'errors.log') }
        throw "家教版 WPF 验证失败，退出代码 $($process.ExitCode)。"
    }
    Get-Content (Join-Path $output 'result.txt')
} finally { Pop-Location }
