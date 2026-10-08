param(
    [switch]$IncludeRuntime,
    [ValidateSet('win-arm64', 'win-x64')]
    [string]$RuntimeIdentifier = 'win-arm64'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$feedDirectory = Join-Path $projectRoot '.build\feed'
New-Item -ItemType Directory -Path $feedDirectory -Force | Out-Null
$downloaded = @{}
function Get-Package([string]$packageId, [string]$packageVersion) {
    $packageKey = "$($packageId.ToLowerInvariant()).$packageVersion"
    if ($downloaded.ContainsKey($packageKey)) { return }
    $downloaded[$packageKey] = $true
    $packagePath = Join-Path $feedDirectory "$packageKey.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath)) {
        $lowerId = $packageId.ToLowerInvariant()
        Write-Host "获取 $packageId $packageVersion"
        $partialPath = "$packagePath.partial"
        for ($downloadAttempt = 1; $downloadAttempt -le 3; $downloadAttempt++) {
            try {
                Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$lowerId/$packageVersion/$lowerId.$packageVersion.nupkg" -OutFile $partialPath -HttpVersion 1.1
                break
            } catch {
                if ($downloadAttempt -eq 3) { throw }
                Write-Host "下载连接中断，重试 $packageId ($downloadAttempt/3)"
                Start-Sleep -Seconds 2
            }
        }
        Move-Item -LiteralPath $partialPath -Destination $packagePath -Force
    }
    $packageArchive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $manifestEntry = $packageArchive.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        $manifestReader = [System.IO.StreamReader]::new($manifestEntry.Open())
        try { [xml]$manifest = $manifestReader.ReadToEnd() } finally { $manifestReader.Dispose() }
    } finally { $packageArchive.Dispose() }
    $dependencyRoot = $manifest.package.metadata.dependencies
    if ($null -eq $dependencyRoot) { return }
    $selectedGroup = $null
    foreach ($frameworkName in 'net10.0','.NETCoreApp10.0','net8.0','.NETCoreApp8.0','net6.0','.NETCoreApp6.0','.NETCoreApp2.1','.NETStandard2.0','.NETStandard1.6','.NETStandard1.3','.NETStandard1.1') {
        $candidateGroup = @($dependencyRoot.group) | Where-Object { $_.targetFramework -eq $frameworkName } | Select-Object -First 1
        if ($null -ne $candidateGroup) { $selectedGroup = $candidateGroup; break }
    }
    $dependencies = if ($null -ne $selectedGroup) { @($selectedGroup.dependency) } else { @($dependencyRoot.dependency) }
    foreach ($dependency in $dependencies) {
        if ($null -eq $dependency -or [string]::IsNullOrEmpty($dependency.id)) { continue }
        $versionMatch = [regex]::Match($dependency.version, '\d+\.\d+\.\d+(?:[-.][0-9A-Za-z.-]+)?')
        if (-not $versionMatch.Success) { throw "无法解析 $($dependency.id) 的版本 $($dependency.version)" }
        Get-Package $dependency.id $versionMatch.Value
    }
}
Get-Package 'Microsoft.Data.Sqlite' '10.0.11'
if ($IncludeRuntime) {
    Get-Package "Microsoft.NETCore.App.Runtime.$RuntimeIdentifier" '10.0.11'
    Get-Package "Microsoft.WindowsDesktop.App.Runtime.$RuntimeIdentifier" '10.0.11'
    Get-Package "Microsoft.AspNetCore.App.Runtime.$RuntimeIdentifier" '10.0.11'
    Get-Package "Microsoft.NETCore.App.Host.$RuntimeIdentifier" '10.0.11'
}
Write-Host "依赖缓存已准备：$feedDirectory"
