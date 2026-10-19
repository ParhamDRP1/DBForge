param([switch]$InstallCompiler, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) { & (Join-Path $root 'build.ps1') }
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$stage = Join-Path $root ('artifacts\package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage | Out-Null
foreach ($component in @('GUI', 'CLI', 'Library')) {
    $target = Join-Path $stage $component
    New-Item -ItemType Directory -Force $target | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $root 'release') -Filter '*.dll' -File | Copy-Item -Destination $target
    if ($component -ne 'Library') {
        $exe = if ($component -eq 'GUI') { 'DSNForge.Desktop.exe' } else { 'DSNForge.Cli.exe' }
        Copy-Item -LiteralPath (Join-Path $root ('release\' + $exe)) -Destination $target
        Copy-Item -LiteralPath (Join-Path $root ('release\' + $exe + '.config')) -Destination $target
    }
    foreach ($file in @('README.txt','README.md','README.fa.md','LICENSE','CHANGELOG.md','LIBRARY-USAGE.cs.txt')) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
    }
    foreach ($directory in @('docs','assets','examples')) {
        Copy-Item -LiteralPath (Join-Path $root $directory) -Destination $target -Recurse
    }
    $suffix = if ($component -eq 'Library') { 'net48' } else { 'win-x64' }
    Compress-Archive -Path (Join-Path $target '*') -DestinationPath (Join-Path $dist "DSNForge-$version-$component-$suffix.zip") -Force
}
dotnet pack (Join-Path $root 'src\DSNForge.Core\DSNForge.Core.csproj') -c Release --no-build -o $dist --nologo
if ($LASTEXITCODE -ne 0) { throw 'NuGet packaging failed' }
$compiler = $env:ISCC_PATH
if (-not $compiler) {
    $localCompiler = Join-Path $root 'artifacts\tools\inno\ISCC.exe'
    if (Test-Path -LiteralPath $localCompiler) { $compiler = $localCompiler }
    elseif (Get-Command ISCC.exe -ErrorAction SilentlyContinue) { $compiler = (Get-Command ISCC.exe).Source }
}
if (-not $compiler -and $InstallCompiler) {
    $toolRoot = Join-Path $root 'artifacts\tools'
    New-Item -ItemType Directory -Force $toolRoot | Out-Null
    $download = Join-Path $toolRoot 'inno-7.1.0.exe'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $download
    $expected = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) { throw 'Inno Setup checksum mismatch' }
    $installDir = Join-Path $toolRoot 'inno'
    $install = Start-Process -FilePath $download -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',('/DIR="' + $installDir + '"') -WindowStyle Hidden -PassThru -Wait
    if ($install.ExitCode -ne 0) { throw "Inno Setup installation failed: $($install.ExitCode)" }
    $compiler = Join-Path $installDir 'ISCC.exe'
}
if (-not $compiler -or -not (Test-Path -LiteralPath $compiler)) { throw 'Set ISCC_PATH or use -InstallCompiler to build the installer.' }
& $compiler "/DAppVersion=$version" "/DSourceRoot=$root" (Join-Path $root 'installer\DSNForge.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$commit = git -C $root rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0) { $commit = 'uncommitted-local-build' }
$manifest = [ordered]@{
    product = 'DSNForge'; version = $version; sourceCommit = $commit
    builtAtUtc = [DateTime]::UtcNow.ToString('o')
    workflowRun = if ($env:GITHUB_RUN_ID) { "https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID" } else { $null }
    sdk = (dotnet --version); framework = 'net48'; platform = 'win-x64'; signed = $false
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dist 'build-manifest.json') -Encoding UTF8
$lines = Get-ChildItem -LiteralPath $dist -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$lines | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Host "Release assets: $dist"
