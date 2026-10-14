[CmdletBinding()]
param()

# Downloads the two pinned third-party binaries that are not stored in the repository and
# verifies them: SHA-256 must match exactly and the Authenticode signature must be valid.
# Already present files are verified, not downloaded again.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

$dependencies = @(
    [pscustomobject]@{
        Name = 'Intel PresentMon 2.5.1'
        Url = 'https://github.com/GameTechDev/PresentMon/releases/download/v2.5.1/PresentMon-2.5.1-x64.exe'
        Path = Join-Path $projectRoot 'Assets\PresentMon-2.5.1-x64.exe'
        Sha256 = '9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191'
        Signer = 'CN=Intel Corporation,*'
    },
    [pscustomobject]@{
        Name = 'PawnIO 2.2.0 installer'
        Url = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe'
        Path = Join-Path $projectRoot 'installer\dependencies\PawnIO_setup.exe'
        Sha256 = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
        Signer = '*'
    }
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

foreach ($dependency in $dependencies) {
    if (-not (Test-Path -LiteralPath $dependency.Path -PathType Leaf)) {
        New-Item -ItemType Directory -Force -Path (Split-Path $dependency.Path) | Out-Null
        Write-Host "Downloading $($dependency.Name)..."
        $temporary = "$($dependency.Path).download"
        Invoke-WebRequest -Uri $dependency.Url -OutFile $temporary -UseBasicParsing
        Move-Item -LiteralPath $temporary -Destination $dependency.Path -Force
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dependency.Path).Hash
    if ($hash -ne $dependency.Sha256) {
        Remove-Item -LiteralPath $dependency.Path -Force
        throw "$($dependency.Name): SHA-256 mismatch. Expected $($dependency.Sha256), got $hash. The file was removed."
    }

    $signature = Get-AuthenticodeSignature -FilePath $dependency.Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike $dependency.Signer) {
        throw "$($dependency.Name): the digital signature is not valid ($($signature.Status))."
    }

    Write-Host "$($dependency.Name): OK"
}
