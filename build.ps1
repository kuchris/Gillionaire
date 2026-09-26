$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet restore Gillionaire.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    dotnet build Gillionaire.sln --configuration Release --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    $output = Join-Path $PSScriptRoot 'Gillionaire/bin/x64/Release'
    $manifest = Get-Content -LiteralPath (Join-Path $output 'Gillionaire.json') -Raw | ConvertFrom-Json
    if ($manifest.InternalName -ne 'Gillionaire' -or $manifest.DalamudApiLevel -ne 15) {
        throw 'Unexpected plugin manifest.'
    }
    $files = @('Gillionaire.dll', 'Gillionaire.deps.json', 'Gillionaire.json', 'ECommons.dll') |
        ForEach-Object { Join-Path $output $_ }
    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing package file: $file" }
    }
    $artifacts = Join-Path $PSScriptRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $dependencyLicense = Join-Path $artifacts 'ECommons.LICENSE.md'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ECommons/LICENSE.md') -Destination $dependencyLicense -Force
    $files += $dependencyLicense
    $zipPath = Join-Path $artifacts "Gillionaire-$($manifest.AssemblyVersion).zip"
    Compress-Archive -LiteralPath $files -DestinationPath $zipPath -Force
    Write-Host "Package: $zipPath"
} finally { Pop-Location }
