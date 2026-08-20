$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'KK.Var\KK.Var.csproj'
$releaseDirectory = Join-Path $PSScriptRoot 'artifacts\release'
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("kk-var-release-" + [Guid]::NewGuid().ToString('N'))
$version = '0.1.0'

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

try {
    foreach ($runtime in @('win-x86', 'win-x64')) {
        $publishDirectory = Join-Path $temporaryDirectory $runtime
        dotnet publish $projectPath -p:PublishProfile=$runtime -o $publishDirectory --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Publish failed for $runtime."
        }

        $archivePath = Join-Path $releaseDirectory "KK.Var-$version-$runtime.zip"
        Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -Force
        Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath |
            Select-Object Hash, Path
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory -PathType Container) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
