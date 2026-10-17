param(
    [ValidateSet('Publish','Verify','Preview','Pages')][string]$Mode = 'Publish',
    [string]$Version = '',
    [string]$CommitMessage = '',
    [string]$ReleaseDirectory = '',
    [switch]$IncludeHarmony,
    [ValidateSet('Standard','Tutor')][string]$Edition = 'Standard'
)
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$ownerName = 'yilaxin'
$repositoryName = 'E2-Day'
$repoPath = "$ownerName/$repositoryName"
$projectPath = if ($Edition -eq 'Tutor') { 'src/DailyRecord.Tutor/DailyRecord.Tutor.csproj' } else { 'src/DailyRecord.App/DailyRecord.App.csproj' }
if ($Edition -eq 'Tutor' -and $IncludeHarmony) { throw '家教版当前仅发布 Windows。' }
$appProject = [xml](Get-Content -LiteralPath (Join-Path $projectDirectory $projectPath) -Raw -Encoding UTF8)
$appVersion = [string]$appProject.Project.PropertyGroup.Version
if (-not $Version) { $Version = $appVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号需为 major.minor.patch 格式。' }
if ($Mode -eq 'Publish' -and $Version -ne $appVersion) { throw '发布版本与 App 源码版本不一致。' }
if (-not $CommitMessage) { $CommitMessage = "Publish E2-Day $Version with updated Windows packages and documentation" }
$versionTag = if ($Edition -eq 'Tutor') { "tutor-v$Version" } else { "v$Version" }
$releaseDirectory = if ($ReleaseDirectory) { [IO.Path]::GetFullPath((Join-Path $projectDirectory $ReleaseDirectory)) } else { Join-Path $projectDirectory "artifacts/releases/$versionTag" }
if (!$releaseDirectory.StartsWith($projectDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '发布目录必须位于项目内。' }
$releaseNotesPath = Join-Path $projectDirectory "docs/RELEASE-$versionTag.md"
$assetNames = @("E2-Day-$versionTag-win-arm64.zip", "E2-Day-$versionTag-win-x64.zip", 'SHA256SUMS.txt')
if ($IncludeHarmony) { $assetNames = @("E2-Day-$versionTag-win-arm64.zip", "E2-Day-$versionTag-win-x64.zip", "E2-Day-$versionTag-harmony-unsigned.zip", 'SHA256SUMS.txt') }
if ($Edition -eq 'Tutor') { $assetNames = @("E2-Day-Tutor-v$Version-win-arm64.zip", "E2-Day-Tutor-v$Version-win-x64.zip", "E2-Day-Tutor-Guide-v$Version.pdf", 'SHA256SUMS.txt') }
$responseDirectory = Join-Path $projectDirectory '.build/github'
New-Item -ItemType Directory -Path $responseDirectory -Force | Out-Null

$credentialInput = "protocol=https`nhost=github.com`nusername=$ownerName`n`n"
$credentialLines = @($credentialInput | git -c credential.interactive=never credential fill)
if ($LASTEXITCODE -ne 0) { throw '本机 GitHub 授权不可用。' }
$credentialMap = @{}
foreach ($line in $credentialLines) {
    $separatorIndex = $line.IndexOf('=')
    if ($separatorIndex -gt 0) { $credentialMap[$line.Substring(0,$separatorIndex)] = $line.Substring($separatorIndex + 1) }
}
if (-not $credentialMap['password']) { throw '本机没有可用的 GitHub 授权。' }
$requestHeaders = @{
    Authorization = 'Bearer ' + $credentialMap['password']
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'E2-Day-Publisher'
}
$credentialMap.Clear()
$credentialLines = $null

function Invoke-GitHubApi {
    param([string]$Method = 'GET', [string]$Path, $Body, [string]$InFile, [string]$ContentType = 'application/json; charset=utf-8')
    $requestUri = if ($Path.StartsWith('https://')) { $Path } else { 'https://api.github.com' + $Path }
    $targetUri = [Uri]$requestUri
    if ($targetUri.Scheme -ne 'https' -or $targetUri.Host -notin @('api.github.com','uploads.github.com')) { throw '拒绝向非 GitHub 官方接口发送授权。' }
    $maximumAttempts = if ($Method -in @('GET','PATCH','PUT')) { 3 } else { 1 }
    for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
        $script:lastStatus = 0
        try {
            $requestParameters = @{
                Uri = $requestUri; Method = $Method; Headers = $requestHeaders
                HttpVersion = '1.1'; TimeoutSec = 300; ErrorAction = 'Stop'
            }
            if ($null -ne $Body) { $requestParameters.Body = ($Body | ConvertTo-Json -Depth 8 -Compress); $requestParameters.ContentType = $ContentType }
            if ($InFile) { $requestParameters.InFile = $InFile; $requestParameters.ContentType = $ContentType }
            return Invoke-RestMethod @requestParameters
        } catch {
            if ($_.Exception.Response) { $script:lastStatus = [int]$_.Exception.Response.StatusCode }
            if ($script:lastStatus -in @(401,403,404,422) -or $attempt -eq $maximumAttempts) {
                throw "GitHub 请求未完成：$Method $requestUri，状态 $script:lastStatus。"
            }
            Start-Sleep -Seconds 2
        }
    }
}

function Send-ReleaseAsset {
    param([string]$UploadUrl, [string]$FilePath, [string]$ContentType)
    $uploadUri = [Uri]$UploadUrl
    if ($uploadUri.Scheme -ne 'https' -or $uploadUri.Host -ne 'uploads.github.com') { throw '发布附件地址不是 GitHub 官方上传接口。' }
    $responsePath = Join-Path $responseDirectory ('upload-response-' + [Guid]::NewGuid().ToString('N') + '.json')
    $authorizationHeader = $requestHeaders.Authorization
    if ($authorizationHeader.Contains('"') -or $authorizationHeader.Contains("`n") -or $authorizationHeader.Contains("`r")) { throw '授权格式异常。' }
    $curlConfiguration = 'header = "Authorization: ' + $authorizationHeader + '"' + "`n" + 'header = "Accept: application/vnd.github+json"' + "`n" + 'header = "X-GitHub-Api-Version: 2022-11-28"' + "`n"
    $httpStatus = $curlConfiguration | & curl.exe --config - --http1.1 --request POST --data-binary "@$FilePath" --header "Content-Type: $ContentType" --url $UploadUrl --output $responsePath --write-out '%{http_code}' --connect-timeout 30 --max-time 300 --silent --show-error
    $curlConfiguration = $null
    if ($LASTEXITCODE -ne 0 -or [int]$httpStatus -notin @(200,201)) { throw "程序包上传连接未完成，HTTP 状态 $httpStatus。" }
    return Get-Content -LiteralPath $responsePath -Raw -Encoding UTF8 | ConvertFrom-Json
}

try {
    $profile = Invoke-GitHubApi -Path '/user'
    if ($profile.login -cne $ownerName) { throw 'GitHub 授权账号与目标账号不一致，停止上传。' }
    Write-Output "GitHub 账号验证成功：$($profile.login)"
    if ($Mode -in @('Publish','Preview')) {
      if ($Mode -eq 'Publish') {
        foreach ($assetName in $assetNames) {
            if (-not (Test-Path -LiteralPath (Join-Path $releaseDirectory $assetName))) { throw "缺少发布附件：$assetName" }
        }
        if (-not (Test-Path -LiteralPath $releaseNotesPath)) { throw '缺少本版发布说明。' }
        $releaseBody = Get-Content -LiteralPath $releaseNotesPath -Raw -Encoding UTF8
        if ($releaseBody -match '发布准备状态：候选版') { throw '当前仍为候选版。先完成手机验收并更新对应发布说明，再发布正式版本。' }
        $releaseTitle = (($releaseBody -split '\r?\n' | Select-Object -First 1) -replace '^#\s+', '').Trim()
        if (-not $releaseTitle) { throw '发布说明缺少标题。' }
        $expectedChecksums = @{}
        foreach ($checksumLine in Get-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding UTF8) {
            if ($checksumLine -match '^([a-fA-F0-9]{64})  (.+)$') { $expectedChecksums[$Matches[2]] = $Matches[1].ToLowerInvariant() }
        }
        foreach ($packageName in $assetNames | Where-Object { $_.EndsWith('.zip') -or $_.EndsWith('.pdf') }) {
            $actualHash = (Get-FileHash -LiteralPath (Join-Path $releaseDirectory $packageName) -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($expectedChecksums[$packageName] -ne $actualHash) { throw "本地程序包校验失败：$packageName" }
        }
      }
        try { $repository = Invoke-GitHubApi -Path "/repos/$repoPath" }
        catch {
            if ($script:lastStatus -ne 404) { throw }
            Write-Output "创建公开仓库：$repoPath"
            try {
                $repository = Invoke-GitHubApi -Method POST -Path '/user/repos' -Body @{
                    name = $repositoryName
                    description = 'E²-Day · 探索每一天：Windows 任务与全年/月度日历 App，支持备注图片、公历/农历重复、提前提醒、完成历史、本地备份和分享图。Daily task planner for Windows x64/ARM64.'
                    homepage = "https://github.com/$repoPath/releases/latest"
                    private = $false; auto_init = $false; has_issues = $true; has_wiki = $false
                }
            } catch { $repository = Invoke-GitHubApi -Path "/repos/$repoPath" }
        }
        if ($repository.private -or $repository.owner.login -cne $ownerName) { throw '仓库所有者或公开状态不符合预期。' }
        $updatedTopics = @(@($repository.topics) + @('windows','wpf','dotnet','todo','task-management','calendar','planner','productivity','chinese-calendar','lunar-calendar','offline','desktop-app','backup','arm64','x64','e2-day','notes') | Select-Object -Unique | Select-Object -First 20)
        $null = Invoke-GitHubApi -Method PUT -Path "/repos/$repoPath/topics" -Body @{ names = $updatedTopics }
        Push-Location $projectDirectory
        try {
            git config user.name $ownerName
            if ($LASTEXITCODE -ne 0) { throw '无法设置本项目提交作者。' }
            git config user.email '271911605+yilaxin@users.noreply.github.com'
            if ($LASTEXITCODE -ne 0) { throw '无法设置本项目公开提交邮箱。' }
            git add -- .gitattributes .gitignore CHANGELOG.md Directory.Build.props LICENSE NuGet.Config README.md THIRD_PARTY_NOTICES.md data docs scripts src tests 使用说明.md 需求说明.md 验证记录.md
            if ($LASTEXITCODE -ne 0) { throw '源码准备失败。' }
            $stagedFiles = @(git -c core.quotepath=false diff --cached --name-only)
            if ($stagedFiles | Where-Object { $_ -match '(^|/)(\.build|artifacts|\.env|bin|obj|Accounts|LocalSync|oh_modules|\.hvigor)(/|$)|\.(db|db-shm|db-wal|pfx|p12|key|zip|log|protected|p7b|p7s|cer|csr|jks|keystore|hap|har)$|(^|/)(accounts\.json|session\.lock|local\.properties|signing\.local\.json5)$' }) { throw '暂存内容含非发布文件，停止上传。' }
            if ($stagedFiles.Count -gt 0) {
                git commit -m $CommitMessage
                if ($LASTEXITCODE -ne 0) { throw '源码提交失败。' }
            }
            $configuredOrigin = git remote get-url origin 2>$null
            if ($configuredOrigin) {
                if ($configuredOrigin -ne "https://github.com/$repoPath.git") { throw '已有远程仓库与目标不同，停止上传。' }
            } else {
                git remote add origin "https://github.com/$repoPath.git"
                if ($LASTEXITCODE -ne 0) { throw '无法记录发布仓库地址。' }
            }
            $commitSha = (git rev-parse HEAD).Trim()
          if ($Mode -eq 'Publish') {
            if (-not (git tag --list $versionTag)) {
                git tag -a $versionTag -m "E2-Day $Version public release"
                if ($LASTEXITCODE -ne 0) { throw '版本标签创建失败。' }
            }
            $tagCommit = (git rev-list -n 1 $versionTag).Trim()
            if ($tagCommit -ne $commitSha) { throw '版本标签与当前发布提交不一致，停止上传。' }
          }
            Write-Output '正在上传源码、说明和功能截图。'
            if ($Mode -eq 'Publish') { git -c credential.username=$ownerName -c credential.interactive=never push origin HEAD:refs/heads/main "refs/tags/$versionTag" }
            else { git -c credential.username=$ownerName -c credential.interactive=never push origin HEAD:refs/heads/main }
            if ($LASTEXITCODE -ne 0) { throw '源码上传失败。' }
          if ($Mode -eq 'Publish') {
            git archive --format=zip --output (Join-Path $releaseDirectory "E2-Day-$versionTag-source.zip") HEAD
            if ($LASTEXITCODE -ne 0) { throw '本地源码压缩包生成失败。' }
          }
        } finally { Pop-Location }
        $null = Invoke-GitHubApi -Method PATCH -Path "/repos/$repoPath" -Body @{
            default_branch = 'main'
            description = 'E²-Day · 探索每一天：Windows 任务与全年/月度日历 App，支持备注图片、公历/农历重复、提前提醒、完成历史、本地备份和分享图。Daily task planner for Windows x64/ARM64.'
            homepage = "https://github.com/$repoPath/releases/latest"
        }
      if ($Mode -eq 'Publish') {
        $release = @(Invoke-GitHubApi -Path "/repos/$repoPath/releases?per_page=100") | Where-Object { $_.tag_name -eq $versionTag } | Select-Object -First 1
        if (-not $release) {
            try {
                $release = Invoke-GitHubApi -Method POST -Path "/repos/$repoPath/releases" -Body @{
                    tag_name = $versionTag; target_commitish = $commitSha
                    name = $releaseTitle
                    body = $releaseBody; draft = $true; prerelease = $false
                }
            } catch {
                $release = @(Invoke-GitHubApi -Path "/repos/$repoPath/releases?per_page=100") | Where-Object { $_.tag_name -eq $versionTag } | Select-Object -First 1
                if (-not $release) { throw }
            }
        }
        if ($release.draft) {
            $release = Invoke-GitHubApi -Method PATCH -Path "/repos/$repoPath/releases/$($release.id)" -Body @{ tag_name = $versionTag; target_commitish = $commitSha; name = $releaseTitle; body = $releaseBody }
        }
        foreach ($assetName in $assetNames) {
            $assetFile = Get-Item -LiteralPath (Join-Path $releaseDirectory $assetName)
            $localHash = (Get-FileHash -LiteralPath $assetFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $uploadedAsset = $null
            for ($uploadAttempt = 1; $uploadAttempt -le 3; $uploadAttempt++) {
                $uploadedAsset = @(Invoke-GitHubApi -Path "/repos/$repoPath/releases/$($release.id)/assets") | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
                if ($uploadedAsset) {
                    if ($release.draft -and $uploadedAsset.state -eq 'starter') {
                        Write-Output "清理本次上传产生的不完整附件：$assetName"
                        $null = Invoke-GitHubApi -Method DELETE -Path "/repos/$repoPath/releases/assets/$($uploadedAsset.id)"
                        $uploadedAsset = $null
                    }
                }
                if ($uploadedAsset) {
                    if ($uploadedAsset.state -ne 'uploaded' -or $uploadedAsset.size -ne $assetFile.Length) { throw "已存在的附件不完整：$assetName" }
                    if ($uploadedAsset.digest -and $uploadedAsset.digest -ne "sha256:$localHash") { throw "远程附件校验值不匹配：$assetName" }
                    break
                }
                Write-Output "正在上传附件：$assetName，$($assetFile.Length) 字节。"
                try {
                    $uploadUrl = $release.upload_url.Split('{')[0] + '?name=' + [Uri]::EscapeDataString($assetName)
                    $assetContentType = if ($assetName.EndsWith('.zip')) { 'application/zip' } elseif ($assetName.EndsWith('.pdf')) { 'application/pdf' } else { 'text/plain' }
                    $uploadedAsset = Send-ReleaseAsset -UploadUrl $uploadUrl -FilePath $assetFile.FullName -ContentType $assetContentType
                } catch {
                    if ($uploadAttempt -eq 3) { throw }
                    Write-Output '连接中断，先检查远程附件，再继续。'
                    Start-Sleep -Seconds 2
                    continue
                }
                if ($uploadedAsset.state -ne 'uploaded' -or $uploadedAsset.size -ne $assetFile.Length) { throw "附件大小或状态异常：$assetName" }
                if ($uploadedAsset.digest -and $uploadedAsset.digest -ne "sha256:$localHash") { throw "附件校验值不匹配：$assetName" }
                break
            }
            Write-Output "附件已验证：$assetName"
        }
        $makeLatest = if ($Edition -eq 'Tutor') { 'false' } else { 'true' }
        $release = Invoke-GitHubApi -Method PATCH -Path "/repos/$repoPath/releases/$($release.id)" -Body @{ tag_name = $versionTag; target_commitish = $commitSha; draft = $false; prerelease = $false; make_latest = $makeLatest }
        if ($release.tag_name -ne $versionTag -or $release.draft) { throw '发布页版本标签或公开状态与目标不一致。' }
        Write-Output "发布成功：$($release.html_url)"
      }
    }
    if ($Mode -eq 'Pages') {
        try { $pages = Invoke-GitHubApi -Path "/repos/$repoPath/pages" }
        catch {
            if ($script:lastStatus -ne 404) { throw }
            $pages = Invoke-GitHubApi -Method POST -Path "/repos/$repoPath/pages" -Body @{ build_type = 'legacy'; source = @{ branch = 'main'; path = '/docs' } }
        }
        if ($pages.source.branch -ne 'main' -or $pages.source.path -ne '/docs' -or $pages.cname) { throw '现有 Pages 配置与本项目静态应用目录不同，保留现有配置并停止。' }
        try { $pageBuild = Invoke-GitHubApi -Path "/repos/$repoPath/pages/builds/latest" }
        catch {
            if ($script:lastStatus -ne 404) { throw }
            $pageBuild = Invoke-GitHubApi -Method POST -Path "/repos/$repoPath/pages/builds"
        }
        Write-Output "静态应用托管：$($pages.html_url)；状态：$($pages.status)"
        Write-Output "最新静态构建：$($pageBuild.status)；提交：$($pageBuild.commit)；错误：$($pageBuild.error.message)"
    }
    $repository = Invoke-GitHubApi -Path "/repos/$repoPath"
    $latestRelease = if ($Edition -eq 'Tutor') { Invoke-GitHubApi -Path "/repos/$repoPath/releases/tags/$versionTag" } else { Invoke-GitHubApi -Path "/repos/$repoPath/releases/latest" }
    [pscustomobject]@{
        Repository = $repository.html_url; Public = -not $repository.private
        DefaultBranch = $repository.default_branch; Description = $repository.description
        Topics = $repository.topics; Release = $latestRelease.html_url
        Tag = $latestRelease.tag_name; Draft = $latestRelease.draft
        Assets = @($latestRelease.assets | Select-Object name,size,state,digest,browser_download_url)
    } | ConvertTo-Json -Depth 6
} finally { $requestHeaders.Clear() }
