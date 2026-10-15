param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = @(Get-ChildItem -LiteralPath $directory -File -Filter '*.hap')
if ($packages.Count -ne 1) { throw 'Expected exactly one native HAP.' }
$package = $packages[0]
$expected = (Get-Content -LiteralPath (Join-Path $directory 'SHA256SUMS.txt') -Raw).Trim().Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
if ($expected.Count -ne 2 -or $expected[1] -ne $package.Name -or (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash -ine $expected[0]) { throw 'HAP checksum does not match.' }
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
function Read-Entry([string]$Name) {
    $entry = $archive.GetEntry($Name)
    if (!$entry) { throw "Missing native resource: $Name" }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
try {
    $module = Read-Entry 'module.json' | ConvertFrom-Json
    $pages = Read-Entry 'resources/base/profile/main_pages.json' | ConvertFrom-Json
    $pack = Read-Entry 'pack.info' | ConvertFrom-Json
    $sourceApp = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'src/DailyRecord.Harmony/AppScope/app.json5') -Raw | ConvertFrom-Json
    if ($module.app.bundleName -ne 'com.yilaxin.e2day' -or $module.app.versionName -ne $sourceApp.app.versionName -or $module.app.versionCode -ne $sourceApp.app.versionCode) { throw 'Unexpected native app identity or version.' }
    if ($module.module.deviceTypes.Count -ne 1 -or $module.module.deviceTypes[0] -ne 'phone' -or $pages.src[0] -ne 'pages/NativeIndex') { throw 'Native phone entry does not match.' }
    if ($pack.summary.modules[0].apiVersion.compatible -ne 20 -or $pack.summary.modules[0].apiVersion.target -ne 20) { throw 'Unexpected device API requirement.' }
    if ($module.module.requestPermissions.Count -or $module.module.extensionAbilities.Count) { throw 'Unexpected permissions or backup extension.' }
    foreach ($name in @('ets/modules.abc','resources.index','resources/base/media/app_icon.png','resources/rawfile/lunar-dates.json')) {
        if (!$archive.GetEntry($name) -or $archive.GetEntry($name).Length -le 0) { throw "Missing native output: $name" }
    }
    if ($archive.Entries.FullName | Where-Object { $_ -match '\.(db|pfx|p12|key|jks|keystore|p7b|log)$' -or $_ -match '(^|/)(accounts\.json|signing\.local\.json5|app\.js|index\.html)$' }) { throw 'Unexpected private data or web runtime in native package.' }
    [pscustomobject]@{ Result = 'PASS'; Version = $module.app.versionName; MinimumApi = 20; TargetApi = 20; CompileSdk = $module.app.compileSdkVersion; Bytes = $package.Length; Sha256 = $expected[0]; Signing = 'Requires official signer inspection outside this ZIP check'; DeviceValidation = 'See 验证记录.md' } | ConvertTo-Json
} finally { $archive.Dispose() }
