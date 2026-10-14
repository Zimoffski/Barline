[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'Barline.csproj'
$installerScript = Join-Path $projectRoot 'installer\Barline.iss'
$version = ([xml](Get-Content -LiteralPath $projectFile -Raw)).Project.PropertyGroup.Version |
    Where-Object { $_ } |
    Select-Object -First 1
if (-not $version) {
    throw 'The <Version> property is missing in Barline.csproj.'
}
$setupOutput = Join-Path $projectRoot "Barline-$version-Setup.exe"

Push-Location $projectRoot
try {
    # PresentMon and the PawnIO installer are downloaded from their official releases and
    # verified (pinned SHA-256 and a valid signature); they are not stored in the repository.
    & (Join-Path $PSScriptRoot 'Get-Dependencies.ps1')

    dotnet format $projectFile --verify-no-changes --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Source formatting validation failed.'
    }

    dotnet publish $projectFile -p:PublishProfile=Windows-x64
    if ($LASTEXITCODE -ne 0) {
        throw 'Application publishing failed.'
    }

    $compilerCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $compiler = $compilerCandidates |
        Where-Object { [System.IO.File]::Exists($_) } |
        Select-Object -First 1

    if (-not $compiler) {
        throw 'Inno Setup 6 is required to build the installer.'
    }

    # Only the current installer is kept next to the sources.
    Get-ChildItem -LiteralPath $projectRoot -Filter 'Barline-*-Setup.exe' |
        Where-Object { $_.FullName -ne $setupOutput } |
        Remove-Item -Force

    & $compiler /Qp "/DAppVersion=$version" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw 'Installer compilation failed.'
    }

    $artifact = Get-Item -LiteralPath $setupOutput
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $artifact.FullName

    [pscustomobject]@{
        Installer = $artifact.FullName
        SizeMB = [Math]::Round($artifact.Length / 1MB, 1)
        SHA256 = $hash.Hash
    } | Format-List
}
finally {
    Pop-Location
}
