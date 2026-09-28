[CmdletBinding()]
param(
    [string] $Version = '1.0.1',
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $DotNetPath = 'dotnet',
    [string] $InnoCompilerPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactsRoot "publish\$Runtime"
$installerDirectory = Join-Path $artifactsRoot 'installer'
$projectPath = Join-Path $repositoryRoot 'src\AniT.App\AniT.App.csproj'
$installerScript = Join-Path $repositoryRoot 'installer\AniT.iss'

function Reset-ArtifactDirectory([string] $path) {
    $resolved = [System.IO.Path]::GetFullPath($path)
    $expectedPrefix = [System.IO.Path]::GetFullPath($artifactsRoot).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Diretório de artefato fora da raiz permitida: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

function Invoke-Checked([string] $executable, [string[]] $arguments) {
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao executar $executable (código $LASTEXITCODE)."
    }
}

Reset-ArtifactDirectory $publishDirectory
Reset-ArtifactDirectory $installerDirectory

& (Join-Path $PSScriptRoot 'New-InstallerAssets.ps1')

Invoke-Checked $DotNetPath @(
    'publish', $projectPath,
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '--output', $publishDirectory,
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    # A publicação por RID precisa de um grafo de dependências diferente do
    # restore comum do CI. Grave esse lock transitório em obj/ para não alterar
    # os packages.lock.json versionados da solução.
    '-p:NuGetLockFilePath=obj\packages.publish.lock.json',
    "-p:Version=$Version"
)

$requiredFiles = @(
    (Join-Path $publishDirectory 'AniT.exe'),
    (Join-Path $publishDirectory 'Player\MPC-HC\mpc-hc64.exe'),
    (Join-Path $publishDirectory 'Player\MPC-HC\COPYING.txt')
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Arquivo obrigatório ausente na publicação: $requiredFile"
    }
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $publishDirectory 'THIRD_PARTY_NOTICES.md')

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $InnoCompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or -not (Test-Path -LiteralPath $InnoCompilerPath)) {
    throw 'ISCC.exe não encontrado. Instale o Inno Setup 7 ou informe -InnoCompilerPath.'
}

Invoke-Checked $InnoCompilerPath @(
    "/DAppVersion=$Version",
    "/DSourceDir=$publishDirectory",
    "/DOutputDir=$installerDirectory",
    $installerScript
)

$installerPath = Join-Path $installerDirectory "AniT-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "O compilador não produziu o instalador esperado: $installerPath"
}

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
$hashLine = "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), [System.IO.Path]::GetFileName($installerPath)
Set-Content -LiteralPath (Join-Path $installerDirectory 'SHA256SUMS.txt') -Value $hashLine -Encoding ascii

Write-Output ''
Write-Output 'Instalador gerado com sucesso:'
Write-Output "  $installerPath"
Write-Output "  SHA-256: $($hash.Hash)"
