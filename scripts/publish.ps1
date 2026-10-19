param([string]$GitHubCli = 'gh', [string]$Tag = 'v1.2')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$owner = 'ParhamDRP1'
$repository = "$owner/DBForge"
Push-Location $root
try {
    $login = & $GitHubCli api user --jq .login
    if ($LASTEXITCODE -ne 0 -or $login -ne $owner) { throw "Authenticate GitHub CLI as $owner before publishing." }
    $dirty = git status --porcelain
    if ($dirty) { throw 'Commit the reviewed source before publishing.' }
    & $GitHubCli repo view $repository --json name *> $null
    if ($LASTEXITCODE -eq 0) {
        $remote = git remote get-url origin 2>$null
        if ($remote -notin @("https://github.com/$repository.git", "https://github.com/$repository")) {
            throw 'Repository already exists and is not the configured origin; inspect it before proceeding.'
        }
        git push -u origin main
    } else {
        & $GitHubCli repo create $repository --public --source . --remote origin --push --description 'DSNForge: Windows GUI, CLI and .NET library for Db2 for z/OS LOAD, UNLOAD and dataset transfers.'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Repository publication failed' }
    & $GitHubCli repo edit $repository --add-topic db2 --add-topic mainframe --add-topic zos --add-topic dotnet --add-topic winforms --add-topic ftp
    git rev-parse --verify "refs/tags/$Tag" *> $null
    if ($LASTEXITCODE -ne 0) { git tag -a $Tag -m "DSNForge $Tag" }
    git push origin $Tag
    if ($LASTEXITCODE -ne 0) { throw 'Tag push failed' }
    Write-Host "Source published. Follow the tagged-source build at https://github.com/$repository/actions"
    Write-Host 'The workflow creates the release only after build, tests and packaging succeed.'
} finally {
    Pop-Location
}
