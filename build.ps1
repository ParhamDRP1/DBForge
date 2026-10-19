$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
dotnet build (Join-Path $root 'DSNForge.sln') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& (Join-Path $root 'tests\DSNForge.Tests\bin\Release\net48\DSNForge.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
$output = Join-Path $root 'release'
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($project in @('DSNForge.Desktop', 'DSNForge.Cli')) {
    $source = Join-Path $root ('src\' + $project + '\bin\Release\net48')
    Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in '.exe','.dll','.config','.pdb' } | Copy-Item -Destination $output -Force
}
Copy-Item -LiteralPath (Join-Path $root 'README.txt') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root 'LIBRARY-USAGE.cs.txt') -Destination $output -Force
foreach ($file in @('README.md','README.fa.md','CHANGELOG.md','LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $output -Force
}
foreach ($directory in @('docs','assets','examples')) {
    Copy-Item -LiteralPath (Join-Path $root $directory) -Destination $output -Recurse -Force
}
Write-Host ('Ready: ' + $output)
