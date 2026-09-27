[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Destination
)

$ErrorActionPreference = 'Stop'
$version = '2.8.2'
$archiveName = "MPC-HC.$version.x64.zip"
$expectedSha256 = 'EEFEE5AC29FC33E6031E34E0E163E157212D272C1BCF576149D40C0C7ABB32F4'
$downloadUrl = "https://github.com/clsid2/mpc-hc/releases/download/$version/$archiveName"
$resolvedDestination = [System.IO.Path]::GetFullPath($Destination)

if (Test-Path -LiteralPath $resolvedDestination) {
    $existing = Get-ChildItem -LiteralPath $resolvedDestination -Force
    if ($existing.Count -gt 0) {
        throw "O destino precisa estar vazio: $resolvedDestination"
    }
}
else {
    New-Item -ItemType Directory -Path $resolvedDestination | Out-Null
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "AniT-MpcHc-$([Guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $temporaryRoot $archiveName
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
    $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualSha256 -ne $expectedSha256) {
        throw "Hash inesperado para $archiveName. Esperado: $expectedSha256; obtido: $actualSha256"
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $resolvedDestination
    $playerPath = Join-Path $resolvedDestination 'mpc-hc64.exe'
    if (-not (Test-Path -LiteralPath $playerPath)) {
        throw 'O pacote oficial não contém mpc-hc64.exe na raiz.'
    }

    $productVersion = (Get-Item -LiteralPath $playerPath).VersionInfo.ProductVersion
    if (-not $productVersion.StartsWith($version, [StringComparison]::Ordinal)) {
        throw "A versão extraída não corresponde a $version: $productVersion"
    }

    Write-Output "MPC-HC $productVersion validado em $resolvedDestination"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
