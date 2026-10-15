param([string]$Version='', [string]$PackageDirectory='')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if (!$Version) { $project=[xml](Get-Content (Join-Path $root 'src/DailyRecord.Tutor/DailyRecord.Tutor.csproj') -Raw); $Version=[string]$project.Project.PropertyGroup.Version }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号无效。' }
$packages=if ($PackageDirectory) { [IO.Path]::GetFullPath((Join-Path $root $PackageDirectory)) } else { Join-Path $root "artifacts/releases/tutor-v$Version" }
if (!$packages.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '测试程序包须位于项目内。' }
$auditRoot=Join-Path $root ('.build/tutor-package-audit-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $auditRoot | Out-Null
$hashes=@{}
foreach ($line in Get-Content (Join-Path $packages 'SHA256SUMS.txt')) { if ($line -match '^([a-fA-F0-9]{64})  ([A-Za-z0-9._-]+)$') { $hashes[$Matches[2]]=$Matches[1].ToLowerInvariant() } }
$report=@()
foreach ($rid in @('win-arm64','win-x64')) {
    $name="E2-Day-Tutor-v$Version-$rid.zip"; $zip=Join-Path $packages $name
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$name]) { throw "程序包校验失败：$name" }
    $folder=Join-Path $auditRoot $rid
    $archive=[IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $archive.Entries) {
            $target=[IO.Path]::GetFullPath((Join-Path $folder $entry.FullName))
            if (!$target.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'ZIP 含越界路径。' }
        }
    } finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($zip,$folder)
    foreach ($required in @('E2Day.Tutor.exe','E2Day.Tutor.dll','DailyRecord.Core.dll','hostfxr.dll','hostpolicy.dll','coreclr.dll','PresentationFramework.dll','e_sqlite3.dll','E2Day.Tutor.runtimeconfig.json','E2Day.Tutor.deps.json','e2-day-tutor.ico')) {
        if (!(Test-Path -LiteralPath (Join-Path $folder $required) -PathType Leaf)) { throw "解压后缺少 $required" }
    }
    if ([version]$Version -ge [version]'1.0.2') {
        foreach ($guide in @('先读我.txt','安装与使用教程.pdf')) { if (!(Test-Path -LiteralPath (Join-Path $folder $guide))) { throw "缺少新用户教程 $guide" } }
    }
    $exe=Join-Path $folder 'E2Day.Tutor.exe'
    if ((Get-Item $exe).VersionInfo.FileVersion -ne "$Version.0") { throw '解压后的程序版本不匹配。' }
    $config=Get-Content (Join-Path $folder 'E2Day.Tutor.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($config.runtimeOptions.framework -or !$config.runtimeOptions.includedFrameworks) { throw '程序未包含所需运行时配置。' }
    foreach ($binary in @('E2Day.Tutor.exe','coreclr.dll','e_sqlite3.dll')) {
        $bytes=[IO.File]::ReadAllBytes((Join-Path $folder $binary)); $offset=[BitConverter]::ToInt32($bytes,0x3c)
        $expected=if ($rid -eq 'win-arm64') {0xAA64} else {0x8664}
        if ([BitConverter]::ToUInt16($bytes,$offset+4) -ne $expected) { throw "二进制架构不匹配：$rid $binary" }
    }
    $freshData=Join-Path $auditRoot "$rid-first-launch"
    $process=Start-Process -FilePath $exe -WorkingDirectory $folder -ArgumentList @('--data-dir',('"{0}"' -f $freshData)) -WindowStyle Hidden -PassThru
    try {
        $title=''
        for ($attempt=0; $attempt -lt 20; $attempt++) {
            Start-Sleep -Milliseconds 400; $process.Refresh()
            if ($process.HasExited) { throw "首次启动过早退出：$rid，$($process.ExitCode)" }
            $title=$process.MainWindowTitle
            if ($title -eq 'E²-Day 家教版 · 账号登录') { break }
        }
        if ($title -ne 'E²-Day 家教版 · 账号登录') { throw "未确认首次账号窗口：$rid" }
        if ((Get-ChildItem -LiteralPath $freshData -Filter tutor.db -Recurse -ErrorAction SilentlyContinue).Count) { throw '首次启动前已存在教学数据，隔离测试异常。' }
        $report += "PASS $rid $Version ZIP integrity, complete extraction, self-contained runtime, PE architecture, clean first-account window"
    } finally {
        $process.Refresh()
        if (!$process.HasExited) {
            $closed=$process.CloseMainWindow()
            if (!$closed -or !$process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
        }
    }
    $smoke=Join-Path $auditRoot "$rid-smoke"; $screens=Join-Path $auditRoot "$rid-previews"
    $validation=Start-Process -FilePath $exe -WorkingDirectory $folder -ArgumentList @('--data-dir',('"{0}"' -f $smoke),'--smoke-test',('"{0}"' -f $screens)) -WindowStyle Hidden -Wait -PassThru
    if ($validation.ExitCode -ne 0) { if (Test-Path (Join-Path $smoke 'errors.log')) { Get-Content (Join-Path $smoke 'errors.log') }; throw "解压后的 $rid WPF 验证失败。" }
    $report += (Get-Content (Join-Path $screens 'result.txt') | Where-Object { $_ -match '^(PASS|Version:|Architecture:)' }) -join '; '
    $report += "Signature $rid`: $((Get-AuthenticodeSignature $exe).Status) (SmartScreen reputation prompts remain possible)"
    $report[-3..-1] | Write-Output
}
[IO.File]::WriteAllLines((Join-Path $auditRoot 'result.txt'),$report)
Write-Output "程序包审计通过；独立测试结果：$auditRoot"
