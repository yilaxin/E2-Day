param([string]$Tag='tutor-v1.0.0',[string]$ReleaseDirectory='artifacts/releases/tutor-v1.0.0')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$directory=[IO.Path]::GetFullPath((Join-Path $root $ReleaseDirectory))
if (!$directory.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '校验目录必须位于项目内。' }
$release=Invoke-RestMethod -Uri "https://api.github.com/repos/yilaxin/E2-Day/releases/tags/$Tag" -Headers @{'User-Agent'='E2-Day-Public-Verification';Accept='application/vnd.github+json'}
if ($release.draft -or $release.tag_name -ne $Tag) { throw '发布页尚未公开或标签不一致。' }
$output=Join-Path $root ('.build/public-verification/'+$Tag)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$manifest=$release.assets | Where-Object name -eq 'SHA256SUMS.txt' | Select-Object -First 1
if (!$manifest) { throw '缺少公开校验文件。' }
Invoke-WebRequest -Uri $manifest.browser_download_url -OutFile (Join-Path $output 'SHA256SUMS.txt') -HttpVersion 1.1
$lines=@()
foreach ($line in Get-Content (Join-Path $output 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-fA-F0-9]{64})  ([A-Za-z0-9._-]+\.zip)$') { throw '公开校验文件格式无效。' }
    $hash=$Matches[1].ToLowerInvariant(); $name=$Matches[2]
    $asset=$release.assets | Where-Object name -eq $name | Select-Object -First 1
    if (!$asset -or $asset.state -ne 'uploaded') { throw "缺少公开下载：$name" }
    $download=Join-Path $output $name
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $download -HttpVersion 1.1 -TimeoutSec 300
    if ((Get-Item $download).Length -ne $asset.size -or (Get-FileHash $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "公开下载校验失败：$name" }
    if ((Get-FileHash (Join-Path $directory $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "公开下载与本地发布不一致：$name" }
    $lines += "PASS $name SHA256 $hash"; Write-Output $lines[-1]
}
$latest=Invoke-RestMethod -Uri 'https://api.github.com/repos/yilaxin/E2-Day/releases/latest' -Headers @{'User-Agent'='E2-Day-Public-Verification'}
if ($Tag.StartsWith('tutor-') -and $latest.tag_name.StartsWith('tutor-')) { throw '家教版意外替换了日常版最新发布入口。' }
$lines += "Release: $($release.html_url)"; $lines += "Standard latest: $($latest.tag_name)"
[IO.File]::WriteAllLines((Join-Path $output 'result.txt'),$lines)
Write-Output "公开下载与完整 SHA-256 校验通过；日常版最新入口：$($latest.tag_name)"
